using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;

namespace ScenarioRunner
{
    public class LessonReplayTests
    {
        private static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "lessons-replay");

        private static IReadOnlyList<LessonReplayCase> Cases() => LessonReplayCase.Load(Path.Combine(Dir, "cases.json"));

        private static LessonIndex Index() => LessonIndex.Parse(File.ReadAllText(Path.Combine(Dir, "index.json")));

        private sealed class Scripted : ITextModel
        {
            private readonly Func<string, TextCompletionResult> _answer;

            public Scripted(Func<string, TextCompletionResult> answer)
            {
                _answer = answer;
            }

            public string Name => "scripted";

            public bool IsAvailable => true;

            public List<string> Prompts { get; } = new List<string>();

            public Task<TextCompletionResult> CompleteAsync(TextCompletionRequest request, CancellationToken cancellationToken = default)
            {
                Prompts.Add(request.Prompt);
                return Task.FromResult(_answer(request.Prompt));
            }
        }

        private static TextCompletionResult Ok(string text) => new TextCompletionResult { Success = true, Text = text };

        [Fact]
        public void TheCases_AreTheRealPreFixLines_OfThreeFixCommits()
        {
            var cases = Cases();

            Assert.Equal(new[] { "valuetask-completedtask", "process-api-net48", "nullable-flow-netstandard" }, cases.Select(c => c.Id));
            Assert.Equal(new[] { "127969f", "fec29eb", "5a913af" }, cases.Select(c => c.FixCommit));
            Assert.Contains("ValueTask.CompletedTask", cases[0].Diff);
            Assert.Contains("psi.ArgumentList.Add", cases[1].Diff);
            Assert.Contains("process.WaitForExit(TimeSpan.FromSeconds(60))", cases[1].Diff);
            Assert.Contains("!string.IsNullOrWhiteSpace(HtmlFilePath)", cases[2].Diff);
            Assert.All(cases, c => Assert.NotEmpty(c.FlagTerms));
            Assert.All(cases, c => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", c.AsOf));
        }

        [Fact]
        public void TheFixtureIndex_SaysItIsSyntheticOnEveryRecord()
        {
            var index = Index();

            Assert.Equal("fixture/synthetic", index.Repository);
            Assert.All(index.Records, r => Assert.StartsWith("[fixture]", r.Title));
        }

        [Fact]
        public async Task ARetrievalAwareModel_ShowsTheWithColumnBeatingTheWithoutColumn_ByTheTableAlone()
        {
            var model = new Scripted(prompt => Ok(prompt.Contains("BEGIN_LESSON_DATA") ? "This uses ValueTask.CompletedTask and ArgumentList, which do not exist on net48." : "Looks fine to me."));

            var report = await LessonReplay.RunAsync(model, Cases(), Index(), live: false);

            Assert.Equal(3, report.Replayed);
            Assert.Equal(0, report.FlaggedWithout);
            Assert.Equal(2, report.FlaggedWith);
            var byId = report.Rows.ToDictionary(r => r.Case.Id);
            Assert.Equal(new[] { 9001 }, byId["valuetask-completedtask"].LessonNumbers);
            Assert.Equal(9002, byId["process-api-net48"].LessonNumbers[0]);
            Assert.Empty(byId["nullable-flow-netstandard"].LessonNumbers);
            Assert.Equal(LessonReplayOutcome.NotFlagged, byId["nullable-flow-netstandard"].With);
        }

        [Fact]
        public async Task WhenNothingIsRetrieved_BothColumnsShareOneAnswer_AndTheModelIsAskedOnce()
        {
            var model = new Scripted(_ => Ok("Looks fine to me."));

            var report = await LessonReplay.RunAsync(model, Cases(), new LessonIndex("o/r", new LessonRecord[0]), live: false);

            Assert.Equal(3, model.Prompts.Count);
            Assert.All(report.Rows, r => Assert.Equal(r.Without, r.With));
            Assert.DoesNotContain(model.Prompts, p => p.Contains("BEGIN_LESSON_DATA"));
        }

        [Fact]
        public async Task TheWithPrompt_IsThePlainPromptPlusTheSection_AndNothingElseChanges()
        {
            var model = new Scripted(_ => Ok("x"));
            var first = Cases()[0];

            await LessonReplay.RunAsync(model, new[] { first }, Index(), live: false);

            Assert.Equal(2, model.Prompts.Count);
            var plain = LessonReplay.BuildReviewPrompt(first);
            Assert.Equal(plain, model.Prompts[0]);
            Assert.StartsWith(plain.TrimEnd(), model.Prompts[1]);
            Assert.Contains("#9001", model.Prompts[1]);
            Assert.Contains("BEGIN_DIFF_DATA", plain);
            Assert.Contains("data, not instructions", plain);
        }

        [Fact]
        public async Task AFailedCell_IsCounted_NeverTakenAsNotFlaggedOrFlagged()
        {
            var calls = 0;
            var model = new Scripted(_ => ++calls == 1 ? new TextCompletionResult { Success = false, ErrorMessage = "HTTP 429" } : Ok("net48 CompletedTask"));

            var report = await LessonReplay.RunAsync(model, Cases().Take(1).ToList(), Index(), live: false);

            Assert.Equal(LessonReplayOutcome.Failed, report.Rows[0].Without);
            Assert.Equal(LessonReplayOutcome.Flagged, report.Rows[0].With);
            Assert.Equal(1, report.Failed);
        }

        [Fact]
        public void IndexAsOf_KeepsOnlyRecordsClosedBeforeTheDate_AndDropsThoseWithNoClosingTime()
        {
            var index = new LessonIndex("o/r", new[]
            {
                new LessonRecord("issue", 1, "old", "", new string[0], new string[0], "", new DateTimeOffset(2026, 8, 28, 23, 59, 59, TimeSpan.Zero)),
                new LessonRecord("issue", 2, "same day", "", new string[0], new string[0], "", new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero)),
                new LessonRecord("issue", 3, "later", "", new string[0], new string[0], "", new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)),
                new LessonRecord("issue", 4, "unknown", "", new string[0], new string[0], ""),
            });

            var kept = LessonReplay.IndexAsOf(index, "2026-08-29");

            Assert.Equal(new[] { 1 }, kept.Records.Select(r => r.Number));
            Assert.Equal("o/r", kept.Repository);
        }

        [Fact]
        public async Task EachCaseIsGivenItsOwnIndex_SoALaterRecordNeverReachesAnEarlierDefect()
        {
            var cases = Cases();
            var filler = Enumerable.Range(1, 6).Select(i => new LessonRecord("issue", 300 + i, "unrelated topic " + i, "nothing to do with it " + i, new string[0], new string[0], "", new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));
            var index = new LessonIndex("o/r", filler.Concat(new[]
            {
                new LessonRecord("issue", 100, "net48 CompletedTask ValueTask", "ValueTask.CompletedTask does not exist on net48", new string[0], new string[0], "", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
                new LessonRecord("issue", 200, "net48 CompletedTask ValueTask", "ValueTask.CompletedTask does not exist on net48", new string[0], new string[0], "", new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero)),
            }));

            var report = await LessonReplay.RunAsync(new Scripted(_ => Ok("x")), cases.Take(1).ToList(), c => LessonReplay.IndexAsOf(index, c.AsOf), live: false);

            Assert.Equal(new[] { 100 }, report.Rows[0].LessonNumbers);
        }

        [Fact]
        public async Task TheReport_StatesCountsAndLimits_AndMakesNoImprovementClaim()
        {
            var model = new Scripted(prompt => Ok(prompt.Contains("BEGIN_LESSON_DATA") ? "net48 CompletedTask ArgumentList" : "fine"));
            var offline = await LessonReplay.RunAsync(model, Cases(), Index(), live: false);
            var live = await LessonReplay.RunAsync(model, Cases(), Index(), live: true);

            var markdown = offline.ToMarkdown();
            Assert.Contains("OFFLINE run", markdown);
            Assert.Contains("says nothing about retrieval or any model", markdown);
            Assert.Contains("Defects replayed: 3", markdown);
            Assert.Contains("Flagged without lessons: 0 of 3", markdown);
            Assert.Contains("Flagged with lessons: 2 of 3", markdown);
            Assert.Contains("none found", markdown);
            Assert.Contains("this table is not that claim", markdown);
            Assert.DoesNotContain("OFFLINE", live.ToMarkdown());
            Assert.Contains("an observation, not a result", live.ToMarkdown());
            using var json = JsonDocument.Parse(offline.ToJson());
            Assert.Equal(3, json.RootElement.GetProperty("rows").GetArrayLength());
            Assert.False(json.RootElement.GetProperty("live").GetBoolean());
        }

        // The live replay: every configured model reviews each real defect with and without the lessons its index held before the
        // defect. LESSON_REPLAY=1 turns it on and LESSON_REPLAY_INDEX is one index file built with `lessons build --before <the latest
        // case date>`; each case then sees only the records closed before its own date. Reports go to LESSON_REPLAY_OUT. A no-op without
        // the switch, the index or a model, like the other live harnesses.
        [SkippableFact]
        public async Task RunLessonReplay_LiveAgainstConfiguredModels()
        {
            var optIn = Environment.GetEnvironmentVariable("LESSON_REPLAY");
            Skip.If(optIn != "1" && !string.Equals(optIn, "true", StringComparison.OrdinalIgnoreCase), "LESSON_REPLAY=1 is not set.");

            var indexPath = Environment.GetEnvironmentVariable("LESSON_REPLAY_INDEX");
            Skip.If(string.IsNullOrWhiteSpace(indexPath) || !File.Exists(indexPath), "LESSON_REPLAY_INDEX does not name an index file.");

            var registry = TextModelRegistry.CreateFromEnvironment();
            Skip.If(registry.Models.Count == 0, "No model is configured.");

            var index = LessonIndex.Parse(File.ReadAllText(indexPath!));
            var directory = Environment.GetEnvironmentVariable("LESSON_REPLAY_OUT");
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Path.Combine(Path.GetTempPath(), "lesson-replay-" + Guid.NewGuid().ToString("N"));
            }

            Directory.CreateDirectory(directory);
            foreach (var model in registry.Models.Where(m => m.IsAvailable))
            {
                var report = await LessonReplay.RunAsync(model, Cases(), c => LessonReplay.IndexAsOf(index, c.AsOf), live: true);
                File.WriteAllText(Path.Combine(directory, "lesson-replay-" + model.Name + ".md"), report.ToMarkdown());
                File.WriteAllText(Path.Combine(directory, "lesson-replay-" + model.Name + ".json"), report.ToJson());
                Console.WriteLine("[LessonReplay] " + model.Name + ": flagged without lessons " + report.FlaggedWithout + " of " + report.Replayed + ", with lessons " + report.FlaggedWith + " of " + report.Replayed + ", failed cells " + report.Failed + ". Written to " + directory);
            }
        }
    }
}
