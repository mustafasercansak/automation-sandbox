using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutomationSandbox.UiModel;

namespace ScenarioRunner
{
    // The organic drift dataset (#663) as plain data: two genuine releases of a third-party application, every interactive element of the old
    // one as a stored locator and a reviewed target in the new one. Shared by the dataset's own tests and by the heal-review agent harness
    // (#676), so both read exactly the same files in exactly the same way.
    internal static class OrganicDriftData
    {
        public static string DatasetPath(params string[] parts)
        {
            var all = new List<string> { AppContext.BaseDirectory, "Fixtures", "organic-drift", "swagger-ui" };
            all.AddRange(parts);
            return Path.Combine(all.ToArray());
        }

        public static JsonDocument LoadGroundTruth()
        {
            var path = DatasetPath("ground-truth.json");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Organic drift ground truth not found at '" + path + "'.");
            }

            return JsonDocument.Parse(File.ReadAllText(path));
        }

        public static UiElementInfo LoadTree(string version, string state)
        {
            var path = DatasetPath("trees", version, state + ".json");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Captured tree not found at '" + path + "'.");
            }

            return UiTreeSerializer.FromJson(File.ReadAllText(path));
        }

        public static List<UiElementInfo> PreOrder(UiElementInfo root)
        {
            var result = new List<UiElementInfo>();
            var stack = new Stack<UiElementInfo>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                result.Add(node);
                for (var i = node.Children.Count - 1; i >= 0; i--)
                {
                    stack.Push(node.Children[i]);
                }
            }

            return result;
        }

        public static List<UiElementInfo> Interactive(UiElementInfo root, HashSet<string> controlTypes)
        {
            return PreOrder(root).Where(node => controlTypes.Contains(node.ControlType)).ToList();
        }

        public static HashSet<string> InteractiveTypes(JsonDocument truth)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in truth.RootElement.GetProperty("interactiveControlTypes").EnumerateArray())
            {
                set.Add(type.GetString()!);
            }

            return set;
        }

        // The stored locator is the old element as the capture reported it, without its subtree: what a suite records once and
        // keeps, like the --expected file of the heal CLI.
        public static UiElementInfo StoredLocator(UiElementInfo node)
        {
            var copy = UiTreeSerializer.FromJson(UiTreeSerializer.ToJson(node));
            copy.Children = new List<UiElementInfo>();
            return copy;
        }
    }
}
