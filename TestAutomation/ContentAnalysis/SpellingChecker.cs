using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

namespace AutomationSandbox.ContentAnalysis
{
    /// <summary>Dictionary-based spell checker: finds misspelled words in captured page text with no API key,
    /// no network call and no text leaving the machine. Built from Hunspell dictionaries (a <c>.dic</c> and
    /// <c>.aff</c> pair per language), which handle inflected and agglutinative languages such as Turkish.
    /// It judges individual words only; grammar and meaning need an <see cref="IContentAnalysisProvider" />.</summary>
    public sealed class SpellingChecker
    {
        private const int MaxSuggestions = 3;
        private const int MinimumWordLength = 3;
        private static readonly char[] CompoundMarkers = { ' ', '-' };

        private static readonly Regex NonProse = new(
            @"https?://\S+|www\.\S+|\S+@\S+\.\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Word = new(
            @"[\p{L}\p{M}]+(?:['’][\p{L}\p{M}]+)*", RegexOptions.Compiled);

        private readonly IReadOnlyList<WordList> _dictionaries;
        private readonly HashSet<string> _allowedWords;

        private SpellingChecker(IReadOnlyList<WordList> dictionaries, HashSet<string> allowedWords)
        {
            _dictionaries = dictionaries;
            _allowedWords = allowedWords;
        }

        /// <summary>Loads one or more Hunspell dictionaries. Each entry is a base path without extension:
        /// <c>dir/tr</c> loads <c>dir/tr.dic</c> and <c>dir/tr.aff</c>. A word is accepted when ANY loaded
        /// dictionary accepts it, so a Turkish page that contains English terms can load both; the trade-off is
        /// that a typo which happens to be a valid word in the other language is not reported. Suggestions are
        /// interleaved across all dictionaries, so a misspelled English word on a Turkish page still gets an
        /// English suggestion.</summary>
        /// <param name="dictionaryBasePaths">Base paths of the dictionaries to load.</param>
        /// <param name="allowedWords">Brand names, product names and jargon to never report (case-insensitive).</param>
        /// <exception cref="ArgumentException">No dictionary was supplied.</exception>
        /// <exception cref="FileNotFoundException">A <c>.dic</c> or <c>.aff</c> file does not exist.</exception>
        public static SpellingChecker FromDictionaryFiles(
            IEnumerable<string> dictionaryBasePaths,
            IEnumerable<string>? allowedWords = null)
        {
            if (dictionaryBasePaths == null)
            {
                throw new ArgumentNullException(nameof(dictionaryBasePaths));
            }

            var dictionaries = new List<WordList>();
            foreach (var basePath in dictionaryBasePaths)
            {
                var dic = basePath + ".dic";
                var aff = basePath + ".aff";
                foreach (var file in new[] { dic, aff })
                {
                    if (!File.Exists(file))
                    {
                        throw new FileNotFoundException("Spelling dictionary file not found.", file);
                    }
                }

                dictionaries.Add(WordList.CreateFromFiles(dic, aff));
            }

            if (dictionaries.Count == 0)
            {
                throw new ArgumentException("At least one dictionary is required.", nameof(dictionaryBasePaths));
            }

            var allowed = new HashSet<string>(
                (allowedWords ?? Enumerable.Empty<string>()).Where(w => !string.IsNullOrWhiteSpace(w)),
                StringComparer.OrdinalIgnoreCase);
            return new SpellingChecker(dictionaries, allowed);
        }

        /// <summary>Returns one issue per distinct misspelled word in <paramref name="passage" />.</summary>
        public IReadOnlyList<ContentIssue> Check(ContentPassage passage)
        {
            if (passage == null)
            {
                throw new ArgumentNullException(nameof(passage));
            }

            var issues = new List<ContentIssue>();
            if (string.IsNullOrWhiteSpace(passage.Text))
            {
                return issues;
            }

            var text = NonProse.Replace(passage.Text, new string(' ', 1));
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in Word.Matches(text))
            {
                var token = match.Value.Replace('’', '\'');
                if (ShouldSkip(text, match, token) || IsAccepted(token) || !reported.Add(token))
                {
                    continue;
                }

                var suggestions = Suggest(token);
                var message = suggestions.Count == 0
                    ? $"\"{token}\" may be misspelled."
                    : $"\"{token}\" may be misspelled. Suggestions: {string.Join(", ", suggestions)}.";
                issues.Add(new ContentIssue(passage.CssSelector, passage.Text, "Spelling", message, ContentIssueSource.Dictionary));
            }

            return issues;
        }

        // Each guard exists because the dictionary alone reports these as errors although they are not
        // content defects: acronyms, camelCase/brand casing, identifier fragments, and very short tokens.
        private bool ShouldSkip(string text, Match match, string token)
        {
            if (token.Length < MinimumWordLength)
            {
                return true;
            }

            if (HasDigitOrUnderscoreNeighbor(text, match))
            {
                return true;
            }

            var hasLower = false;
            for (var i = 0; i < token.Length; i++)
            {
                var c = token[i];
                if (char.IsLower(c))
                {
                    hasLower = true;
                }
                else if (char.IsUpper(c) && i > 0 && token[i - 1] != '\'')
                {
                    // An uppercase letter after the first one: GitHub, iPhone, McDonald, or an acronym.
                    return true;
                }
            }

            if (!hasLower)
            {
                return true;
            }

            var apostrophe = token.IndexOf('\'');
            var stem = apostrophe > 0 ? token.Substring(0, apostrophe) : token;
            return _allowedWords.Contains(token) || _allowedWords.Contains(stem);
        }

        private static bool HasDigitOrUnderscoreNeighbor(string text, Match match)
        {
            var before = match.Index - 1;
            var after = match.Index + match.Length;
            return (before >= 0 && IsDigitOrUnderscore(text[before])) ||
                (after < text.Length && IsDigitOrUnderscore(text[after]));
        }

        private static bool IsDigitOrUnderscore(char c)
        {
            return char.IsDigit(c) || c == '_';
        }

        private bool IsAccepted(string token)
        {
            return _dictionaries.Any(dictionary => dictionary.Check(token));
        }

        // A bilingual page cannot tell which dictionary a misspelling "belongs" to, so suggestions are
        // interleaved: the best one from each dictionary first, then the second best, and so on. Compound
        // splits ("reci eve", "reci-eve") are dropped unless the original word itself had a space or hyphen:
        // they are what an unrelated dictionary produces for a foreign word, and never a useful fix.
        private IReadOnlyList<string> Suggest(string token)
        {
            var wordIsCompound = token.IndexOfAny(CompoundMarkers) >= 0;
            var perDictionary = _dictionaries
                .Select(dictionary => dictionary.Suggest(token)
                    .Where(suggestion => wordIsCompound || suggestion.IndexOfAny(CompoundMarkers) < 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList())
                .ToList();

            var result = new List<string>();
            for (var rank = 0; result.Count < MaxSuggestions; rank++)
            {
                var anyAtThisRank = false;
                foreach (var suggestions in perDictionary)
                {
                    if (rank >= suggestions.Count)
                    {
                        continue;
                    }

                    anyAtThisRank = true;
                    if (!result.Contains(suggestions[rank], StringComparer.Ordinal))
                    {
                        result.Add(suggestions[rank]);
                        if (result.Count == MaxSuggestions)
                        {
                            break;
                        }
                    }
                }

                if (!anyAtThisRank)
                {
                    break;
                }
            }

            return result;
        }
    }
}
