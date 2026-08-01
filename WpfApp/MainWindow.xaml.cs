using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace WpfApp
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<CustomerRecord> _records = new();

        public MainWindow()
        {
            InitializeComponent();
            DgvRecords.ItemsSource = _records;
            CmbRecordType.SelectedIndex = 0;
        }

        private void CmbRecordType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var isCorporate = (CmbRecordType.SelectedItem as ComboBoxItem)?.Content as string == "Corporate";
            CompanyPanel.Visibility = isCorporate ? Visibility.Visible : Visibility.Collapsed;
            if (!isCorporate)
            {
                TxtCompanyName.Clear();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(TxtFirstName.Text))
            {
                errors.Add("First name is required.");
            }

            if (string.IsNullOrWhiteSpace(TxtLastName.Text))
            {
                errors.Add("Last name is required.");
            }

            if (string.IsNullOrWhiteSpace(TxtEmail.Text))
            {
                errors.Add("Email is required.");
            }
            else if (!IsValidEmail(TxtEmail.Text.Trim()))
            {
                errors.Add("Enter a valid email address.");
            }

            if (CompanyPanel.Visibility == Visibility.Visible && string.IsNullOrWhiteSpace(TxtCompanyName.Text))
            {
                errors.Add("Company name is required for corporate records.");
            }

            if (errors.Count > 0)
            {
                TxtValidationSummary.Text = string.Join(Environment.NewLine, errors);
                return;
            }

            TxtValidationSummary.Text = "";

            var recordType = (CmbRecordType.SelectedItem as ComboBoxItem)?.Content as string ?? "";
            _records.Add(new CustomerRecord
            {
                FirstName = TxtFirstName.Text.Trim(),
                LastName = TxtLastName.Text.Trim(),
                Email = TxtEmail.Text.Trim(),
                RecordType = recordType,
            });

            TxtFirstName.Clear();
            TxtLastName.Clear();
            TxtEmail.Clear();
            TxtCompanyName.Clear();
            TxtFirstName.Focus();
        }

        private static bool IsValidEmail(string email)
        {
            var atIndex = email.IndexOf('@');
            var dotIndex = email.LastIndexOf('.');
            return atIndex > 0 && dotIndex > atIndex + 1 && dotIndex < email.Length - 1;
        }
    }
}
