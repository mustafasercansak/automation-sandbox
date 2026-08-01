using System.Text.Json;

namespace Discovery
{
    public static class UiTreeSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
        };

        public static string ToJson(UiElementInfo root) => JsonSerializer.Serialize(root, Options);

        public static UiElementInfo FromJson(string json) =>
            JsonSerializer.Deserialize<UiElementInfo>(json, Options)
            ?? throw new JsonException("UI tree JSON'dan deserialize edilemedi.");
    }
}
