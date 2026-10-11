using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AutomationSandbox.SiteAuditCli
{
    // Pure BCL logic, no I/O: kept separate from the downloader so ScenarioRunner (which also builds for
    // net48) can compile this file directly and unit test it.
    internal static class DictionaryLocation
    {
        // The dictionaries come from github.com/wooorm/dictionaries at a fixed commit, so a run next year
        // checks the same words as a run today and an upstream change cannot silently alter results.
        // Bump deliberately, together with the real-dictionary CI assertions.
        public const string PinnedCommit = "8cfea406b505e4d7df52d5a19bce525df98c54ab";

        // Language codes such as "en", "tr", "en-GB", "pt-PT". The pattern has no dot, slash or backslash,
        // so a value can never name a path outside the dictionary directory.
        private static readonly Regex LanguageCode = new Regex(
            "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$", RegexOptions.Compiled);

        public static bool IsValidLanguageCode(string? language)
        {
            return language != null && LanguageCode.IsMatch(language);
        }

        public static string RemoteUrl(string language, string extension)
        {
            RequireValid(language);
            return "https://raw.githubusercontent.com/wooorm/dictionaries/" + PinnedCommit +
                "/dictionaries/" + language + "/index." + extension;
        }

        // The base path SpellingChecker.FromDictionaryFiles expects: "<dir>/<language>" means
        // "<dir>/<language>.dic" + "<dir>/<language>.aff".
        public static string LocalBasePath(string directory, string language)
        {
            RequireValid(language);
            return Path.Combine(directory, language);
        }

        private static void RequireValid(string language)
        {
            if (!IsValidLanguageCode(language))
            {
                throw new ArgumentException("Not a valid dictionary language code: " + language, nameof(language));
            }
        }
    }
}
