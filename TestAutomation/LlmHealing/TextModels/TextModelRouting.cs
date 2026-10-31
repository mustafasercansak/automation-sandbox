using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>The default drafting model and optional reviewer for one task kind.</summary>
    public sealed class TextTaskDefault
    {
        /// <summary>Creates a default.</summary>
        public TextTaskDefault(string model, string? reviewer)
        {
            Model = model;
            Reviewer = reviewer;
        }

        /// <summary>Name of the default drafting model.</summary>
        public string Model { get; }

        /// <summary>Name of the default reviewer, or null for no review.</summary>
        public string? Reviewer { get; }
    }

    /// <summary>Which task kinds each model may serve, and optional defaults per task kind. Read from JSON with
    /// <see cref="Parse" />; an invalid file is rejected with a message that says what is wrong, because a routing
    /// mistake that is only noticed when a model answers has already spent quota on the wrong model.</summary>
    public sealed class TextModelRoutingConfig
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _modelTasks;
        private readonly Dictionary<string, TextTaskDefault> _defaults;

        private TextModelRoutingConfig(Dictionary<string, IReadOnlyList<string>> modelTasks, Dictionary<string, TextTaskDefault> defaults)
        {
            _modelTasks = modelTasks;
            _defaults = defaults;
        }

        /// <summary>No restrictions and no defaults: every registered model may serve every task kind, and a task
        /// needs a <c>Model::</c> label.</summary>
        public static TextModelRoutingConfig Empty { get; } = new TextModelRoutingConfig(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, TextTaskDefault>(StringComparer.OrdinalIgnoreCase));

        /// <summary>True when the file lists no model, so nothing is restricted.</summary>
        public bool IsEmpty => _modelTasks.Count == 0;

        /// <summary>True when <paramref name="modelName" /> may serve <paramref name="taskKind" />. With an empty
        /// configuration every model may.</summary>
        public bool Allows(string modelName, string taskKind)
        {
            if (IsEmpty)
            {
                return true;
            }

            return _modelTasks.TryGetValue(modelName, out var kinds) &&
                kinds.Any(kind => string.Equals(kind, taskKind, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True when the file lists <paramref name="modelName" />, ignoring case.</summary>
        public bool Lists(string modelName) => _modelTasks.ContainsKey(modelName);

        /// <summary>The default for <paramref name="taskKind" />, or null.</summary>
        public TextTaskDefault? DefaultFor(string taskKind)
        {
            return _defaults.TryGetValue(taskKind, out var value) ? value : null;
        }

        /// <summary>Reads the configuration. Expected shape:
        /// <c>{"models":{"Gemini":{"tasks":["docs"]}},"defaults":{"docs":{"model":"Gemini","reviewer":"Mistral"}}}</c>.
        /// Comments and trailing commas are accepted. Throws <see cref="FormatException" /> for an unknown task
        /// kind, a duplicate model name, or a default that names a model the file does not allow for that task
        /// kind.</summary>
        public static TextModelRoutingConfig Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new FormatException("The routing configuration is empty.");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (JsonException ex)
            {
                throw new FormatException("The routing configuration is not valid JSON: " + ex.Message);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException("The routing configuration must be a JSON object.");
                }

                var modelTasks = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("models", out var models))
                {
                    if (models.ValueKind != JsonValueKind.Object)
                    {
                        throw new FormatException("\"models\" must be an object.");
                    }

                    foreach (var model in models.EnumerateObject())
                    {
                        var name = model.Name.Trim();
                        if (name.Length == 0 || modelTasks.ContainsKey(name))
                        {
                            throw new FormatException(name.Length == 0
                                ? "A model name must not be empty."
                                : $"The model \"{name}\" is listed twice (names are compared ignoring case).");
                        }

                        if (model.Value.ValueKind != JsonValueKind.Object ||
                            !model.Value.TryGetProperty("tasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
                        {
                            throw new FormatException($"The model \"{name}\" needs a \"tasks\" array.");
                        }

                        var kinds = new List<string>();
                        foreach (var task in tasks.EnumerateArray())
                        {
                            var kind = task.ValueKind == JsonValueKind.String ? TextTaskKinds.Normalize(task.GetString()) : null;
                            if (kind == null)
                            {
                                throw new FormatException($"The model \"{name}\" lists an unknown task kind (known: {string.Join(", ", TextTaskKinds.All)}).");
                            }

                            if (!kinds.Contains(kind))
                            {
                                kinds.Add(kind);
                            }
                        }

                        modelTasks.Add(name, kinds);
                    }
                }

                var defaults = new Dictionary<string, TextTaskDefault>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("defaults", out var defaultsElement))
                {
                    if (defaultsElement.ValueKind != JsonValueKind.Object)
                    {
                        throw new FormatException("\"defaults\" must be an object.");
                    }

                    foreach (var entry in defaultsElement.EnumerateObject())
                    {
                        var kind = TextTaskKinds.Normalize(entry.Name);
                        if (kind == null)
                        {
                            throw new FormatException($"\"defaults\" names an unknown task kind \"{entry.Name}\" (known: {string.Join(", ", TextTaskKinds.All)}).");
                        }

                        var model = ReadString(entry.Value, "model");
                        if (model == null)
                        {
                            throw new FormatException($"The default for \"{kind}\" needs a \"model\".");
                        }

                        var reviewer = ReadString(entry.Value, "reviewer");
                        CheckAllowed(modelTasks, model, kind, "default model");
                        if (reviewer != null)
                        {
                            CheckAllowed(modelTasks, reviewer, kind, "default reviewer");
                            if (string.Equals(model, reviewer, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new FormatException($"The default for \"{kind}\" uses the same model as drafter and reviewer.");
                            }
                        }

                        defaults[kind] = new TextTaskDefault(model, reviewer);
                    }
                }

                return new TextModelRoutingConfig(modelTasks, defaults);
            }
        }

        private static string? ReadString(JsonElement element, string property)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = value.GetString()?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static void CheckAllowed(Dictionary<string, IReadOnlyList<string>> modelTasks, string model, string kind, string role)
        {
            if (!modelTasks.TryGetValue(model, out var kinds) || !kinds.Contains(kind))
            {
                throw new FormatException($"The {role} \"{model}\" for \"{kind}\" is not listed as allowed to serve \"{kind}\".");
            }
        }
    }

    /// <summary>The outcome of routing one task: the models to use, or the reasons it must stop.</summary>
    public sealed class TextModelRoutingResult
    {
        internal TextModelRoutingResult(ITextModel? drafter, ITextModel? reviewer, IReadOnlyList<string> problems)
        {
            Drafter = drafter;
            Reviewer = reviewer;
            Problems = problems;
        }

        /// <summary>True when the task can go ahead: <see cref="Drafter" /> is set and there are no problems.</summary>
        public bool Success => Problems.Count == 0 && Drafter != null;

        /// <summary>The drafting model; null unless <see cref="Success" />.</summary>
        public ITextModel? Drafter { get; }

        /// <summary>The independent reviewer; null when none was asked for or on failure.</summary>
        public ITextModel? Reviewer { get; }

        /// <summary>Every reason the task must stop; empty on success.</summary>
        public IReadOnlyList<string> Problems { get; }
    }

    /// <summary>Chooses the drafting model and the reviewer for a task from the issue's labels. It never falls back
    /// silently: any label that cannot be honoured stops the task and says why.</summary>
    public static class TextModelRouter
    {
        /// <summary>Label prefix that names the drafting model, for example <c>Model::Gemini</c>.</summary>
        public const string ModelLabelPrefix = "Model::";

        /// <summary>Label prefix that names the independent reviewer, for example <c>Review::Mistral</c>.</summary>
        public const string ReviewLabelPrefix = "Review::";

        /// <summary>Routes a task of kind <paramref name="taskKind" />. Reads only labels, names and configuration;
        /// no model is called.</summary>
        public static TextModelRoutingResult Route(string taskKind, IEnumerable<string> labels, TextModelRegistry registry, TextModelRoutingConfig config)
        {
            if (labels == null)
            {
                throw new ArgumentNullException(nameof(labels));
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var problems = new List<string>();
            var kind = TextTaskKinds.Normalize(taskKind);
            if (kind == null)
            {
                problems.Add($"Unknown task kind \"{taskKind}\" (known: {string.Join(", ", TextTaskKinds.All)}).");
                return new TextModelRoutingResult(null, null, problems);
            }

            var modelNames = NamesWithPrefix(labels, ModelLabelPrefix, problems);
            var reviewNames = NamesWithPrefix(labels, ReviewLabelPrefix, problems);
            var fallback = config.DefaultFor(kind);

            string? drafterName = null;
            if (modelNames.Count > 1)
            {
                problems.Add($"More than one {ModelLabelPrefix} label ({string.Join(", ", modelNames)}); keep exactly one.");
            }
            else if (modelNames.Count == 1)
            {
                drafterName = modelNames[0];
            }
            else if (fallback != null)
            {
                drafterName = fallback.Model;
            }
            else
            {
                problems.Add($"No {ModelLabelPrefix}<name> label and no default model for \"{kind}\".");
            }

            string? reviewerName = null;
            var reviewerIsDefault = false;
            if (reviewNames.Count > 1)
            {
                problems.Add($"More than one {ReviewLabelPrefix} label ({string.Join(", ", reviewNames)}); keep at most one.");
            }
            else if (reviewNames.Count == 1)
            {
                reviewerName = reviewNames[0];
            }
            else if (fallback != null)
            {
                reviewerName = fallback.Reviewer;
                reviewerIsDefault = reviewerName != null;
            }

            var drafter = drafterName == null ? null : Resolve(drafterName, "drafting model", kind, registry, config, problems);
            var reviewer = reviewerName == null ? null : Resolve(reviewerName, "reviewer", kind, registry, config, problems);
            if (drafterName != null && reviewerName != null &&
                string.Equals(drafterName, reviewerName, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"\"{drafterName}\" is both drafting model and reviewer; an independent review needs a different model." +
                    (reviewerIsDefault ? $" The reviewer comes from the default for \"{kind}\"; name another one with a {ReviewLabelPrefix}<name> label." : ""));
            }

            return problems.Count == 0
                ? new TextModelRoutingResult(drafter, reviewer, problems)
                : new TextModelRoutingResult(null, null, problems);
        }

        private static List<string> NamesWithPrefix(IEnumerable<string> labels, string prefix, List<string> problems)
        {
            var names = new List<string>();
            foreach (var label in labels)
            {
                if (label == null || !label.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = label.TrimStart().Substring(prefix.Length).Trim();
                if (name.Length == 0)
                {
                    problems.Add($"The label \"{label.Trim()}\" has no model name after the prefix.");
                }
                else if (!names.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        private static ITextModel? Resolve(string name, string role, string kind, TextModelRegistry registry, TextModelRoutingConfig config, List<string> problems)
        {
            var model = registry.Find(name);
            if (model == null)
            {
                var known = registry.Models.Count == 0 ? "none configured" : string.Join(", ", registry.Models.Select(m => m.Name));
                problems.Add($"The {role} \"{name}\" is not a configured model (configured: {known}).");
                return null;
            }

            if (!model.IsAvailable)
            {
                problems.Add($"The {role} \"{model.Name}\" has no API key configured.");
                return null;
            }

            if (!config.IsEmpty && !config.Lists(model.Name))
            {
                problems.Add($"The {role} \"{model.Name}\" is not listed in the routing configuration.");
                return null;
            }

            if (!config.Allows(model.Name, kind))
            {
                problems.Add($"The {role} \"{model.Name}\" is not allowed to serve \"{kind}\" in the routing configuration.");
                return null;
            }

            return model;
        }
    }
}
