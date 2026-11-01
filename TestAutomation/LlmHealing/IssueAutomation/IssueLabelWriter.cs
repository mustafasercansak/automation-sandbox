using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>The outcome of <see cref="IssueLabelWriter.AddNeedsHumanAsync" />.</summary>
    public sealed class IssueLabelResult
    {
        internal IssueLabelResult(bool success, string? error)
        {
            Success = success;
            Error = error;
        }

        /// <summary>True when GitHub added the label.</summary>
        public bool Success { get; }

        /// <summary>Why the label was not added, or null. Never contains the token.</summary>
        public string? Error { get; }
    }

    /// <summary>Adds the one label <see cref="AgentLabels.NeedsHuman" /> to an issue through the GitHub REST API. There is
    /// deliberately no parameter for the label name: this is the only label the agent may write, and it only ever adds, so a
    /// caller cannot widen it into a general label editor. The host is fixed, the repository name is validated before it reaches
    /// the URL, the token travels only in a header and never appears in an error, no redirect is followed by default, and every
    /// failure is returned as a result.</summary>
    public static class IssueLabelWriter
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        /// <summary>Adds <c>Agent::Needs-Human</c> to issue <paramref name="number" /> of <paramref name="repository" />. Adding a
        /// label the issue already has changes nothing. The token needs permission to write issues (<c>issues: write</c>, or the
        /// <c>public_repo</c> scope of a classic token for a public repository).</summary>
        public static async Task<IssueLabelResult> AddNeedsHumanAsync(
            string repository,
            int number,
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

            if (string.IsNullOrWhiteSpace(token))
            {
                return Fail("A token that may write issue labels is required.");
            }

            var owned = httpClient == null;
            var client = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repository}/issues/{number}/labels")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { labels = new[] { AgentLabels.NeedsHuman } }), Encoding.UTF8, "application/json"),
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.TryAddWithoutValidation("User-Agent", "AutomationSandbox");
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(Timeout);
                try
                {
                    using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        return new IssueLabelResult(true, null);
                    }

                    var code = (int)response.StatusCode;
                    return Fail($"GitHub answered HTTP {code}" + (code == 401 || code == 403 || code == 404
                        ? "; the token may lack permission to write issue labels on this repository."
                        : "."));
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
            finally
            {
                if (owned)
                {
                    client.Dispose();
                }
            }
        }

        private static IssueLabelResult Fail(string error) => new IssueLabelResult(false, error);
    }
}
