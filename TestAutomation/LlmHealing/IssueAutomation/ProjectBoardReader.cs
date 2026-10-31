using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>How a board read ended.</summary>
    public enum BoardReadOutcome
    {
        /// <summary>The card was found; <see cref="BoardStatusResult.Status" /> is its Status, or null when it has none.</summary>
        Found,

        /// <summary>The board was read completely and the issue is not on it.</summary>
        NotOnBoard,

        /// <summary>The board could not be read reliably; nothing can be said about the issue.</summary>
        Failed,
    }

    /// <summary>The outcome of <see cref="ProjectBoardReader.ReadStatusAsync" />.</summary>
    public sealed class BoardStatusResult
    {
        internal BoardStatusResult(BoardReadOutcome outcome, string? status, string? error)
        {
            Outcome = outcome;
            Status = status;
            Error = error;
        }

        /// <summary>How the read ended.</summary>
        public BoardReadOutcome Outcome { get; }

        /// <summary>The card's Status column, only when <see cref="Outcome" /> is <see cref="BoardReadOutcome.Found" /> and the
        /// card has one.</summary>
        public string? Status { get; }

        /// <summary>Why the read failed, or null. Never contains the token.</summary>
        public string? Error { get; }
    }

    /// <summary>Reads the Status of one issue's card in a GitHub Projects (v2) board through the GraphQL API. Fail-closed:
    /// a board that cannot be read, or cards the token cannot see while the issue is missing, is a failure, never
    /// "not on the board" and never "Ready".</summary>
    public static class ProjectBoardReader
    {
        private const int MaxPages = 100;

        private static readonly Regex ProjectIdPattern = new Regex(@"\APVT_[A-Za-z0-9_-]{4,200}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private const string Query = @"query($project: ID!, $cursor: String) {
  node(id: $project) { ... on ProjectV2 {
    items(first: 100, after: $cursor) {
      nodes { content { ... on Issue { number repository { nameWithOwner } } }
        fieldValueByName(name: ""Status"") { ... on ProjectV2ItemFieldSingleSelectValue { name } } }
      pageInfo { hasNextPage endCursor }
    }
  } }
}";

        /// <summary>Reads the Status of issue <paramref name="number" /> of <paramref name="repository" /> in the board
        /// <paramref name="projectId" /> (a <c>PVT_...</c> node id). The token needs read access to the project and to
        /// the issues on it. With no <paramref name="httpClient" /> a client that follows no redirects is used.</summary>
        public static async Task<BoardStatusResult> ReadStatusAsync(
            string repository,
            int number,
            string projectId,
            string token,
            HttpClient? httpClient = null,
            CancellationToken cancellationToken = default)
        {
            if (!GitHubIssueReader.IsValidRepository(repository))
            {
                return Fail("The repository must look like owner/name.");
            }

            if (number < 1)
            {
                return Fail("The issue number must be positive.");
            }

            if (projectId == null || !ProjectIdPattern.IsMatch(projectId))
            {
                return Fail("The project id must be a GitHub Projects node id (PVT_...).");
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return Fail("A token with read access to the project is required.");
            }

            var owned = httpClient == null;
            var client = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            try
            {
                string? cursor = null;
                var unreadable = false;
                for (var page = 0; page < MaxPages; page++)
                {
                    var body = JsonSerializer.Serialize(new { query = Query, variables = new { project = projectId, cursor } });
                    using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql")
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    };
                    request.Headers.TryAddWithoutValidation("User-Agent", "AutomationSandbox");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

                    string text;
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        timeout.CancelAfter(Timeout);
                        try
                        {
                            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
                            if (!response.IsSuccessStatusCode)
                            {
                                return Fail($"GitHub answered HTTP {(int)response.StatusCode} for the project board.");
                            }

                            text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            return Fail("The request to GitHub timed out.");
                        }
                        catch (HttpRequestException)
                        {
                            return Fail("The request to GitHub failed.");
                        }
                    }

                    var parsed = ParsePage(text, repository, number, ref unreadable, out var hasNext, out var next);
                    if (parsed != null)
                    {
                        return parsed;
                    }

                    if (!hasNext)
                    {
                        return unreadable
                            ? Fail("The board has cards the token cannot read, so the issue may be hidden among them; check the token's repository permissions.")
                            : new BoardStatusResult(BoardReadOutcome.NotOnBoard, null, null);
                    }

                    if (string.IsNullOrEmpty(next) || next == cursor)
                    {
                        return Fail("The board's pagination did not advance.");
                    }

                    cursor = next;
                }

                return Fail("The board has more cards than the reader will page through.");
            }
            finally
            {
                if (owned)
                {
                    client.Dispose();
                }
            }
        }

        // Returns a result when the card is found or the page is invalid; null to continue with the next page.
        private static BoardStatusResult? ParsePage(string text, string repository, int number, ref bool unreadable, out bool hasNext, out string? next)
        {
            hasNext = false;
            next = null;
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) &&
                    errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                {
                    return Fail("GitHub reported errors for the project board query.");
                }

                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                    !data.TryGetProperty("node", out var node) || node.ValueKind != JsonValueKind.Object ||
                    !node.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object ||
                    !items.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array ||
                    !items.TryGetProperty("pageInfo", out var pageInfo) || pageInfo.ValueKind != JsonValueKind.Object ||
                    !pageInfo.TryGetProperty("hasNextPage", out var more) || (more.ValueKind != JsonValueKind.True && more.ValueKind != JsonValueKind.False))
                {
                    return Fail("GitHub's answer is not a readable project board, or the token cannot see the project.");
                }

                foreach (var item in nodes.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null)
                    {
                        unreadable = true;
                        continue;
                    }

                    if (content.ValueKind == JsonValueKind.Object &&
                        content.TryGetProperty("number", out var itemNumber) && itemNumber.ValueKind == JsonValueKind.Number && itemNumber.TryGetInt32(out var n) && n == number &&
                        content.TryGetProperty("repository", out var repo) && repo.ValueKind == JsonValueKind.Object &&
                        repo.TryGetProperty("nameWithOwner", out var name) && name.ValueKind == JsonValueKind.String &&
                        string.Equals(name.GetString(), repository, StringComparison.OrdinalIgnoreCase))
                    {
                        string? status = null;
                        if (item.TryGetProperty("fieldValueByName", out var field) && field.ValueKind == JsonValueKind.Object &&
                            field.TryGetProperty("name", out var statusName) && statusName.ValueKind == JsonValueKind.String)
                        {
                            status = statusName.GetString();
                        }

                        return new BoardStatusResult(BoardReadOutcome.Found, status, null);
                    }
                }

                hasNext = more.ValueKind == JsonValueKind.True;
                if (hasNext && pageInfo.TryGetProperty("endCursor", out var cursor) && cursor.ValueKind == JsonValueKind.String)
                {
                    next = cursor.GetString();
                }

                return null;
            }
            catch (JsonException)
            {
                return Fail("GitHub's answer is not valid JSON.");
            }
        }

        private static BoardStatusResult Fail(string error) => new BoardStatusResult(BoardReadOutcome.Failed, null, error);
    }
}
