using System;
using System.Text.Json;
using UiModel;

namespace WebDiscovery
{
    public static class PlaywrightApplicationConnector
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public static UiElementInfo ParseJson(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                throw new ArgumentException("DOM capture JSON string cannot be null or empty.", nameof(rawJson));
            }

            var webElementRoot = JsonSerializer.Deserialize<WebElementInfo>(rawJson, JsonOptions);
            if (webElementRoot == null)
            {
                throw new InvalidOperationException("Failed to deserialize DOM capture JSON into WebElementInfo.");
            }

            return WebElementMapper.ToUiElementTree(webElementRoot);
        }
    }
}
