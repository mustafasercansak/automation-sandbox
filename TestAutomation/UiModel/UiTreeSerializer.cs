using System.Text.Json;
namespace AutomationSandbox.UiModel
{
    /// <summary>Serializes and deserializes editable UI trees as JSON.</summary>
    public static class UiTreeSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
        };

        /// <summary>Serializes the supplied document to the package&apos;s JSON representation.</summary>
        public static string ToJson(UiElementInfo root) => JsonSerializer.Serialize(root, Options);
        /// <summary>Deserializes JSON into the package&apos;s editable document model.</summary>
        public static UiElementInfo FromJson(string json) =>
            JsonSerializer.Deserialize<UiElementInfo>(json, Options)
            ?? throw new JsonException("Failed to deserialize UI tree from JSON.");
    }
}
