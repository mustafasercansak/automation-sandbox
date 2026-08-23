using System;
using System.Collections.Generic;
using System.Linq;
using UiModel;

namespace IntentAutomation
{
    public sealed class IntentDesktopLocatorRepositoryRecorder
    {
        private readonly IntentDesktopLocatorRecordingOptions _options;

        public IntentDesktopLocatorRepositoryRecorder(IntentDesktopLocatorRecordingOptions? options = null)
        {
            _options = options ?? new IntentDesktopLocatorRecordingOptions();
            if (_options.MinimumScore < 0.0 || _options.MinimumScore > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MinimumScore must be between 0.0 and 1.0.");
            }
        }

        public IReadOnlyList<IntentDesktopLocatorRecordingResult> Record(
            IntentDesktopExplorationResult explorationResult,
            LocatorRepository repository)
        {
            if (explorationResult == null)
            {
                throw new ArgumentNullException(nameof(explorationResult));
            }

            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            return explorationResult.StepResults
                .OrderBy(stepResult => stepResult.Step.Order)
                .Select(stepResult => RecordStep(stepResult, repository))
                .ToList();
        }

        private IntentDesktopLocatorRecordingResult RecordStep(
            IntentDesktopStepExplorationResult stepResult,
            LocatorRepository repository)
        {
            var step = stepResult.Step;
            var candidate = stepResult.Candidates
                .OrderByDescending(item => item.Score)
                .FirstOrDefault();
            var locatorKey = IntentLocatorKeySynthesizer.Synthesize(step, candidate);

            var result = new IntentDesktopLocatorRecordingResult
            {
                Step = step,
                LocatorKey = locatorKey,
                Candidate = candidate,
            };

            if (step.ActionType == IntentActionType.Navigate || step.ActionType == IntentActionType.Unknown)
            {
                result.Diagnostic = "Step does not require a locator repository record.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(locatorKey))
            {
                result.Diagnostic = "Step has no LocatorKey.";
                return result;
            }

            if (stepResult.RequiresReview && !_options.RecordReviewCandidates)
            {
                result.Diagnostic = "Step requires review; candidate was not recorded.";
                return result;
            }

            if (candidate == null)
            {
                result.Diagnostic = "Step has no element candidate.";
                return result;
            }

            if (candidate.Score < _options.MinimumScore)
            {
                result.Diagnostic = $"Best candidate score {candidate.Score:F2} is below recording threshold {_options.MinimumScore:F2}.";
                return result;
            }

            var snapshot = UiElementSnapshot.Capture(candidate.Element);
            snapshot.TestIntent = step.TestIntent;
            result.Record = repository.Upsert(
                locatorKey,
                snapshot,
                applicationName: _options.ApplicationName,
                platform: _options.Platform);
            result.Recorded = true;
            result.Diagnostic = "Recorded best usable desktop candidate.";
            return result;
        }
    }
}
