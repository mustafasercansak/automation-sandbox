using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>The outcome of <see cref="LessonIndexBuilder.BuildAsync" />.</summary>
    public sealed class LessonIndexBuildResult
    {
        internal LessonIndexBuildResult(LessonIndex? index, string? error, int detailFailures, bool truncated)
        {
            Index = index;
            Error = error;
            DetailFailures = detailFailures;
            Truncated = truncated;
        }

        /// <summary>The index, or null when the build failed.</summary>
        public LessonIndex? Index { get; }

        /// <summary>Why the build failed, or null. Never contains the token.</summary>
        public string? Error { get; }

        /// <summary>How many pull requests are in the index without their files or review comments because those requests failed.</summary>
        public int DetailFailures { get; }

        /// <summary>True when the build stopped at the record or page limit, so older history is missing.</summary>
        public bool Truncated { get; }

        /// <summary>True when an index was built.</summary>
        public bool Success => Index != null;
    }

    /// <summary>Limits of an index build.</summary>
    public sealed class LessonIndexBuildOptions
    {
        /// <summary>Most records kept (newest closed first); at most <see cref="LessonIndex.MaxRecords" />.</summary>
        public int MaxRecords { get; set; } = 1000;

        /// <summary>Most pages of 100 closed items read.</summary>
        public int MaxPages { get; set; } = 20;

        /// <summary>Most pull requests whose changed files and review comments are fetched (two extra requests each).</summary>
        public int MaxPullRequestDetails { get; set; } = 300;

        /// <summary>Keeps only items closed before this moment. A replay of a past defect sets it to the day the defect was introduced, so
        /// the index holds no history the reviewer could not have had then.</summary>
        public DateTimeOffset? ClosedBefore { get; set; }
    }

    /// <summary>Builds a <see cref="LessonIndex" /> from the closed issues and pull requests of a repository through the GitHub REST
    /// API. It only reads. The host is fixed, the repository name is validated before it reaches a URL, a token travels only in a header,
    /// no redirect is followed by default, credential shapes in the text are masked before it is stored, and every failure is returned
    /// as a result.</summary>
    public static class LessonIndexBuilder
    {
        private const int MaxBodyLength = 1500;
        private const int MaxReviewLength = 1500;
        private const int MaxFilesPerPullRequest = 100;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        /// <summary>Reads the closed history of <paramref name="repository" /> (<c>owner/name</c>). A public repository needs no
        /// <paramref name="token" />, but an unauthenticated run is rate limited hard.</summary>
        public static async Task<LessonIndexBuildResult> BuildAsync(
            string repository,
            string? token = null,
            LessonIndexBuildOptions? options = null,
            HttpClient? httpClient = null,
            CancellationToken cancellationToken = default)
        {
            if (!GitHubIssueReader.IsValidRepository(repository))
            {
                return Fail("The repository must look like owner/name.");
            }

            var settings = options ?? new LessonIndexBuildOptions();
            if (settings.MaxRecords < 1 || settings.MaxRecords > LessonIndex.MaxRecords || settings.MaxPages < 1 || settings.MaxPages > 50 || settings.MaxPullRequestDetails < 0)
            {
                return Fail("The limits are out of range.");
            }

            var owned = httpClient == null;
            var client = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            try
            {
                var records = new List<LessonRecord>();
                var detailFailures = 0;
                var details = 0;
                var truncated = false;
                for (var page = 1; page <= settings.MaxPages && records.Count < settings.MaxRecords; page++)
                {
                    var listing = await GetAsync(client, $"https://api.github.com/repos/{repository}/issues?state=closed&sort=updated&direction=desc&per_page=100&page={page}", token, cancellationToken).ConfigureAwait(false);
                    if (listing.Error != null)
                    {
                        return Fail(listing.Error);
                    }

                    JsonDocument document;
                    try
                    {
                        document = JsonDocument.Parse(listing.Body!);
                    }
                    catch (JsonException)
                    {
                        return Fail("GitHub's answer was not valid JSON.");
                    }

                    using (document)
                    {
                        if (document.RootElement.ValueKind != JsonValueKind.Array)
                        {
                            return Fail("GitHub's answer was not a list.");
                        }

                        var items = document.RootElement.EnumerateArray().ToList();
                        foreach (var item in items)
                        {
                            if (records.Count >= settings.MaxRecords)
                            {
                                truncated = true;
                                break;
                            }

                            if (!TryRead(item, out var number, out var title, out var body, out var labels, out var closedAt, out var isPullRequest))
                            {
                                continue;
                            }

                            if (settings.ClosedBefore.HasValue && (!closedAt.HasValue || closedAt.Value >= settings.ClosedBefore.Value))
                            {
                                continue;
                            }

                            var files = new List<string>();
                            var review = "";
                            if (isPullRequest && details < settings.MaxPullRequestDetails)
                            {
                                details++;
                                var fileResult = await GetAsync(client, $"https://api.github.com/repos/{repository}/pulls/{number}/files?per_page={MaxFilesPerPullRequest}", token, cancellationToken).ConfigureAwait(false);
                                var commentResult = await GetAsync(client, $"https://api.github.com/repos/{repository}/pulls/{number}/comments?per_page=30", token, cancellationToken).ConfigureAwait(false);
                                if (fileResult.Error != null || commentResult.Error != null)
                                {
                                    detailFailures++;
                                }

                                if (fileResult.Error == null)
                                {
                                    files = Strings(fileResult.Body!, "filename").Take(MaxFilesPerPullRequest).ToList();
                                }

                                if (commentResult.Error == null)
                                {
                                    review = Clean(string.Join(" | ", Strings(commentResult.Body!, "body")), MaxReviewLength);
                                }
                            }

                            records.Add(new LessonRecord(isPullRequest ? LessonRecord.PullRequestKind : LessonRecord.IssueKind, number, Clean(title, 300), Clean(body, MaxBodyLength), labels, files, review, closedAt));
                        }

                        if (items.Count < 100)
                        {
                            break;
                        }

                        if (page == settings.MaxPages)
                        {
                            truncated = true;
                        }
                    }
                }

                return new LessonIndexBuildResult(new LessonIndex(repository, records), null, detailFailures, truncated);
            }
            finally
            {
                if (owned)
                {
                    client.Dispose();
                }
            }
        }

        private static bool TryRead(JsonElement item, out int number, out string title, out string body, out List<string> labels, out DateTimeOffset? closedAt, out bool isPullRequest)
        {
            number = 0;
            title = "";
            body = "";
            labels = new List<string>();
            closedAt = null;
            isPullRequest = false;
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("number", out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetInt32(out number) || number < 1)
            {
                return false;
            }

            title = item.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
            body = item.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()! : "";
            isPullRequest = item.TryGetProperty("pull_request", out var pr) && pr.ValueKind == JsonValueKind.Object;
            if (item.TryGetProperty("closed_at", out var c) && c.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(c.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var closed))
            {
                closedAt = closed;
            }

            if (item.TryGetProperty("labels", out var l) && l.ValueKind == JsonValueKind.Array)
            {
                foreach (var label in l.EnumerateArray().Take(30))
                {
                    if (label.ValueKind == JsonValueKind.Object && label.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    {
                        labels.Add(Clean(name.GetString()!, 100));
                    }
                }
            }

            return true;
        }

        private static IEnumerable<string> Strings(string json, string property)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                return new string[0];
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return new string[0];
                }

                return document.RootElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetProperty(property).GetString()!)
                    .ToList();
            }
        }

        // One line, control characters out, credential shapes masked, cut. A text whose masking times out is dropped, never stored unmasked.
        private static string Clean(string text, int max)
        {
            var builder = new StringBuilder(Math.Min(text.Length, max + 16));
            foreach (var c in text)
            {
                builder.Append(char.IsControl(c) ? ' ' : c);
            }

            string masked;
            try
            {
                masked = CiLogClassifier.MaskCredentials(builder.ToString());
                masked = Regex.Replace(masked, @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Trim();
            }
            catch (RegexMatchTimeoutException)
            {
                return "";
            }

            return masked.Length <= max ? masked : masked.Substring(0, max);
        }

        private static async Task<(string? Body, string? Error)> GetAsync(HttpClient client, string url, string? token, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.TryAddWithoutValidation("User-Agent", "AutomationSandbox");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token!.Trim());
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return (null, $"GitHub answered HTTP {(int)response.StatusCode}.");
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return (body, null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (null, "The request to GitHub timed out.");
            }
            catch (HttpRequestException)
            {
                return (null, "The request to GitHub failed.");
            }
        }

        private static LessonIndexBuildResult Fail(string error) => new LessonIndexBuildResult(null, error, 0, false);
    }
}
