using System.Text.Json;

namespace UiModel
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
            ?? throw new JsonException("Failed to deserialize UI tree from JSON.");
    }
}
