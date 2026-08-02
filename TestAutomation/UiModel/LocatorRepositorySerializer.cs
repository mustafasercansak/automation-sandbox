using System.Text.Json;

namespace UiModel
{
    public static class LocatorRepositorySerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
        };

        public static string ToJson(LocatorRepositoryDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            ValidateSchemaVersion(document.SchemaVersion);
            return JsonSerializer.Serialize(document, Options);
        }

        public static LocatorRepositoryDocument FromJson(string json)
        {
            var document = JsonSerializer.Deserialize<LocatorRepositoryDocument>(json, Options)
                ?? throw new JsonException("Failed to deserialize locator repository JSON.");
            ValidateSchemaVersion(document.SchemaVersion);
            return document;
        }

        private static void ValidateSchemaVersion(int schemaVersion)
        {
            if (schemaVersion != LocatorRepositoryDocument.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Locator repository schema version {schemaVersion} is not supported. Current version is {LocatorRepositoryDocument.CurrentSchemaVersion}.");
            }
        }
    }
}
