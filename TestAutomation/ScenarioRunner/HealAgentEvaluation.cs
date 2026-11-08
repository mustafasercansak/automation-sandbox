using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;
using AutomationSandbox.UiModel;

namespace ScenarioRunner
{
    /// <summary>The cases of the evaluation (#676): every stored locator of the organic drift dataset, taken exactly as the dataset's own
    /// tests take them. The set is frozen: nothing is added, dropped or reordered after a result has been seen.</summary>
    internal static class HealAgentCases
    {
        public static List<HealAgentCase> OrganicDrift()
        {
            var cases = new List<HealAgentCase>();
            using (var truth = OrganicDriftData.LoadGroundTruth())
            {
                var types = OrganicDriftData.InteractiveTypes(truth);
                var pair = truth.RootElement.GetProperty("pair");
                var oldVersion = pair.GetProperty("old").GetString()!;
                var newVersion = pair.GetProperty("new").GetString()!;
                foreach (var state in truth.RootElement.GetProperty("states").EnumerateArray())
                {
                    var stateName = state.GetProperty("name").GetString()!;
                    var oldNodes = OrganicDriftData.Interactive(OrganicDriftData.LoadTree(oldVersion, stateName), types);
                    var newRoot = OrganicDriftData.LoadTree(newVersion, stateName);
                    var newNodes = OrganicDriftData.Interactive(newRoot, types);
                    foreach (var entry in state.GetProperty("entries").EnumerateArray())
                    {
                        var oldIndex = entry.GetProperty("old").GetInt32();
                        var target = entry.GetProperty("new");
                        var label = stateName + "#" + oldIndex.ToString(CultureInfo.InvariantCulture) + " '" + HealAgentTools.Clean(oldNodes[oldIndex].Name, 50) + "'";
                        cases.Add(new HealAgentCase(label, OrganicDriftData.StoredLocator(oldNodes[oldIndex]), newRoot, target.ValueKind == JsonValueKind.Null ? null : newNodes[target.GetInt32()]));
                    }
                }
            }

            return cases;
        }
    }

    /// <summary>All runs of one model over the cases, and the counts the article may quote with their sample sizes.</summary>
    internal sealed class HealAgentReport
    {
        public HealAgentReport(string modelName, bool live, int runsPerCase, int cases, IReadOnlyList<HealAgentRun> runs)
        {
            ModelName = modelName;
            Live = live;
            RunsPerCase = runsPerCase;
            Cases = cases;
            Runs = runs;
        }

        public string ModelName { get; }

        public bool Live { get; }

        public int RunsPerCase { get; }

        public int Cases { get; }

        public IReadOnlyList<HealAgentRun> Runs { get; }

        public int Count(bool engineConfident, HealAgentOutcome outcome) => Runs.Count(r => r.EngineConfident == engineConfident && r.Outcome == outcome);

        public int Total(bool engineConfident) => Runs.Count(r => r.EngineConfident == engineConfident);

        /// <summary>Runs where the engine had accepted the right element and the agent named a different one.</summary>
        public int ChangedACorrectEngineHeal => Runs.Count(r => r.EngineConfident && r.Outcome == HealAgentOutcome.OverrodeWrong);

        public string ToMarkdown()
        {
            var b = new StringBuilder();
            b.AppendLine("# Heal-review agent: " + ModelName + (Live ? "" : " (OFFLINE, scripted answers)"));
            b.AppendLine();
            b.AppendLine("Cases: " + Cases + " stored locators of the organic drift dataset (Swagger UI 4.19.1 to 5.17.14), each run " + RunsPerCase + " time(s), " + Runs.Count + " run(s) in all. Outcomes are counted per run, split by what the engine (Balanced) did on the same locator.");
            b.AppendLine();
            b.AppendLine("| Engine (Balanced) | Runs | Agreed | Overrode, correct | Overrode, wrong | Escalated | Failed |");
            b.AppendLine("| :--- | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (var confident in new[] { true, false })
            {
                b.AppendLine("| " + (confident ? "accepted a heal" : "declined") + " | " + Total(confident) + " | " + Count(confident, HealAgentOutcome.Agreed) + " | " + Count(confident, HealAgentOutcome.OverrodeCorrect) + " | " + Count(confident, HealAgentOutcome.OverrodeWrong) + " | " + Count(confident, HealAgentOutcome.Escalated) + " | " + Count(confident, HealAgentOutcome.Failed) + " |");
            }

            b.AppendLine();
            b.AppendLine("Runs that changed a heal the engine had accepted into a wrong one: " + ChangedACorrectEngineHeal + " of " + Total(true) + ".");
            b.AppendLine("A failed run (a model error, an invalid answer, an element no tool had shown, the turn limit) is never counted as an escalation.");
            b.AppendLine();
            b.AppendLine("What this is not: one application pair with mild drift and **no removed element**, so the dangerous case (a deleted element healing onto a neighbour, where every unanimous model verdict was a false heal in earlier measurements) is not tested here. The reviewed answers are the maintainer's judgement, not independent confirmation. Run-to-run variation is real: read a difference of a few runs as noise. This is an observation, not a ranking of models and not external validation.");
            b.AppendLine();
            b.AppendLine("## Runs");
            b.AppendLine();
            foreach (var run in Runs.Where(r => r.Outcome != HealAgentOutcome.Agreed))
            {
                b.AppendLine("- `" + run.Label.Replace("`", "'") + "` engine " + (run.EngineConfident ? "accepted" : "declined") + ": **" + run.Outcome + "**" + (run.PickedId.Length > 0 ? ", picked " + run.PickedId + " '" + run.PickedName.Replace("`", "'") + "'" : "") + (run.FailureReason != null ? " (" + run.FailureReason.Replace("`", "'") + ")" : "") + ", " + run.Turns + " turn(s)" + (run.UsedHealTool ? ", used the engine tool" : ""));
            }

            return b.ToString();
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(new
            {
                model = ModelName,
                live = Live,
                runsPerCase = RunsPerCase,
                cases = Cases,
                runs = Runs.Select(r => new
                {
                    label = r.Label,
                    engineConfident = r.EngineConfident,
                    outcome = r.Outcome.ToString(),
                    pickedId = r.PickedId,
                    pickedName = r.PickedName,
                    pickCorrect = r.PickCorrect,
                    turns = r.Turns,
                    usedHealTool = r.UsedHealTool,
                    reason = r.Reason,
                    failure = r.FailureReason,
                    steps = r.Steps,
                    elapsedMs = (long)r.Elapsed.TotalMilliseconds,
                }).ToList(),
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>Picks which configured models a live run uses (#676): all of them, or only the named ones, so models can be run one at a
    /// time. A name that matches no configured model is an error and is never skipped quietly: a typo must not look like a run.</summary>
    internal static class HealAgentModelFilter
    {
        public static List<ITextModel> Select(IEnumerable<ITextModel> models, string? only)
        {
            var available = models.Where(m => m.IsAvailable).ToList();
            if (string.IsNullOrWhiteSpace(only))
            {
                return available;
            }

            var picked = new List<ITextModel>();
            foreach (var name in only!.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
            {
                var model = available.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
                if (model == null)
                {
                    throw new InvalidOperationException("No configured model is named '" + name + "'. Configured: " + string.Join(", ", available.Select(m => m.Name)) + ".");
                }

                if (!picked.Contains(model))
                {
                    picked.Add(model);
                }
            }

            return picked;
        }
    }

    /// <summary>Spaces the calls to a model at least <c>minInterval</c> apart (measured from one call's start to the next one's), so a live
    /// run does not hit a provider's requests-per-minute limit (#676: the first live run, Gemini, got HTTP 429 on 42 of 50 runs after about
    /// two minutes). It changes nothing the agent sees, only when a call is made; a failed call passes through untouched.</summary>
    internal sealed class PacedTextModel : ITextModel
    {
        private readonly ITextModel _inner;
        private readonly TimeSpan _minInterval;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private readonly Func<TimeSpan> _clock;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private TimeSpan? _lastStart;

        public PacedTextModel(ITextModel inner, TimeSpan minInterval, Func<TimeSpan, CancellationToken, Task>? delayAsync = null, Func<TimeSpan>? clock = null)
        {
            if (minInterval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(minInterval), "minInterval must not be negative.");
            }

            _inner = inner;
            _minInterval = minInterval;
            _delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _clock = clock ?? (() => stopwatch.Elapsed);
        }

        public string Name => _inner.Name;

        public bool IsAvailable => _inner.IsAvailable;

        public async Task<TextCompletionResult> CompleteAsync(TextCompletionRequest request, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_lastStart.HasValue)
                {
                    var wait = _lastStart.Value + _minInterval - _clock();
                    if (wait > TimeSpan.Zero)
                    {
                        await _delayAsync(wait, cancellationToken).ConfigureAwait(false);
                    }
                }

                _lastStart = _clock();
                return await _inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    internal static class HealAgentEvaluation
    {
        public const int MaxRunsPerCase = 5;

        public static async Task<HealAgentReport> RunAsync(ITextModel model, IReadOnlyList<HealAgentCase> cases, bool live, int runsPerCase, int maxTurns = HealReviewAgent.DefaultMaxTurns, CancellationToken cancellationToken = default)
        {
            if (runsPerCase < 1 || runsPerCase > MaxRunsPerCase)
            {
                throw new ArgumentOutOfRangeException(nameof(runsPerCase), "runsPerCase must be from 1 to " + MaxRunsPerCase + ".");
            }

            var runs = new List<HealAgentRun>();
            foreach (var item in cases)
            {
                for (var i = 0; i < runsPerCase; i++)
                {
                    runs.Add(await HealReviewAgent.RunAsync(model, item, maxTurns, cancellationToken).ConfigureAwait(false));
                }
            }

            return new HealAgentReport(model.Name, live, runsPerCase, cases.Count, runs);
        }
    }
}
