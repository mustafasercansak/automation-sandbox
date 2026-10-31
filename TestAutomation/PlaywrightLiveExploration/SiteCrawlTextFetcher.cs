using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.PlaywrightLiveExploration
{
    // The default fetcher for robots.txt and sitemaps: a short timeout, a size limit, and no exception for an
    // ordinary failure, because a missing file is a normal answer and must not stop the crawl.
    internal static class SiteCrawlTextFetcher
    {
        private const long MaxBytes = 2_000_000;
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public static async Task<string?> FetchAsync(string url, string userAgent, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
                {
                    return null;
                }

                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return text.Length > MaxBytes ? null : text;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }
        }
    }
}
