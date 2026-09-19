using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutomationSandbox.IntentAutomation;
using AutomationSandbox.IntentExecution;
using AutomationSandbox.PlaywrightLiveExploration;

namespace ScenarioRunner
{
    // Real headless Chromium against local file:// fixtures, same convention as
    // PlaywrightWebSessionTests - the executor is only worth trusting if it actually drives a
    // real page. Each test supplies its own fixed IntentScenario via a fake IIntentPlanner
    // rather than exercising DeterministicIntentPlanner's NLP-ish rules - planning already has
    // its own test coverage elsewhere; these tests are about execution correctness.
    public class IntentWebExecutorTests
    {
        [Fact]
        public async Task RunAsync_ExecutesNavigateFillClickAssert_EndToEnd()
        {
            var htmlPath = WriteTempHtml("""
                <input data-testid="username-input" placeholder="Username" />
                <button data-testid="login-button">Log in</button>
                <div data-testid="welcome-message"></div>
                <script>
                    document.querySelector('[data-testid=login-button]').addEventListener('click', () => {
                        const name = document.querySelector('[data-testid=username-input]').value;
                        document.querySelector('[data-testid=welcome-message]').textContent = 'Welcome, ' + name + '!';
                    });
                </script>
                """);
            try
            {
                var url = new Uri(htmlPath).AbsoluteUri;
                var scenario = new IntentScenario
                {
                    Name = "Login flow",
                    Goal = "Log in and see the welcome message",
                    TargetUrl = url,
                    Steps = new List<IntentStep>
                    {
                        new() { Order = 1, ActionType = IntentActionType.Navigate, Value = url },
                        new() { Order = 2, ActionType = IntentActionType.Fill, TargetDescription = "username input", Value = "Ada" },
                        new() { Order = 3, ActionType = IntentActionType.Click, TargetDescription = "login button" },
                        new()
                        {
                            Order = 4, ActionType = IntentActionType.Assert, TargetDescription = "welcome message",
                            AssertionKind = AssertionKind.TextEquals, ExpectedValue = "Welcome, Ada!",
                        },
                    },
                };

                var executor = new IntentWebExecutor(new FixedScenarioPlanner(scenario));
                await using var session = await PlaywrightWebSession.StartAsync();

                var result = await executor.RunAsync(new IntentPlanningRequest { Goal = scenario.Goal, TargetUrl = url }, session);

                Assert.True(result.Success, DiagnosticsOf(result));
                Assert.Equal(4, result.StepResults.Count);
                Assert.All(result.StepResults, r => Assert.True(r.Success, r.Diagnostic));
            }
            finally
            {
                DeleteIfExists(htmlPath);
            }
        }

        [Fact]
        public async Task RunAsync_ExercisesTheRemainingActionTypes_EndToEnd()
        {
            var htmlPath = WriteTempHtml("""
                <select data-testid="country-select">
                    <option value="tr">Turkey</option>
                    <option value="us">USA</option>
                </select>
                <input type="checkbox" data-testid="agree-checkbox" />
                <div data-testid="hover-target">idle</div>
                <input type="file" data-testid="resume-input" />
                <input data-testid="key-input" />
                <div data-testid="key-result"></div>
                <div data-testid="ready-panel">ready</div>
                <script>
                    document.querySelector('[data-testid=hover-target]').addEventListener('mouseenter', () => {
                        document.querySelector('[data-testid=hover-target]').textContent = 'hovered';
                    });
                    document.querySelector('[data-testid=key-input]').addEventListener('keydown', (e) => {
                        if (e.key === 'Enter') {
                            document.querySelector('[data-testid=key-result]').textContent = 'enter-pressed';
                        }
                    });
                </script>
                """);
            var uploadPath = Path.Combine(Path.GetTempPath(), "IntentWebExecutorTests_resume_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(uploadPath, "resume contents");
            try
            {
                var url = new Uri(htmlPath).AbsoluteUri;
                var scenario = new IntentScenario
                {
                    Steps = new List<IntentStep>
                    {
                        new() { Order = 1, ActionType = IntentActionType.Navigate, Value = url },
                        new() { Order = 2, ActionType = IntentActionType.Select, TargetDescription = "country select", Value = "us" },
                        new() { Order = 3, ActionType = IntentActionType.Check, TargetDescription = "agree checkbox" },
                        new() { Order = 4, ActionType = IntentActionType.Uncheck, TargetDescription = "agree checkbox" },
                        new() { Order = 5, ActionType = IntentActionType.Hover, TargetDescription = "hover target" },
                        new() { Order = 6, ActionType = IntentActionType.UploadFile, TargetDescription = "resume input", Value = uploadPath },
                        new() { Order = 7, ActionType = IntentActionType.PressKey, TargetDescription = "key input", Value = "Enter" },
                        // Wait can only confirm/wait on a target the match step can already see - it cannot
                        // discover a target that starts display:none and appears later, since
                        // IntentExplorationBridge excludes hidden elements from matching before Wait's own
                        // WaitForVisibleAsync ever runs (see IntentWebExecutor's Wait case comment). This
                        // panel is visible from the start, so the wait resolves immediately.
                        new() { Order = 8, ActionType = IntentActionType.Wait, TargetDescription = "ready panel", Value = "5" },
                        new()
                        {
                            Order = 9, ActionType = IntentActionType.Assert, TargetDescription = "ready panel",
                            AssertionKind = AssertionKind.Visible,
                        },
                    },
                };

                var executor = new IntentWebExecutor(new FixedScenarioPlanner(scenario));
                await using var session = await PlaywrightWebSession.StartAsync();

                var result = await executor.RunAsync(new IntentPlanningRequest { Goal = "Exercise the rest of the vocabulary", TargetUrl = url }, session);

                Assert.True(result.Success, DiagnosticsOf(result));
                Assert.Equal(9, result.StepResults.Count);
                Assert.Equal("hovered", await session.GetTextAsync("[data-testid='hover-target']"));
                Assert.Equal("enter-pressed", await session.GetTextAsync("[data-testid='key-result']"));
                Assert.False(await session.IsCheckedAsync("[data-testid='agree-checkbox']"));
                Assert.EndsWith(Path.GetFileName(uploadPath), await session.GetValueAsync("[data-testid='resume-input']"), StringComparison.Ordinal);
            }
            finally
            {
                DeleteIfExists(htmlPath);
                DeleteIfExists(uploadPath);
            }
        }

        [Fact]
        public async Task RunAsync_AbortsOnTheFirstFailedStep_AndDoesNotAttemptLaterSteps()
        {
            var htmlPath = WriteTempHtml("<button data-testid=\"real-button\">Real</button>");
            try
            {
                var url = new Uri(htmlPath).AbsoluteUri;
                var scenario = new IntentScenario
                {
                    Steps = new List<IntentStep>
                    {
                        new() { Order = 1, ActionType = IntentActionType.Navigate, Value = url },
                        // Fill needs an <input>/<textarea> - the page has none, so this can never
                        // match any candidate, regardless of TargetDescription wording.
                        new() { Order = 2, ActionType = IntentActionType.Fill, TargetDescription = "a field that does not exist anywhere on this page", Value = "x" },
                        new() { Order = 3, ActionType = IntentActionType.Click, TargetDescription = "real button" },
                    },
                };

                var executor = new IntentWebExecutor(new FixedScenarioPlanner(scenario));
                await using var session = await PlaywrightWebSession.StartAsync();

                var result = await executor.RunAsync(new IntentPlanningRequest { Goal = "g", TargetUrl = url }, session);

                Assert.False(result.Success);
                Assert.Equal(2, result.StepResults.Count);
                Assert.True(result.StepResults[0].Success);
                Assert.False(result.StepResults[1].Success);
            }
            finally
            {
                DeleteIfExists(htmlPath);
            }
        }

        [Fact]
        public async Task RunAsync_NotVisibleAssertion_FailsWithAnExplicitDiagnostic()
        {
            var htmlPath = WriteTempHtml("<div data-testid=\"hidden-thing\" style=\"display:none\">gone</div>");
            try
            {
                var url = new Uri(htmlPath).AbsoluteUri;
                var scenario = new IntentScenario
                {
                    Steps = new List<IntentStep>
                    {
                        new() { Order = 1, ActionType = IntentActionType.Navigate, Value = url },
                        new()
                        {
                            Order = 2, ActionType = IntentActionType.Assert, TargetDescription = "hidden thing",
                            AssertionKind = AssertionKind.NotVisible,
                        },
                    },
                };

                var executor = new IntentWebExecutor(new FixedScenarioPlanner(scenario));
                await using var session = await PlaywrightWebSession.StartAsync();

                var result = await executor.RunAsync(new IntentPlanningRequest { Goal = "g", TargetUrl = url }, session);

                Assert.False(result.Success);
                Assert.Contains("NotVisible", result.StepResults[1].Diagnostic, StringComparison.Ordinal);
            }
            finally
            {
                DeleteIfExists(htmlPath);
            }
        }

        [Fact]
        public async Task RunAsync_UrlAssertion_EvaluatesAgainstTheSessionsCurrentUrl_WithoutMatchingAnElement()
        {
            var htmlPath = WriteTempHtml("<p>hi</p>");
            try
            {
                var url = new Uri(htmlPath).AbsoluteUri;
                var scenario = new IntentScenario
                {
                    Steps = new List<IntentStep>
                    {
                        new() { Order = 1, ActionType = IntentActionType.Navigate, Value = url },
                        new()
                        {
                            Order = 2, ActionType = IntentActionType.Assert,
                            AssertionKind = AssertionKind.UrlEquals, ExpectedValue = url,
                        },
                    },
                };

                var executor = new IntentWebExecutor(new FixedScenarioPlanner(scenario));
                await using var session = await PlaywrightWebSession.StartAsync();

                var result = await executor.RunAsync(new IntentPlanningRequest { Goal = "g", TargetUrl = url }, session);

                Assert.True(result.Success, DiagnosticsOf(result));
                Assert.Null(result.StepResults[1].CssSelector);
            }
            finally
            {
                DeleteIfExists(htmlPath);
            }
        }

        private static string DiagnosticsOf(IntentWebExecutionResult result)
        {
            return string.Join(" | ", result.StepResults.ConvertAll(r => $"{r.Step.ActionType}: {r.Diagnostic}"));
        }

        private static string WriteTempHtml(string bodyHtml)
        {
            var path = Path.Combine(Path.GetTempPath(), "IntentWebExecutorTests_" + Guid.NewGuid().ToString("N") + ".html");
            File.WriteAllText(path, $"<!doctype html><html><body>{bodyHtml}</body></html>");
            return path;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private sealed class FixedScenarioPlanner : IIntentPlanner
        {
            private readonly IntentScenario _scenario;

            public FixedScenarioPlanner(IntentScenario scenario)
            {
                _scenario = scenario;
            }

            public IntentPlanningResult Plan(IntentPlanningRequest request) => new() { Scenario = _scenario };
        }
    }
}
