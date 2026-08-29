using SelfHealing;
using UiModel;

internal static class Program
{
    private const string LocatorKey = "Checkout.SubmitButton";

    public static async Task<int> Main()
    {
        var repositoryPath = Path.Combine(
            Path.GetTempPath(),
            "AutomationSandbox.Quickstart." + Guid.NewGuid().ToString("N") + ".locator.json");

        try
        {
            var repository = new LocatorRepository(repositoryPath);

            // AutoHeal applies the healed locator and retries the action. The default
            // HealingMode.Review only records a review entry and fails closed - use it when
            // a human vets every heal.
            var engine = new SelfHealingEngine(repository, mode: HealingMode.AutoHeal);

            var staleLocator = new UiElementInfo
            {
                ControlType = "Button",
                AutomationId = "btn-submit",
                Name = "Submit order",
                ParentControlType = "Form",
                SiblingIndex = 1,
                SiblingCount = 3,
                BoundingRectangle = new BoundingRectangle(500, 400, 160, 40),
                TestIntent = "Submit the checkout form",
            };

            var liveTree = BuildRefactoredTree();
            var attempts = 0;

            var clickedAutomationId = await engine.ExecuteWithHealingAsync(
                locatorKey: LocatorKey,
                expected: staleLocator,
                action: element =>
                {
                    attempts++;
                    if (element.AutomationId == staleLocator.AutomationId)
                    {
                        throw new ElementNotFoundException("The stored locator no longer exists.");
                    }

                    return Task.FromResult(element.AutomationId);
                },
                captureTreeRoot: () => liveTree,
                log: Console.WriteLine);

            var saved = repository.Find(LocatorKey);
            if (attempts != 2 ||
                clickedAutomationId != "checkout-confirm" ||
                saved?.Snapshot.AutomationId != "checkout-confirm" ||
                saved.HealingHistory.Count != 1)
            {
                throw new InvalidOperationException("The end-to-end healing result was not persisted as expected.");
            }

            Console.WriteLine();
            Console.WriteLine("Success: the stale locator was healed and the retried action passed.");
            Console.WriteLine($"Stored locator: {saved.Snapshot.AutomationId}");
            Console.WriteLine($"Healing source: {saved.HealingHistory[0].Source}");
            return 0;
        }
        finally
        {
            DeleteIfPresent(repositoryPath);
            DeleteIfPresent(repositoryPath + ".lock");
        }
    }

    private static UiElementInfo BuildRefactoredTree()
    {
        return new UiElementInfo
        {
            ControlType = "Window",
            AutomationId = "checkout-window",
            Children =
            {
                new UiElementInfo
                {
                    ControlType = "Form",
                    AutomationId = "checkout-form",
                    Children =
                    {
                        new UiElementInfo
                        {
                            ControlType = "Edit",
                            AutomationId = "email",
                            Name = "Email address",
                            ParentControlType = "Form",
                            SiblingIndex = 0,
                            SiblingCount = 3,
                            BoundingRectangle = new BoundingRectangle(500, 330, 280, 40),
                        },
                        new UiElementInfo
                        {
                            ControlType = "Button",
                            AutomationId = "checkout-confirm",
                            Name = "Confirm order",
                            ParentControlType = "Form",
                            SiblingIndex = 1,
                            SiblingCount = 3,
                            BoundingRectangle = new BoundingRectangle(505, 402, 160, 40),
                        },
                        new UiElementInfo
                        {
                            ControlType = "Button",
                            AutomationId = "checkout-cancel",
                            Name = "Cancel",
                            ParentControlType = "Form",
                            SiblingIndex = 2,
                            SiblingCount = 3,
                            BoundingRectangle = new BoundingRectangle(700, 500, 120, 40),
                        },
                    },
                },
            },
        };
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class ElementNotFoundException : Exception
    {
        public ElementNotFoundException(string message) : base(message)
        {
        }
    }
}
