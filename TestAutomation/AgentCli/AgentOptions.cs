using System;
using System.Collections.Generic;
using AutomationSandbox.LlmHealing;

namespace AutomationSandbox.AgentCli
{
    internal sealed class AgentUsageException : Exception
    {
        public AgentUsageException(string message) : base(message)
        {
        }
    }

    internal sealed class AgentOptions
    {
        public bool ShowHelp { get; private set; }
        public string Command { get; private set; } = "";
        public string Repository { get; private set; } = "";
        public int Issue { get; private set; }
        public string? ProjectId { get; private set; }
        public string? RoutingFile { get; private set; }
        public List<string> AllowedLabels { get; } = new List<string>();

        public static AgentOptions Parse(string[] args)
        {
            var options = new AgentOptions();
            if (args.Length == 0)
            {
                throw new AgentUsageException("A command is required. The only command is \"triage\".");
            }

            if (args[0] == "-h" || args[0] == "--help")
            {
                options.ShowHelp = true;
                return options;
            }

            if (args[0] != "triage")
            {
                throw new AgentUsageException($"Unknown command \"{Shorten(args[0])}\". The only command is \"triage\".");
            }

            options.Command = "triage";
            for (var i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "-h":
                    case "--help":
                        options.ShowHelp = true;
                        return options;
                    case "--repo":
                        options.Repository = Value(args, ref i, arg);
                        break;
                    case "--issue":
                        var raw = Value(args, ref i, arg);
                        if (!int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) || number < 1)
                        {
                            throw new AgentUsageException("--issue must be a positive whole number.");
                        }

                        options.Issue = number;
                        break;
                    case "--project-id":
                        options.ProjectId = Value(args, ref i, arg);
                        break;
                    case "--routing-file":
                        options.RoutingFile = Value(args, ref i, arg);
                        break;
                    case "--allow-label":
                        options.AllowedLabels.Add(Value(args, ref i, arg));
                        break;
                    default:
                        throw new AgentUsageException(LooksLikeASecret(arg)
                            ? "Keys and tokens are read from environment variables only, never from an argument (they would reach the shell history and the process list)."
                            : $"Unknown argument \"{Shorten(arg)}\".");
                }
            }

            if (options.Repository.Length == 0)
            {
                throw new AgentUsageException("--repo owner/name is required.");
            }

            if (!GitHubIssueReader.IsValidRepository(options.Repository))
            {
                throw new AgentUsageException("--repo must look like owner/name.");
            }

            if (options.Issue == 0)
            {
                throw new AgentUsageException("--issue is required.");
            }

            if (options.ProjectId != null && !ProjectBoardReader.IsValidProjectId(options.ProjectId))
            {
                throw new AgentUsageException("--project-id must be a GitHub Projects node id (PVT_...).");
            }

            return options;
        }

        private static string Value(string[] args, ref int index, string flag)
        {
            if (index + 1 >= args.Length)
            {
                throw new AgentUsageException($"{flag} needs a value.");
            }

            index++;
            return args[index];
        }

        private static bool LooksLikeASecret(string arg)
        {
            var text = arg.ToLowerInvariant();
            return text.StartsWith("-", StringComparison.Ordinal) &&
                (text.Contains("token") || text.Contains("key") || text.Contains("secret") || text.Contains("password"));
        }

        // An unknown argument may be a pasted secret, so it is never echoed back in full.
        private static string Shorten(string text) => text.Length <= 24 ? text : text.Substring(0, 24) + "...";
    }
}
