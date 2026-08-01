using Discovery;
using FlaUI.Core.AutomationElements;

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
            Assert.False(panel.IsOffscreen);
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

        public void Dispose()
        {
            _connector.Dispose();
        }
    }
}
