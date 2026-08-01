using System;
using System.Windows.Forms;

namespace WinFormsApp
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
            cmbRecordType.SelectedIndex = 0;
        }

        private void cmbRecordType_SelectedIndexChanged(object sender, EventArgs e)
        {
            panel1.Visible = cmbRecordType.SelectedItem as string == "Corporate";
            if (!panel1.Visible)
            {
                txtCompanyName.Clear();
                errorProvider1.SetError(txtCompanyName, string.Empty);
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (!ValidateForm())
            {
                return;
            }

            dgvRecords.Rows.Add(
                txtFirstName.Text.Trim(),
                txtLastName.Text.Trim(),
                txtEmail.Text.Trim(),
                cmbRecordType.SelectedItem);

            txtFirstName.Clear();
            txtLastName.Clear();
            txtEmail.Clear();
            txtCompanyName.Clear();
            txtFirstName.Focus();
        }

        private bool ValidateForm()
        {
            var isValid = true;

            isValid &= SetRequiredFieldError(txtFirstName, "First name is required.");
            isValid &= SetRequiredFieldError(txtLastName, "Last name is required.");
            isValid &= SetRequiredFieldError(txtEmail, "Email is required.");

            if (isValid && !IsValidEmail(txtEmail.Text.Trim()))
            {
                errorProvider1.SetError(txtEmail, "Enter a valid email address.");
                isValid = false;
            }

            if (panel1.Visible)
            {
                isValid &= SetRequiredFieldError(txtCompanyName, "Company name is required for corporate records.");
            }

            return isValid;
        }

        private bool SetRequiredFieldError(TextBox textBox, string message)
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                errorProvider1.SetError(textBox, message);
                return false;
            }

            errorProvider1.SetError(textBox, string.Empty);
            return true;
        }

        private static bool IsValidEmail(string email)
        {
            var atIndex = email.IndexOf('@');
            var dotIndex = email.LastIndexOf('.');
            return atIndex > 0 && dotIndex > atIndex + 1 && dotIndex < email.Length - 1;
        }
    }
}
