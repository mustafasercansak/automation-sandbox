using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutomationSandbox.SiteAuditCli
{
    internal sealed class UsageException : Exception
    {
        public UsageException(string message) : base(message)
        {
        }
    }

    internal sealed class AuditOptions
    {
        public bool ShowHelp { get; private set; }
        public string Url { get; private set; } = "";
        public List<string> Languages { get; } = new List<string>();
        public List<string> AllowedWords { get; } = new List<string>();
        public int MaxPages { get; private set; } = 25;
        public int MaxDepth { get; private set; } = 3;
        public string OutputDirectory { get; private set; } = "site-audit-report";
        public string? DictionaryDirectory { get; private set; }
        public bool NoDownload { get; private set; }
        public bool InstallBrowser { get; private set; }
        public bool NoHeuristics { get; private set; }

        public static AuditOptions Parse(string[] args, Func<string, string[]> readAllLines)
        {
            var options = new AuditOptions();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "-h":
                    case "--help":
                        options.ShowHelp = true;
                        return options;
                    case "--url":
                        options.Url = Value(args, ref i, arg);
                        break;
                    case "--lang":
                        foreach (var language in Value(args, ref i, arg).Split(','))
                        {
                            var trimmed = language.Trim();
                            if (!DictionaryLocation.IsValidLanguageCode(trimmed))
                            {
                                throw new UsageException(
                                    "Invalid language code '" + trimmed + "'. Use codes like en, tr, en-GB, pt-PT.");
                            }

                            if (!options.Languages.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                            {
                                options.Languages.Add(trimmed);
                            }
                        }

                        break;
                    case "--allow-word":
                        options.AllowedWords.Add(Value(args, ref i, arg));
                        break;
                    case "--allow-file":
                        var path = Value(args, ref i, arg);
                        foreach (var line in readAllLines(path))
                        {
                            var word = line.Trim();
                            if (word.Length > 0 && !word.StartsWith("#", StringComparison.Ordinal))
                            {
                                options.AllowedWords.Add(word);
                            }
                        }

                        break;
                    case "--max-pages":
                        options.MaxPages = PositiveInt(Value(args, ref i, arg), arg, minimum: 1);
                        break;
                    case "--max-depth":
                        options.MaxDepth = PositiveInt(Value(args, ref i, arg), arg, minimum: 0);
                        break;
                    case "--output-dir":
                        options.OutputDirectory = Value(args, ref i, arg);
                        break;
                    case "--dictionary-dir":
                        options.DictionaryDirectory = Value(args, ref i, arg);
                        break;
                    case "--no-download":
                        options.NoDownload = true;
                        break;
                    case "--install-browser":
                        options.InstallBrowser = true;
                        break;
                    case "--no-heuristics":
                        options.NoHeuristics = true;
                        break;
                    default:
                        throw new UsageException("Unrecognized argument: " + arg);
                }
            }

            if (options.Url.Length == 0)
            {
                throw new UsageException("--url is required.");
            }

            if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new UsageException("--url must be an absolute http or https URL.");
            }

            if (options.Languages.Count == 0)
            {
                options.Languages.Add("en");
            }

            return options;
        }

        private static string Value(string[] args, ref int index, string name)
        {
            if (index + 1 >= args.Length)
            {
                throw new UsageException(name + " requires a value.");
            }

            index++;
            return args[index];
        }

        private static int PositiveInt(string text, string name, int minimum)
        {
            if (!int.TryParse(text, out var value) || value < minimum)
            {
                throw new UsageException(name + " must be an integer >= " + minimum + ".");
            }

            return value;
        }
    }
}
