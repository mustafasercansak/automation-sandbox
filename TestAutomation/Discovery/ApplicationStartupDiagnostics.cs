using System;

namespace Discovery
{
    internal static class ApplicationStartupDiagnostics
    {
        public static InvalidOperationException CreateFailure(
            int processId,
            TimeSpan elapsed,
            bool hasExited,
            int? exitCode,
            bool sawNativeMainWindowHandle,
            int topLevelWindowCount,
            Exception? lastUiaError)
        {
            var classification = Classify(
                hasExited,
                sawNativeMainWindowHandle,
                topLevelWindowCount,
                lastUiaError);
            var exitCodeText = exitCode.HasValue ? exitCode.Value.ToString() : "n/a";
            var lastErrorText = lastUiaError == null
                ? "none"
                : lastUiaError.GetType().Name + ": " + lastUiaError.Message;
            var message =
                $"Main window was not available after {elapsed.TotalSeconds:F1}s " +
                $"(classification={classification}, processId={processId}, hasExited={hasExited}, " +
                $"exitCode={exitCodeText}, nativeMainWindowHandleSeen={sawNativeMainWindowHandle}, " +
                $"topLevelWindowCount={topLevelWindowCount}, lastUiaError={lastErrorText}).";

            return new InvalidOperationException(message, lastUiaError);
        }

        private static string Classify(
            bool hasExited,
            bool sawNativeMainWindowHandle,
            int topLevelWindowCount,
            Exception? lastUiaError)
        {
            if (hasExited)
            {
                return "early-exit";
            }

            if (topLevelWindowCount > 1)
            {
                return "ambiguous-windows";
            }

            if (sawNativeMainWindowHandle || lastUiaError != null)
            {
                return "uia-attach";
            }

            return "slow-startup";
        }
    }
}
