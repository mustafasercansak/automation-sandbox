using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>One past issue or pull request in a <see cref="LessonIndex" />: the text and file paths a retrieval can match on.
    /// Everything in it is untrusted text written by other people (and credential shapes are masked when the index is built).</summary>
    public sealed class LessonRecord
    {
        /// <summary>The kind value for an issue.</summary>
        public const string IssueKind = "issue";

        /// <summary>The kind value for a pull request.</summary>
        public const string PullRequestKind = "pull-request";

        /// <summary>Creates a record. Lists may be empty; they are copied.</summary>
        public LessonRecord(string kind, int number, string title, string body, IEnumerable<string> labels, IEnumerable<string> files, string reviewComments, DateTimeOffset? closedAt = null)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Number = number;
            Title = title ?? "";
            Body = body ?? "";
            Labels = (labels ?? Enumerable.Empty<string>()).ToArray();
            Files = (files ?? Enumerable.Empty<string>()).ToArray();
            ReviewComments = reviewComments ?? "";
            ClosedAt = closedAt;
        }

        /// <summary><see cref="IssueKind" /> or <see cref="PullRequestKind" />.</summary>
        public string Kind { get; }

        /// <summary>The issue or pull request number, so a reader can open the source.</summary>
        public int Number { get; }

        /// <summary>The title.</summary>
        public string Title { get; }

        /// <summary>An excerpt of the body.</summary>
        public string Body { get; }

        /// <summary>The label names.</summary>
        public IReadOnlyList<string> Labels { get; }

        /// <summary>The paths a pull request changed; empty for an issue.</summary>
        public IReadOnlyList<string> Files { get; }

        /// <summary>An excerpt of the review comments, joined; empty when there are none.</summary>
        public string ReviewComments { get; }

        /// <summary>When it was closed, or null when the index did not record it.</summary>
        public DateTimeOffset? ClosedAt { get; }
    }

    /// <summary>A plain-file index of past issues and pull requests, read back with <see cref="Parse(string)" />. It is built from the GitHub API by
    /// <see cref="LessonIndexBuilder" /> and stored as one JSON file: no embedding service, no database. Parsing is strict and
    /// bounded, because the file is read by a tool that sends part of it to a model.</summary>
    public sealed class LessonIndex
    {
        /// <summary>The only file version this build reads and writes.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Most records an index may hold.</summary>
        public const int MaxRecords = 5000;

        /// <summary>Largest index file <see cref="Parse" /> accepts, in characters.</summary>
        public const int MaxFileLength = 20000000;

        private const int MaxText = 20000;

        /// <summary>Creates an index over <paramref name="records" />.</summary>
        public LessonIndex(string repository, IEnumerable<LessonRecord> records)
        {
            Repository = repository ?? "";
            Records = (records ?? Enumerable.Empty<LessonRecord>()).ToArray();
        }

        /// <summary>The repository the records came from (<c>owner/name</c>), or empty.</summary>
        public string Repository { get; }

        /// <summary>The records.</summary>
        public IReadOnlyList<LessonRecord> Records { get; }

        /// <summary>Writes the index as indented JSON.</summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(new
            {
                version = CurrentVersion,
                repository = Repository,
                records = Records.Select(r => new
                {
                    kind = r.Kind,
                    number = r.Number,
                    title = r.Title,
                    body = r.Body,
                    labels = r.Labels,
                    files = r.Files,
                    reviewComments = r.ReviewComments,
                    closedAt = r.ClosedAt,
                }).ToList(),
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>Reads an index. Anything unexpected (an unknown version, a record over the limits, a wrong type) rejects the whole
        /// file as a <see cref="FormatException" />; nothing is repaired.</summary>
        public static LessonIndex Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            if (json.Length > MaxFileLength)
            {
                throw new FormatException("The lesson index file is too large.");
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != CurrentVersion)
                {
                    throw new FormatException("The lesson index has an unknown version.");
                }

                if (!root.TryGetProperty("records", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > MaxRecords)
                {
                    throw new FormatException("The lesson index has no records array, or too many records.");
                }

                var repository = root.TryGetProperty("repository", out var repo) && repo.ValueKind == JsonValueKind.String ? repo.GetString()! : "";
                var records = new List<LessonRecord>();
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        throw new FormatException("A lesson record is not an object.");
                    }

                    var kind = Text(item, "kind", 20);
                    if (kind != LessonRecord.IssueKind && kind != LessonRecord.PullRequestKind)
                    {
                        throw new FormatException("A lesson record has an unknown kind.");
                    }

                    if (!item.TryGetProperty("number", out var number) || number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var n) || n < 1)
                    {
                        throw new FormatException("A lesson record has no valid number.");
                    }

                    DateTimeOffset? closed = null;
                    if (item.TryGetProperty("closedAt", out var c) && c.ValueKind == JsonValueKind.String)
                    {
                        if (!DateTimeOffset.TryParse(c.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
                        {
                            throw new FormatException("A lesson record has an invalid closedAt.");
                        }

                        closed = parsed;
                    }

                    records.Add(new LessonRecord(kind, n, Text(item, "title", 500), Text(item, "body", MaxText), List(item, "labels", 30, 100), List(item, "files", 200, 300), Text(item, "reviewComments", MaxText), closed));
                }

                return new LessonIndex(repository, records);
            }
            catch (JsonException)
            {
                throw new FormatException("The lesson index is not valid JSON.");
            }
        }

        private static string Text(JsonElement item, string name, int max)
        {
            if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                return "";
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("A lesson record field \"" + name + "\" is not text.");
            }

            var text = value.GetString()!;
            if (text.Length > max)
            {
                throw new FormatException("A lesson record field \"" + name + "\" is too long.");
            }

            return text;
        }

        private static List<string> List(JsonElement item, string name, int maxItems, int maxLength)
        {
            var result = new List<string>();
            if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                return result;
            }

            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maxItems)
            {
                throw new FormatException("A lesson record field \"" + name + "\" is not a short list.");
            }

            foreach (var entry in value.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String || entry.GetString()!.Length > maxLength)
                {
                    throw new FormatException("A lesson record field \"" + name + "\" holds an invalid entry.");
                }

                result.Add(entry.GetString()!);
            }

            return result;
        }
    }
}
