using System;
using System.Collections.Generic;
using System.Linq;
using AutomationSandbox.UiModel;
using AutomationSandbox.WebDiscovery;

namespace AutomationSandbox.IntentAutomation
{
    /// <summary>Records eligible web intent matches as reusable locator snapshots.</summary>
    public sealed class IntentLocatorRepositoryRecorder
    {
        private readonly IntentLocatorRecordingOptions _options;

        /// <summary>Configures web recording thresholds and repository metadata; omitted options use the standard recording policy.</summary>
        public IntentLocatorRepositoryRecorder(IntentLocatorRecordingOptions? options = null)
        {
            _options = options ?? new IntentLocatorRecordingOptions();
            if (_options.MinimumScore < 0.0 || _options.MinimumScore > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MinimumScore must be between 0.0 and 1.0.");
            }
        }

        /// <summary>Evaluates exploration results against recording policy and persists eligible locator snapshots.</summary>
        public IReadOnlyList<IntentLocatorRecordingResult> Record(
            IntentExplorationResult explorationResult,
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

        private IntentLocatorRecordingResult RecordStep(
            IntentStepExplorationResult stepResult,
            LocatorRepository repository)
        {
            var step = stepResult.Step;
            var candidate = stepResult.Candidates
                .OrderByDescending(item => item.Score)
                .FirstOrDefault();
            var locatorKey = IntentLocatorKeySynthesizer.Synthesize(step, candidate);

            var result = new IntentLocatorRecordingResult
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

            var snapshot = WebElementMapper.ToUiElementTree(candidate.Element);
            snapshot.TestIntent = step.TestIntent;
            result.Record = repository.Upsert(
                locatorKey,
                snapshot,
                applicationName: _options.ApplicationName,
                platform: _options.Platform);
            result.Recorded = true;
            result.Diagnostic = "Recorded best visible DOM candidate.";
            return result;
        }
    }
}
