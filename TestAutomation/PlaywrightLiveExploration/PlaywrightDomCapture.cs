using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using AutomationSandbox.WebDiscovery;

namespace AutomationSandbox.PlaywrightLiveExploration
{
    // Shared by PlaywrightLiveExplorer.CaptureAsync (one page per call) and PlaywrightWebSession
    // (one page across many calls) so the DOM-capture-script evaluation and JSON round-trip live
    // in exactly one place.
    internal static class PlaywrightDomCapture
    {
        // Playwright's own EvaluateAsync<T> deserializer reflects over settable properties and
        // cannot populate AutomationSandbox.UiModel.BoundingRectangle (a readonly struct with a constructor, no
        // setters) - observed to throw "Property set method not found." live against a real
        // Chromium page. Round-tripping through a JSON string and System.Text.Json (which
        // supports constructor-matched deserialization) sidesteps that, and matches how
        // PlaywrightApplicationConnector.ParseJson already deserializes DOM capture JSON.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public static async Task<WebElementInfo> CaptureAsync(IPage page, WebDiscoveryOptions? discoveryOptions, string errorContext)
        {
            var captureScript = PlaywrightDomCaptureScript.BuildJavaScript(discoveryOptions);
            var stringifyScript = $"() => JSON.stringify(({captureScript})())";
            var json = await page.EvaluateAsync<string>(stringifyScript).ConfigureAwait(false);
            var dom = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<WebElementInfo>(json, JsonOptions);
            if (dom == null)
            {
                throw new InvalidOperationException($"DOM capture script returned no result for '{errorContext}'.");
            }

            return dom;
        }
    }
}
