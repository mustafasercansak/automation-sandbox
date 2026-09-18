using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutomationSandbox.LlmHealing;
using AutomationSandbox.UiModel;

namespace AutomationSandbox.SelfHealing.Testing
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
        /// Optional telemetry report sink. Defaults to an isolated temporary file scoped to this fixture,
        /// so tests never write into a shared, environment-configured healing report (e.g. the CI benchmark
        /// telemetry file) unless explicitly asked to.
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
        private readonly string? _temporaryReportPath;
        private bool _disposed;

        /// <summary>Locator repository owned or supplied to this fixture.</summary>
        public LocatorRepository Repository { get; }
        /// <summary>Healing engine configured for this fixture&apos;s repository and options.</summary>
        public SelfHealingEngine Engine { get; }
        /// <summary>Filesystem path of the locator repository used by the test fixture.</summary>
        public string RepositoryPath { get; }
        /// <summary>Optional callback receiving engine diagnostic messages.</summary>
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

            IHealingReportSink reportSink;
            if (opt.ReportSink != null)
            {
                reportSink = opt.ReportSink;
            }
            else
            {
                _temporaryReportPath = Path.Combine(
                    Path.GetTempPath(),
                    "AutomationSandbox.TestFixture." + Guid.NewGuid().ToString("N") + ".healing-report.json");
                reportSink = new HealingReportFileSink(_temporaryReportPath);
            }

            Engine = new SelfHealingEngine(
                repository: Repository,
                weights: weights,
                llmProviders: opt.LlmProviders,
                reportSink: reportSink,
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

        /// <summary>Releases fixture resources and optional temporary repository files.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Releases fixture resources and optional temporary repository files.</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing && _autoDeleteOnDispose)
            {
                if (_isTemporaryRepository)
                {
                    TryDeleteFile(RepositoryPath);
                    TryDeleteFile(RepositoryPath + ".lock");
                }

                if (_temporaryReportPath != null)
                {
                    TryDeleteFile(_temporaryReportPath);
                    TryDeleteFile(_temporaryReportPath + ".lock");
                    TryDeleteFile(Path.ChangeExtension(_temporaryReportPath, ".html"));
                }
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
        /// <summary>Shared fixture that owns the engine and repository lifecycle.</summary>
        public SelfHealingTestFixture Fixture { get; }

        /// <summary>Persistent locator repository exposed to derived test fixtures.</summary>
        public LocatorRepository Repository => Fixture.Repository;
        /// <summary>Healing engine exposed to derived test fixtures.</summary>
        public SelfHealingEngine Engine => Fixture.Engine;

        /// <summary>Creates the healing fixture used by a derived test class, with default options when none are supplied.</summary>
        protected SelfHealingTestBase(SelfHealingTestOptions? options = null)
        {
            Fixture = options != null ? SelfHealingTestFixture.Create(options) : new SelfHealingTestFixture();
        }

        /// <summary>Executes an action through the fixture&apos;s healing engine, capturing a fresh tree when an eligible locator failure requires resolution.</summary>
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

        /// <summary>Executes an action through the fixture&apos;s healing engine, capturing a fresh tree when an eligible locator failure requires resolution.</summary>
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

        /// <summary>Releases the owned healing fixture.</summary>
        public virtual void Dispose()
        {
            Fixture.Dispose();
        }
    }
}
