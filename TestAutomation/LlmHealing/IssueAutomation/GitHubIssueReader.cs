using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>The outcome of <see cref="GitHubIssueReader.ReadAsync" />.</summary>
    public sealed class GitHubIssueReadResult
    {
        internal GitHubIssueReadResult(IssueSnapshot? issue, string? error)
        {
            Issue = issue;
            Error = error;
        }

        /// <summary>True when <see cref="Issue" /> was read and validated.</summary>
        public bool Success => Issue != null;

        /// <summary>The issue, or null on failure.</summary>
        public IssueSnapshot? Issue { get; }

        /// <summary>Why the read failed, or null. Never contains the token.</summary>
        public string? Error { get; }
    }

    /// <summary>Reads one issue from the GitHub REST API. The host is fixed, the repository name is validated before it
    /// reaches the URL, an optional token travels only in a header, the response is validated before use, and
    /// every failure is returned as a result.</summary>
    public static class GitHubIssueReader
    {
        // \z, not $: in .NET $ also matches before a trailing newline, which would let "owner/repo\n" through.
        private static readonly Regex RepositoryPattern = new Regex(
            @"\A[A-Za-z0-9_-]+/(?!\.{1,2}\z)[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        /// <summary>Fetches issue <paramref name="number" /> of <paramref name="repository" /> (<c>owner/name</c>). With no
        /// <paramref name="httpClient" /> a client that follows no redirects is used; a supplied client must be
        /// configured that way by the caller. A public repository needs no <paramref name="token" />.</summary>
        public static async Task<GitHubIssueReadResult> ReadAsync(
            string repository,
            int number,
            string? token = null,
            HttpClient? httpClient = null,
            CancellationToken cancellationToken = default)
        {
            if (repository == null || !RepositoryPattern.IsMatch(repository))
            {
                return Fail("The repository must look like owner/name.");
            }

            if (number < 1)
            {
                return Fail("The issue number must be positive.");
            }

            var owned = httpClient == null;
            var client = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/issues/{number}");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.TryAddWithoutValidation("User-Agent", "AutomationSandbox");
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token!.Trim());
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(Timeout);
                string body;
                try
                {
                    using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        return Fail($"GitHub answered HTTP {(int)response.StatusCode}.");
                    }

                    body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return Fail("The request to GitHub timed out.");
                }
                catch (HttpRequestException)
                {
                    return Fail("The request to GitHub failed.");
                }

                return Parse(body);
            }
            finally
            {
                if (owned)
                {
                    client.Dispose();
                }
            }
        }

        private static GitHubIssueReadResult Parse(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("number", out var number) || number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var n) || n < 1 ||
                    !root.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array)
                {
                    return Fail("GitHub's answer is not a valid issue.");
                }

                var names = new List<string>();
                foreach (var label in labels.EnumerateArray())
                {
                    if (label.ValueKind != JsonValueKind.Object || !label.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
                    {
                        return Fail("GitHub's answer is not a valid issue.");
                    }

                    names.Add(name.GetString()!);
                }

                return new GitHubIssueReadResult(
                    new IssueSnapshot(n, title.GetString()!, string.Equals(state.GetString(), "open", StringComparison.OrdinalIgnoreCase), root.TryGetProperty("pull_request", out _), names),
                    null);
            }
            catch (JsonException)
            {
                return Fail("GitHub's answer is not valid JSON.");
            }
        }

        private static GitHubIssueReadResult Fail(string error) => new GitHubIssueReadResult(null, error);
    }
}
