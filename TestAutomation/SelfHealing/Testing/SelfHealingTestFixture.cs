using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LlmHealing;
using UiModel;

namespace SelfHealing.Testing
{
    /// <summary>
    /// Configuration options for configuring <see cref="SelfHealingTestFixture"/>.
    /// </summary>
    public sealed class SelfHealingTestOptions
    {
        /// <summary>
        /// Optional explicit path to the locator repository file. If null, a managed temporary file is created.
        /// </summary>
        public string? RepositoryPath { get; set; }

        /// <summary>
        /// Whether to automatically delete temporary repository files upon fixture disposal. Defaults to true.
        /// </summary>
        public bool AutoDeleteRepositoryOnDispose { get; set; } = true;

        /// <summary>
        /// The healing operating mode for the test run. Defaults to <see cref="HealingMode.AutoHeal"/> for test execution.
        /// </summary>
        public HealingMode Mode { get; set; } = HealingMode.AutoHeal;

        /// <summary>
        /// Preset threshold profile to configure for resolution scoring. Defaults to <see cref="ThresholdProfile.Balanced"/>.
        /// </summary>
        public ThresholdProfile Profile { get; set; } = ThresholdProfile.Balanced;

        /// <summary>
        /// Optional custom similarity weights overriding the profile defaults.
        /// </summary>
        public SimilarityWeights? CustomWeights { get; set; }

        /// <summary>
        /// Optional LLM healing providers to register for fallback consensus.
        /// </summary>
        public IEnumerable<ILlmHealingProvider>? LlmProviders { get; set; }

        /// <summary>
        /// Optional telemetry report sink. Defaults to environment file sink.
        /// </summary>
        public IHealingReportSink? ReportSink { get; set; }

        /// <summary>
        /// Optional logging callback for resolution diagnostics.
        /// </summary>
        public Action<string>? LogAction { get; set; }
    }

    /// <summary>
    /// Test lifecycle fixture helper compatible with xUnit (<c>IClassFixture&lt;SelfHealingTestFixture&gt;</c>)
    /// and NUnit (<c>[SetUpFixture]</c> or test fixture field). Manages repository lifecycle and provides
    /// simplified auto-healing execution helpers.
    /// </summary>
    public class SelfHealingTestFixture : IDisposable
    {
        private readonly bool _isTemporaryRepository;
        private readonly bool _autoDeleteOnDispose;
        private bool _disposed;

        public LocatorRepository Repository { get; }
        public SelfHealingEngine Engine { get; }
        public string RepositoryPath { get; }
        public Action<string>? LogAction { get; set; }

        /// <summary>
        /// Creates a new <see cref="SelfHealingTestFixture"/> with default configuration options.
        /// (Single public constructor required for xUnit <c>IClassFixture</c> compatibility).
        /// </summary>
        public SelfHealingTestFixture()
            : this(null)
        {
        }

        /// <summary>
        /// Protected constructor for subclasses configuring custom options.
        /// </summary>
        protected SelfHealingTestFixture(SelfHealingTestOptions? options)
        {
            var opt = options ?? new SelfHealingTestOptions();

            if (!string.IsNullOrEmpty(opt.RepositoryPath))
            {
                RepositoryPath = opt.RepositoryPath!;
                _isTemporaryRepository = false;
            }
            else
            {
                RepositoryPath = Path.Combine(
                    Path.GetTempPath(),
                    "AutomationSandbox.TestFixture." + Guid.NewGuid().ToString("N") + ".locator.json");
                _isTemporaryRepository = true;
            }

            _autoDeleteOnDispose = opt.AutoDeleteRepositoryOnDispose;
            LogAction = opt.LogAction;

            Repository = new LocatorRepository(RepositoryPath);

            var weights = opt.CustomWeights ?? SimilarityWeights.FromProfile(opt.Profile);

            Engine = new SelfHealingEngine(
                repository: Repository,
                weights: weights,
                llmProviders: opt.LlmProviders,
                reportSink: opt.ReportSink,
                mode: opt.Mode);
        }

        /// <summary>
        /// Creates a <see cref="SelfHealingTestFixture"/> configured with custom options.
        /// </summary>
        public static SelfHealingTestFixture Create(SelfHealingTestOptions options)
        {
            return new SelfHealingTestFixture(options);
        }

        /// <summary>
        /// Executes an action with self-healing support, retrying on locator failure and persisting the updated locator.
        /// </summary>
        public Task<T> ExecuteWithHealingAsync<T>(
            string locatorKey,
            UiElementInfo expected,
            Func<UiElementInfo, Task<T>> action,
            Func<UiElementInfo> captureTreeRoot,
            string? testIntent = null,
            string? platform = null,
            CancellationToken cancellationToken = default,
            Func<Exception, bool>? shouldHeal = null)
        {
            return Engine.ExecuteWithHealingAsync(
                locatorKey: locatorKey,
                expected: expected,
                action: action,
                captureTreeRoot: captureTreeRoot,
                testIntent: testIntent,
                log: LogAction,
                platform: platform,
                cancellationToken: cancellationToken,
                shouldHeal: shouldHeal);
        }

        /// <summary>
        /// Executes a void action with self-healing support.
        /// </summary>
        public async Task ExecuteWithHealingAsync(
            string locatorKey,
            UiElementInfo expected,
            Func<UiElementInfo, Task> action,
            Func<UiElementInfo> captureTreeRoot,
            string? testIntent = null,
            string? platform = null,
            CancellationToken cancellationToken = default,
            Func<Exception, bool>? shouldHeal = null)
        {
            await Engine.ExecuteWithHealingAsync<bool>(
                locatorKey: locatorKey,
                expected: expected,
                action: async element =>
                {
                    await action(element).ConfigureAwait(false);
                    return true;
                },
                captureTreeRoot: captureTreeRoot,
                testIntent: testIntent,
                log: LogAction,
                platform: platform,
                cancellationToken: cancellationToken,
                shouldHeal: shouldHeal).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves a candidate match against a live UI tree root.
        /// </summary>
        public Task<HealResult> ResolveAsync(
            UiElementInfo expected,
            UiElementInfo currentTreeRoot,
            string? platform = null,
            CancellationToken cancellationToken = default)
        {
            return SelfHealingResolver.ResolveAsync(
                expected: expected,
                currentTreeRoot: currentTreeRoot,
                llmProviders: Engine.LlmProviders,
                weights: Engine.Weights,
                log: LogAction,
                platform: platform,
                cancellationToken: cancellationToken);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing && _isTemporaryRepository && _autoDeleteOnDispose)
            {
                TryDeleteFile(RepositoryPath);
                TryDeleteFile(RepositoryPath + ".lock");
            }

            _disposed = true;
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Optional abstract base class for test classes providing direct access to self-healing fixture helpers.
    /// </summary>
    public abstract class SelfHealingTestBase : IDisposable
    {
        public SelfHealingTestFixture Fixture { get; }

        public LocatorRepository Repository => Fixture.Repository;
        public SelfHealingEngine Engine => Fixture.Engine;

        protected SelfHealingTestBase(SelfHealingTestOptions? options = null)
        {
            Fixture = options != null ? SelfHealingTestFixture.Create(options) : new SelfHealingTestFixture();
        }

        protected Task<T> ExecuteWithHealingAsync<T>(
            string locatorKey,
            UiElementInfo expected,
            Func<UiElementInfo, Task<T>> action,
            Func<UiElementInfo> captureTreeRoot,
            string? testIntent = null,
            string? platform = null,
            CancellationToken cancellationToken = default,
            Func<Exception, bool>? shouldHeal = null)
        {
            return Fixture.ExecuteWithHealingAsync(locatorKey, expected, action, captureTreeRoot, testIntent, platform, cancellationToken, shouldHeal);
        }

        protected Task ExecuteWithHealingAsync(
            string locatorKey,
            UiElementInfo expected,
            Func<UiElementInfo, Task> action,
            Func<UiElementInfo> captureTreeRoot,
            string? testIntent = null,
            string? platform = null,
            CancellationToken cancellationToken = default,
            Func<Exception, bool>? shouldHeal = null)
        {
            return Fixture.ExecuteWithHealingAsync(locatorKey, expected, action, captureTreeRoot, testIntent, platform, cancellationToken, shouldHeal);
        }

        public virtual void Dispose()
        {
            Fixture.Dispose();
        }
    }
}
