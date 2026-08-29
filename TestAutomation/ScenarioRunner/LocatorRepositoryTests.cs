using System.Threading.Tasks;
using SelfHealing;
using UiModel;
namespace ScenarioRunner
{
    public class LocatorRepositoryTests : IDisposable
    {
        private readonly string _directory;
        private readonly string _filePath;

        public LocatorRepositoryTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "LocatorRepositoryTests_" + Guid.NewGuid().ToString("N"));
            _filePath = Path.Combine(_directory, "locators.json");
        }

        [Fact]
        public void Load_WhenFileDoesNotExist_ReturnsEmptyDocument()
        {
            var repository = new LocatorRepository(_filePath);

            var document = repository.Load();

            Assert.Empty(document.Locators);
            Assert.Equal(LocatorRepositoryDocument.CurrentSchemaVersion, document.SchemaVersion);
        }

        [Fact]
        public void Save_ThenLoad_RoundTrips_AndCreatesDirectory()
        {
            var repository = new LocatorRepository(_filePath);
            var document = new LocatorRepositoryDocument { ApplicationName = "DemoApp" };
            document.Locators.Add(new LocatorRecord
            {
                LocatorKey = "CustomerForm.Email",
                Snapshot = new UiElementInfo
                {
                    ControlType = "Edit",
                    AutomationId = "txtEmail",
                    BoundingRectangle = new BoundingRectangle(100, 200, 150, 25),
                },
            });

            repository.Save(document);
            var loaded = repository.Load();

            Assert.True(File.Exists(_filePath));
            Assert.Equal("DemoApp", loaded.ApplicationName);
            Assert.Equal("txtEmail", loaded.Locators.Single().Snapshot.AutomationId);
            Assert.Equal(100, loaded.Locators.Single().Snapshot.BoundingRectangle.X);
            Assert.Equal(200, loaded.Locators.Single().Snapshot.BoundingRectangle.Y);
            Assert.Equal(150, loaded.Locators.Single().Snapshot.BoundingRectangle.Width);
            Assert.Equal(25, loaded.Locators.Single().Snapshot.BoundingRectangle.Height);
        }

        [Fact]
        public void Upsert_WhenKeyIsNew_AddsRecordWithTimestamps()
        {
            var repository = new LocatorRepository(_filePath);
            var snapshot = new UiElementInfo
            {
                ControlType = "Edit",
                AutomationId = "txtEmail",
                BoundingRectangle = new BoundingRectangle(100, 200, 150, 25),
            };

            var record = repository.Upsert("CustomerForm.Email", snapshot, applicationName: "DemoApp");

            Assert.Equal("CustomerForm.Email", record.LocatorKey);
            Assert.Equal("txtEmail", record.Snapshot.AutomationId);
            Assert.Equal(100, record.Snapshot.BoundingRectangle.X);
            Assert.Equal(200, record.Snapshot.BoundingRectangle.Y);
            Assert.Equal(150, record.Snapshot.BoundingRectangle.Width);
            Assert.Equal(25, record.Snapshot.BoundingRectangle.Height);
            Assert.Equal(record.CreatedAt, record.UpdatedAt);
            Assert.Equal("DemoApp", repository.Load().ApplicationName);
        }

        [Fact]
        public void Upsert_WhenKeyAlreadyExists_UpdatesInPlaceInsteadOfDuplicating()
        {
            var repository = new LocatorRepository(_filePath);
            var original = repository.Upsert("CustomerForm.Email", new UiElementInfo { AutomationId = "txtEmailAddress" });

            var updatedSnapshot = new UiElementInfo { AutomationId = "txtEmail" };
            var entry = new LocatorHealingHistoryEntry { Source = "heuristic", Score = 0.95 };
            var updated = repository.Upsert("CustomerForm.Email", updatedSnapshot, entry);

            var document = repository.Load();
            Assert.Single(document.Locators);
            Assert.Equal("txtEmail", updated.Snapshot.AutomationId);
            Assert.Equal(original.CreatedAt, updated.CreatedAt);
            Assert.True(updated.UpdatedAt >= original.UpdatedAt);
            Assert.Single(updated.HealingHistory);
        }

        [Fact]
        public void Upsert_PersistsSnapshotWithoutDescendantTreeBloat()
        {
            var repository = new LocatorRepository(_filePath);
            var liveNode = new UiElementInfo { AutomationId = "txtEmail" };
            liveNode.Children.Add(new UiElementInfo { AutomationId = "autocompletePopup" });

            repository.Upsert("CustomerForm.Email", liveNode);

            var persisted = repository.Find("CustomerForm.Email");
            Assert.NotNull(persisted);
            Assert.Equal("txtEmail", persisted!.Snapshot.AutomationId);
            Assert.Empty(persisted.Snapshot.Children);
        }

        [Fact]
        public async Task Upsert_CalledConcurrentlyForDifferentKeys_NeverLosesAnUpdate()
        {
            var repository = new LocatorRepository(_filePath);
            var keys = Enumerable.Range(0, 20).Select(i => $"Locator.{i}").ToList();

            await Task.WhenAll(keys.Select(key => Task.Run(() =>
                repository.Upsert(key, new UiElementInfo { AutomationId = key }))));

            var document = repository.Load();
            Assert.Equal(keys.Count, document.Locators.Count);
            foreach (var key in keys)
            {
                Assert.Contains(document.Locators, r => r.LocatorKey == key);
            }
        }

        [Fact]
        public void Find_ReturnsNull_WhenLocatorKeyIsNotPresent()
        {
            var repository = new LocatorRepository(_filePath);
            repository.Upsert("CustomerForm.Email", new UiElementInfo { AutomationId = "txtEmail" });

            Assert.Null(repository.Find("CustomerForm.Phone"));
        }

        [Fact]
        public void Find_ReusesCachedDocument_WhenFileHasNotChanged()
        {
            var repository = new LocatorRepository(_filePath);
            repository.Upsert("CustomerForm.Email", new UiElementInfo { AutomationId = "txtEmail" });

            var first = repository.Find("CustomerForm.Email");
            var second = repository.Find("CustomerForm.Email");

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Same(first, second);
        }

        [Fact]
        public void Find_InvalidatesCache_WhenSecondInstanceModifiesFile()
        {
            var repo1 = new LocatorRepository(_filePath);
            var repo2 = new LocatorRepository(_filePath);

            repo1.Upsert("CustomerForm.Email", new UiElementInfo { AutomationId = "txtEmail" });

            var firstFromRepo1 = repo1.Find("CustomerForm.Email");
            Assert.NotNull(firstFromRepo1);
            Assert.Equal("txtEmail", firstFromRepo1!.Snapshot.AutomationId);

            // Second instance updates the record
            repo2.Upsert("CustomerForm.Email", new UiElementInfo { AutomationId = "txtEmail_Updated" });

            // First instance must observe the update (cache invalidated by mtime/size change)
            var updatedFromRepo1 = repo1.Find("CustomerForm.Email");
            Assert.NotNull(updatedFromRepo1);
            Assert.Equal("txtEmail_Updated", updatedFromRepo1!.Snapshot.AutomationId);
        }

        [Fact]
        public void Find_UsesFirstWins_WhenDuplicateKeysExistInDocument()
        {
            var repository = new LocatorRepository(_filePath);
            var doc = new LocatorRepositoryDocument();
            doc.Locators.Add(new LocatorRecord { LocatorKey = "DupeKey", Snapshot = new UiElementInfo { AutomationId = "first" } });
            doc.Locators.Add(new LocatorRecord { LocatorKey = "DupeKey", Snapshot = new UiElementInfo { AutomationId = "second" } });
            repository.Save(doc);

            var found = repository.Find("DupeKey");
            Assert.NotNull(found);
            Assert.Equal("first", found!.Snapshot.AutomationId);
        }

        [Fact]
        public void Find_WhenSecondInstanceModifiesFileWithCollidingMtimeAndLength_InvalidatesCache()
        {
            var repo1 = new LocatorRepository(_filePath);
            var repo2 = new LocatorRepository(_filePath);

            repo1.Upsert("CustomerForm.Email", new UiElementInfo { AutomationId = "txtEmail_AAAA" });

            var firstFromRepo1 = repo1.Find("CustomerForm.Email");
            Assert.NotNull(firstFromRepo1);
            Assert.Equal("txtEmail_AAAA", firstFromRepo1!.Snapshot.AutomationId);

            var originalFileInfo = new FileInfo(_filePath);
            var originalMtime = originalFileInfo.LastWriteTimeUtc;

            // Second instance updates the record. "AAAA" -> "BBBB" keeps the AutomationId itself the
            // same length, but Upsert also stamps a fresh UpdatedAt timestamp, whose serialized
            // fractional-second digits System.Text.Json trims to their significant length -- so the
            // resulting file length can still drift by a byte or two between writes and isn't
            // something this test can reliably force, only mtime is (below).
            repo2.Upsert("CustomerForm.Email", new UiElementInfo { AutomationId = "txtEmail_BBBB" });

            // Force mtime on disk to match the original timestamp exactly so mtime collides even
            // though length may or may not.
            File.SetLastWriteTimeUtc(_filePath, originalMtime);
            var updatedFileInfo = new FileInfo(_filePath);
            Assert.Equal(originalMtime, updatedFileInfo.LastWriteTimeUtc);

            // First instance must detect the content hash mismatch and return the updated record
            var updatedFromRepo1 = repo1.Find("CustomerForm.Email");
            Assert.NotNull(updatedFromRepo1);
            Assert.Equal("txtEmail_BBBB", updatedFromRepo1!.Snapshot.AutomationId);
        }

        [Fact]
        public void Upsert_WhenConcurrentInstanceModifiesFileWithCollidingMtimeAndLength_DoesNotLoseUpdate()
        {
            var repo1 = new LocatorRepository(_filePath);
            var repo2 = new LocatorRepository(_filePath);

            // Process 1 writes key 1
            repo1.Upsert("Key1", new UiElementInfo { AutomationId = "btnSubmitA" });
            var originalMtime = new FileInfo(_filePath).LastWriteTimeUtc;

            // Process 2 updates key 1. Same-length AutomationId, but the file's actual byte length can
            // still drift a little via Upsert's own UpdatedAt timestamp -- only mtime is forced below.
            repo2.Upsert("Key1", new UiElementInfo { AutomationId = "btnSubmitB" });

            // Force mtime on disk to match original mtime
            File.SetLastWriteTimeUtc(_filePath, originalMtime);
            Assert.Equal(originalMtime, new FileInfo(_filePath).LastWriteTimeUtc);

            // Process 1 now performs an Upsert for Key2. It must not use stale cached document (which lacked btnSubmitB)
            repo1.Upsert("Key2", new UiElementInfo { AutomationId = "btnCancel" });

            var loadedDoc = repo1.Load();
            var key1Record = loadedDoc.Locators.FirstOrDefault(r => r.LocatorKey == "Key1");
            var key2Record = loadedDoc.Locators.FirstOrDefault(r => r.LocatorKey == "Key2");

            Assert.NotNull(key1Record);
            Assert.Equal("btnSubmitB", key1Record!.Snapshot.AutomationId);
            Assert.NotNull(key2Record);
            Assert.Equal("btnCancel", key2Record!.Snapshot.AutomationId);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    public class LocatorHealingHistoryEntryFactoryTests
    {
        [Fact]
        public void FromHealResult_MapsHeuristicResultFields()
        {
            var matched = new UiElementInfo { AutomationId = "txtEmail" };
            matched.Children.Add(new UiElementInfo { AutomationId = "childThatShouldNotPersist" });
            var previous = new UiElementInfo { AutomationId = "txtEmailAddress" };
            previous.Children.Add(new UiElementInfo { AutomationId = "oldChildThatShouldNotPersist" });
            var result = new HealResult
            {
                Matched = matched,
                Score = 0.91,
                Source = HealSource.Heuristic,
                ConfidenceThreshold = 0.5,
            };

            var entry = LocatorHealingHistoryEntryFactory.FromHealResult(result, previous);

            Assert.Equal("heuristic", entry.Source);
            Assert.Equal(0.91, entry.Score);
            Assert.NotSame(matched, entry.AcceptedSnapshot);
            Assert.NotSame(previous, entry.PreviousSnapshot);
            Assert.Equal("txtEmail", entry.AcceptedSnapshot!.AutomationId);
            Assert.Equal("txtEmailAddress", entry.PreviousSnapshot!.AutomationId);
            Assert.Empty(entry.AcceptedSnapshot.Children);
            Assert.Empty(entry.PreviousSnapshot.Children);
            Assert.Null(entry.LlmProviderName);
        }

        [Fact]
        public void FromHealResult_MapsLlmProviderNameAsSource()
        {
            var result = new HealResult
            {
                Matched = new UiElementInfo { AutomationId = "txtEmail" },
                Source = HealSource.Llm,
                LlmProviderName = "Gemini",
                LlmConfidence = 0.9,
            };

            var entry = LocatorHealingHistoryEntryFactory.FromHealResult(result, previousSnapshot: null);

            Assert.Equal("Gemini", entry.Source);
            Assert.Equal(0.9, entry.LlmConfidence);
            Assert.Null(entry.PreviousSnapshot);
        }

        [Fact]
        public void FromHealResult_ThrowsWhenNoMatchWasFound()
        {
            var result = new HealResult { Matched = null };

            Assert.Throws<InvalidOperationException>(() => LocatorHealingHistoryEntryFactory.FromHealResult(result, previousSnapshot: null));
        }
    }
}
