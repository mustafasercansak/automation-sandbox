using Discovery;
using SelfHealing;

namespace ScenarioRunner
{
    public class SelfHealingResolverTests
    {
        [Fact]
        public void Resolve_FindsRenamedControl_ByStructuralSimilarity()
        {
            // "Önceki" snapshot: txtEmail'in AutomationId'si hâlâ doğruyken alınmış hali.
            var expected = new UiElementInfo
            {
                ControlType = "Edit",
                Name = "",
                AutomationId = "txtEmail",
                ParentControlType = "Window",
                ParentAutomationId = "MainForm",
                SiblingIndex = 2,
                SiblingCount = 7,
                BoundingRectangle = new BoundingRectangle(112, 70, 200, 23),
            };

            // "Şimdiki" ağaç: refactor sonrası txtEmail'in AutomationId'si "textBox1" olmuş,
            // ama pozisyonu ve sibling context'i aynı kalmış.
            var currentTree = BuildCurrentMainFormTree(renamedEmailAutomationId: "textBox1");

            var result = SelfHealingResolver.Resolve(expected, currentTree, log: _ => { });

            Assert.NotNull(result.Matched);
            Assert.Equal("textBox1", result.Matched!.AutomationId);
            Assert.True(result.IsConfident, $"Beklenen güvenli eşleşme sağlanamadı, skor: {result.Score}");
        }

        [Fact]
        public void Resolve_ReturnsNoMatch_WhenControlTypeNoLongerExists()
        {
            var expected = new UiElementInfo
            {
                ControlType = "Hyperlink",
                AutomationId = "lnkGizli",
                ParentControlType = "Window",
                SiblingIndex = 0,
                SiblingCount = 1,
                BoundingRectangle = new BoundingRectangle(0, 0, 10, 10),
            };

            var currentTree = BuildCurrentMainFormTree(renamedEmailAutomationId: "txtEmail");

            var result = SelfHealingResolver.Resolve(expected, currentTree, log: _ => { });

            Assert.Null(result.Matched);
            Assert.Equal(0, result.CandidateCount);
        }

        private static UiElementInfo BuildCurrentMainFormTree(string renamedEmailAutomationId)
        {
            var root = new UiElementInfo { ControlType = "Window", Name = "Müşteri Kayıt Formu", AutomationId = "MainForm" };

            var children = new[]
            {
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtAdi", BoundingRectangle = new BoundingRectangle(112, 12, 200, 23) },
                new UiElementInfo { ControlType = "Edit", AutomationId = "txtSoyad", BoundingRectangle = new BoundingRectangle(112, 41, 200, 23) },
                new UiElementInfo { ControlType = "Edit", AutomationId = renamedEmailAutomationId, BoundingRectangle = new BoundingRectangle(112, 70, 200, 23) },
                new UiElementInfo { ControlType = "ComboBox", AutomationId = "cmbKayitTuru", BoundingRectangle = new BoundingRectangle(112, 99, 200, 23) },
                new UiElementInfo { ControlType = "Pane", AutomationId = "panel1", BoundingRectangle = new BoundingRectangle(12, 131, 300, 34) },
                new UiElementInfo { ControlType = "Button", AutomationId = "btnKaydet", BoundingRectangle = new BoundingRectangle(112, 178, 100, 30) },
                new UiElementInfo { ControlType = "DataGrid", AutomationId = "dgvKayitlar", BoundingRectangle = new BoundingRectangle(12, 220, 400, 150) },
            };

            for (var i = 0; i < children.Length; i++)
            {
                children[i].ParentControlType = root.ControlType;
                children[i].ParentAutomationId = root.AutomationId;
                children[i].SiblingIndex = i;
                children[i].SiblingCount = children.Length;
                root.Children.Add(children[i]);
            }

            return root;
        }
    }
}
