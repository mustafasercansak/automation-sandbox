using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>One source file handed to the <c>docs</c> task: its repository-relative path and its text, with credential
    /// shapes already masked.</summary>
    public sealed class DocSource
    {
        internal DocSource(string path, string content)
        {
            Path = path;
            Content = content;
        }

        /// <summary>The repository-relative path with forward slashes; always one of the tracked files.</summary>
        public string Path { get; }

        /// <summary>The file text.</summary>
        public string Content { get; }
    }

    /// <summary>Where the <c>docs</c> task finds its sources: a checkout directory and the list of files git tracks in it. The list
    /// is read lazily, only when a <c>docs</c> issue actually needs it.</summary>
    public sealed class DocsSourceContext
    {
        /// <summary>Creates a context. <paramref name="listTrackedPaths" /> returns repository-relative paths.</summary>
        public DocsSourceContext(string rootDirectory, Func<IReadOnlyCollection<string>> listTrackedPaths)
        {
            RootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
            ListTrackedPaths = listTrackedPaths ?? throw new ArgumentNullException(nameof(listTrackedPaths));
        }

        /// <summary>The checkout directory.</summary>
        public string RootDirectory { get; }

        /// <summary>Lists the tracked files, as repository-relative paths.</summary>
        public Func<IReadOnlyCollection<string>> ListTrackedPaths { get; }
    }

    /// <summary>What <see cref="DocsRequest.Parse" /> found in an issue body.</summary>
    public sealed class DocsRequest
    {
        internal DocsRequest(IReadOnlyList<string> requestedPaths, string instruction, IReadOnlyList<string> problems)
        {
            RequestedPaths = requestedPaths;
            Instruction = instruction;
            Problems = problems;
        }

        /// <summary>The paths named by <c>Source: path</c> lines, in order, without duplicates. They are requests, not trusted
        /// paths: <see cref="DocsSourceCollector" /> accepts one only if it is exactly a tracked file.</summary>
        public IReadOnlyList<string> RequestedPaths { get; }

        /// <summary>The rest of the body, without the <c>Source:</c> lines: what the author wants written. Untrusted text.</summary>
        public string Instruction { get; }

        /// <summary>Why the request cannot be used; empty when it can.</summary>
        public IReadOnlyList<string> Problems { get; }

        private static readonly Regex SourceLine = new Regex(@"^\s*(?:[-*]\s*)?sources?\s*:\s*(?<path>\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        /// <summary>Reads the <c>Source: path</c> lines (one path per line, an optional list marker allowed) out of
        /// <paramref name="body" />.</summary>
        public static DocsRequest Parse(string? body)
        {
            var paths = new List<string>();
            var rest = new List<string>();
            foreach (var line in (body ?? "").Replace("\r", "").Split('\n'))
            {
                Match match;
                try
                {
                    match = SourceLine.Match(line);
                }
                catch (RegexMatchTimeoutException)
                {
                    match = Match.Empty;
                }

                if (!match.Success)
                {
                    rest.Add(line);
                    continue;
                }

                var path = match.Groups["path"].Value.Trim('`', '"', '\'');
                if (!paths.Contains(path))
                {
                    paths.Add(path);
                }
            }

            var problems = new List<string>();
            if (paths.Count == 0)
            {
                problems.Add("The issue names no source file. Add one line per file, for example \"Source: docs/guide.md\".");
            }
            else if (paths.Count > DocsSourceCollector.MaxFiles)
            {
                problems.Add($"The issue names {paths.Count} source files; at most {DocsSourceCollector.MaxFiles} are allowed.");
            }

            return new DocsRequest(paths, string.Join("\n", rest).Trim(), problems);
        }
    }

    /// <summary>The outcome of <see cref="DocsSourceCollector.Collect" />.</summary>
    public sealed class DocsSourceResult
    {
        internal DocsSourceResult(IReadOnlyList<DocSource> sources, IReadOnlyList<string> problems)
        {
            Sources = sources;
            Problems = problems;
        }

        /// <summary>The accepted sources; empty when there is any problem.</summary>
        public IReadOnlyList<DocSource> Sources { get; }

        /// <summary>Every reason a requested file was refused. The collection is all or nothing: one refused file fails it,
        /// because a draft written from part of what was asked for would claim more than it saw.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>True when every requested file was accepted and there is at least one.</summary>
        public bool Success => Problems.Count == 0 && Sources.Count > 0;
    }

    /// <summary>Reads the source files for the <c>docs</c> task. The paths come from an issue, so they are untrusted: a path is
    /// never joined to the checkout directory unless it is exactly equal to one of the files git tracks, and then only after it
    /// passed the checks below. Nothing here writes anything.</summary>
    public static class DocsSourceCollector
    {
        /// <summary>Most files per task.</summary>
        public const int MaxFiles = 8;

        /// <summary>Largest single file, in bytes.</summary>
        public const int MaxFileBytes = 20000;

        /// <summary>Most characters over all files.</summary>
        public const int MaxTotalCharacters = 60000;

        private static readonly string[] TextExtensions =
        {
            ".md", ".txt", ".cs", ".csproj", ".props", ".targets", ".json", ".yml", ".yaml", ".xml", ".html", ".css", ".js", ".mjs", ".ts", ".py", ".ps1", ".sh", ".toml",
        };

        // Names that usually hold secrets. A tracked file can still hold one by mistake, so these are refused outright and the
        // content that is read is also masked.
        private static readonly Regex SecretsName = new Regex(
            @"(?:^|/)(?:\.env(?:\..*)?|\.npmrc|\.netrc|\.pypirc|\.git-credentials|id_(?:rsa|dsa|ecdsa|ed25519)[^/]*|[^/]*\.(?:pem|key|pfx|p12|jks|keystore|snk|asc|gpg|kdbx)|[^/]*(?:secret|credential)[^/]*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        /// <summary>Collects <paramref name="requestedPaths" /> under <paramref name="rootDirectory" />. A path is accepted only
        /// when it is a tracked text file, not a credentials-looking file, not a symbolic link (nor below one), at most
        /// <see cref="MaxFileBytes" /> bytes and free of binary content, and the total stays within <see cref="MaxTotalCharacters" />.</summary>
        public static DocsSourceResult Collect(string rootDirectory, IReadOnlyCollection<string> trackedPaths, IReadOnlyList<string> requestedPaths)
        {
            if (rootDirectory == null)
            {
                throw new ArgumentNullException(nameof(rootDirectory));
            }

            if (trackedPaths == null)
            {
                throw new ArgumentNullException(nameof(trackedPaths));
            }

            if (requestedPaths == null)
            {
                throw new ArgumentNullException(nameof(requestedPaths));
            }

            var problems = new List<string>();
            if (requestedPaths.Count == 0)
            {
                problems.Add("No source file was requested.");
            }

            if (requestedPaths.Count > MaxFiles)
            {
                problems.Add($"{requestedPaths.Count} source files were requested; at most {MaxFiles} are allowed.");
            }

            if (problems.Count > 0)
            {
                return new DocsSourceResult(new DocSource[0], problems);
            }

            var tracked = new HashSet<string>(trackedPaths.Select(p => p.Replace('\\', '/')), StringComparer.Ordinal);
            var root = Path.GetFullPath(rootDirectory);
            var sources = new List<DocSource>();
            var total = 0;
            foreach (var requested in requestedPaths)
            {
                var label = Show(requested);
                if (!IsPlainRelativePath(requested) || !tracked.Contains(requested))
                {
                    problems.Add($"\"{label}\" is not a file tracked by git in this repository.");
                    continue;
                }

                var reason = Refuse(requested);
                if (reason != null)
                {
                    problems.Add($"\"{label}\" {reason}");
                    continue;
                }

                var full = Path.GetFullPath(Path.Combine(root, requested.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    problems.Add($"\"{label}\" is outside the checkout.");
                    continue;
                }

                if (HasSymbolicLink(root, requested))
                {
                    problems.Add($"\"{label}\" is, or is below, a symbolic link.");
                    continue;
                }

                var info = new FileInfo(full);
                if (!info.Exists)
                {
                    problems.Add($"\"{label}\" is tracked but is not in the checkout.");
                    continue;
                }

                if (info.Length > MaxFileBytes)
                {
                    problems.Add($"\"{label}\" is {info.Length} bytes; at most {MaxFileBytes} are allowed.");
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(full, new UTF8Encoding(false));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    problems.Add($"\"{label}\" could not be read.");
                    continue;
                }

                if (text.IndexOf('\0') >= 0)
                {
                    problems.Add($"\"{label}\" looks binary.");
                    continue;
                }

                try
                {
                    text = CiLogClassifier.MaskCredentials(text);
                }
                catch (RegexMatchTimeoutException)
                {
                    problems.Add($"\"{label}\" could not be checked for credentials in time.");
                    continue;
                }

                total += text.Length;
                if (total > MaxTotalCharacters)
                {
                    problems.Add($"The sources together are longer than {MaxTotalCharacters} characters.");
                    break;
                }

                sources.Add(new DocSource(requested, text));
            }

            return problems.Count > 0 ? new DocsSourceResult(new DocSource[0], problems) : new DocsSourceResult(sources, problems);
        }

        // Only a plain forward-slash path: no rooted path, drive letter, backslash, empty or dot segment, or control character.
        private static bool IsPlainRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 260 || path.StartsWith("/", StringComparison.Ordinal) || path.Contains("\\") || path.Contains(":"))
            {
                return false;
            }

            if (path.Any(char.IsControl))
            {
                return false;
            }

            return path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
        }

        private static string? Refuse(string path)
        {
            if (path.Split('/').Any(s => string.Equals(s, ".git", StringComparison.OrdinalIgnoreCase)))
            {
                return "is inside the .git directory.";
            }

            bool looksSecret;
            try
            {
                looksSecret = SecretsName.IsMatch(path);
            }
            catch (RegexMatchTimeoutException)
            {
                looksSecret = true;
            }

            if (looksSecret)
            {
                return "looks like a credentials file and is never sent to a model.";
            }

            var extension = Path.GetExtension(path);
            return TextExtensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase))
                ? null
                : $"has the extension \"{Show(extension)}\", which is not a text file type this task reads.";
        }

        private static bool HasSymbolicLink(string root, string relative)
        {
            var current = root;
            foreach (var segment in relative.Split('/'))
            {
                current = Path.Combine(current, segment);
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return false;
                }
            }

            return false;
        }

        // A path from an issue is shown in a problem message: short, one line.
        private static string Show(string text)
        {
            var clean = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
            return clean.Length <= 80 ? clean : clean.Substring(0, 80) + "...";
        }
    }
}
