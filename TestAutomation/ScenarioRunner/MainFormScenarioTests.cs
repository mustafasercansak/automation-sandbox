using Discovery;
using FlaUI.Core.AutomationElements;
using SelfHealing;

namespace ScenarioRunner
{
    // Bu testler derlenmiş WinFormsApp.exe'yi gerçekten başlatıp FlaUI.UIA3 ile konuşur.
    // UIA3, Windows'un UI Automation COM API'lerine dayandığı için sadece Windows'ta çalışır.
    public class MainFormScenarioTests : IDisposable
    {
        private const string WinFormsAppRelativePath = @"..\..\..\..\..\WinFormsApp\bin\Debug\net48\WinFormsApp.exe";

        private readonly ApplicationConnector _connector;

        public MainFormScenarioTests()
        {
            var exePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, WinFormsAppRelativePath));
            _connector = ApplicationConnector.Launch(exePath);
        }

        [Fact]
        public void KayitOlusturma_ZorunluAlanlarDoluysa_DataGridViewaSatirEkler()
        {
            var window = _connector.GetMainWindow();

            window.FindFirstDescendant(cf => cf.ByAutomationId("txtAdi"))!.AsTextBox().Text = "Ayşe";
            window.FindFirstDescendant(cf => cf.ByAutomationId("txtSoyad"))!.AsTextBox().Text = "Yılmaz";
            window.FindFirstDescendant(cf => cf.ByAutomationId("txtEmail"))!.AsTextBox().Text = "ayse.yilmaz@example.com";

            window.FindFirstDescendant(cf => cf.ByAutomationId("btnKaydet"))!.AsButton().Invoke();

            var grid = window.FindFirstDescendant(cf => cf.ByAutomationId("dgvKayitlar"))!.AsDataGridView();
            Assert.Single(grid.Rows);
        }

        [Fact]
        public void KurumsalSecilince_SirketAdiPaneli_Gorunur_Olur()
        {
            var window = _connector.GetMainWindow();

            var combo = window.FindFirstDescendant(cf => cf.ByAutomationId("cmbKayitTuru"))!.AsComboBox();
            combo.Select("Kurumsal");

            // panel1: AutomationId'si kasıtlı olarak anlamsız bırakılan kontrol (bkz. MainForm.Designer.cs).
            // Bu yüzden AutomationId yerine ControlType üzerinden buluyoruz - tam olarak
            // SelfHealing katmanının çözmesi gereken sorunu burada canlı örnekliyoruz.
            var panel = window.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Pane))!;

            // IsOffscreen bu native WinForms Panel'de desteklenmiyor (gerçek CI çalışmasında
            // PropertyNotSupportedException fırlattığı görüldü) - bounding rectangle her zaman
            // desteklenen, daha güvenilir bir görünürlük sinyali.
            var rect = panel.Properties.BoundingRectangle.ValueOrDefault;
            Assert.True(rect.Width > 0 && rect.Height > 0, $"Panel görünür olmalıydı ama bounding rectangle boş geldi: {rect}");
        }

        [Fact]
        public void UiTree_JsonaSerializeEdilebilir_VeBeklenenKontrolleriIcerir()
        {
            var window = _connector.GetMainWindow();
            var tree = UiTreeWalker.BuildTree(window);
            var json = UiTreeSerializer.ToJson(tree);

            Assert.Contains("txtEmail", json);
            Assert.Contains("btnKaydet", json);
        }

        [Fact]
        public void SelfHealing_KirikAutomationId_CanliUygulamadaDogruElemaniBulur()
        {
            var window = _connector.GetMainWindow();

            // Discovery ile canlı, gerçek UI ağacını çekiyoruz - self-healing tam olarak
            // bunun üzerinde çalışır.
            var currentTree = UiTreeWalker.BuildTree(window);
            var realEmailNode = FindByAutomationId(currentTree, "txtEmail")
                ?? throw new InvalidOperationException("txtEmail canlı ağaçta bulunamadı, test verisi geçersiz.");

            // "Bir önceki sprint'te kaydedilmiş" eski bir locator'ı simüle ediyoruz: o zaman
            // AutomationId "txtEposta" imiş, sonradan bir refactor'de "txtEmail" olmuş.
            // Diğer tüm yapısal bilgiler gerçek ağaçtan alınıyor - sadece AutomationId
            // kasıtlı olarak eski/yanlış.
            var staleExpected = new UiElementInfo
            {
                ControlType = realEmailNode.ControlType,
                Name = realEmailNode.Name,
                AutomationId = "txtEposta",
                ParentControlType = realEmailNode.ParentControlType,
                ParentAutomationId = realEmailNode.ParentAutomationId,
                SiblingIndex = realEmailNode.SiblingIndex,
                SiblingCount = realEmailNode.SiblingCount,
                BoundingRectangle = realEmailNode.BoundingRectangle,
            };

            // Eski id ile doğrudan arama gerçekten başarısız olur - canlı bir locator
            // kırılmasının birebir simülasyonu.
            var directHit = window.FindFirstDescendant(cf => cf.ByAutomationId("txtEposta"));
            Assert.Null(directHit);

            // Self-healing devreye girer: aynı canlı ağaç üzerinde yapısal benzerlikle
            // (AutomationId'ye hiç bakmadan) doğru elemanı buluyor.
            var healResult = SelfHealingResolver.Resolve(staleExpected, currentTree);

            Assert.NotNull(healResult.Matched);
            Assert.Equal("txtEmail", healResult.Matched!.AutomationId);
            Assert.True(healResult.IsConfident, $"Beklenen güvenli eşleşme sağlanamadı, skor: {healResult.Score}");
        }

        private static UiElementInfo? FindByAutomationId(UiElementInfo node, string automationId)
        {
            if (node.AutomationId == automationId)
            {
                return node;
            }

            foreach (var child in node.Children)
            {
                var found = FindByAutomationId(child, automationId);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }

        public void Dispose()
        {
            _connector.Dispose();
        }
    }
}
