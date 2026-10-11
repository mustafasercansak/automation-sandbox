using System.Net.Http;

namespace AutomationSandbox.SiteAuditCli
{
    internal static class DictionaryDownloader
    {
        // Returns the base path SpellingChecker expects. Downloads only what is missing, and only when
        // allowed: a run with --no-download must work from a pre-provisioned directory with no network.
        public static async Task<string> EnsureAsync(
            string directory, string language, bool allowDownload, HttpClient http, TextWriter log, CancellationToken cancellationToken)
        {
            var basePath = DictionaryLocation.LocalBasePath(directory, language);
            foreach (var extension in new[] { "aff", "dic" })
            {
                var target = basePath + "." + extension;
                if (File.Exists(target))
                {
                    continue;
                }

                if (!allowDownload)
                {
                    throw new FileNotFoundException(
                        $"Dictionary file '{target}' is missing and --no-download was given.", target);
                }

                Directory.CreateDirectory(directory);
                var url = DictionaryLocation.RemoteUrl(language, extension);
                log.WriteLine($"Downloading {language}.{extension} dictionary ({url})...");

                // Write to a temp name and move, so an interrupted download can never leave a truncated
                // file that the next run would treat as a complete dictionary.
                var temp = target + ".download";
                using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException(
                            $"Could not download '{url}' (HTTP {(int)response.StatusCode}). Is '{language}' a language wooorm/dictionaries provides?");
                    }

                    await using var file = File.Create(temp);
                    await response.Content.CopyToAsync(file, cancellationToken);
                }

                File.Move(temp, target, overwrite: true);
            }

            return basePath;
        }
    }
}
