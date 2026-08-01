using System;
using System.Windows.Forms;

namespace WinFormsApp
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
            cmbKayitTuru.SelectedIndex = 0;
        }

        private void cmbKayitTuru_SelectedIndexChanged(object sender, EventArgs e)
        {
            panel1.Visible = cmbKayitTuru.SelectedItem as string == "Kurumsal";
            if (!panel1.Visible)
            {
                txtSirketAdi.Clear();
                errorProvider1.SetError(txtSirketAdi, string.Empty);
            }
        }

        private void btnKaydet_Click(object sender, EventArgs e)
        {
            if (!ValidateForm())
            {
                return;
            }

            dgvKayitlar.Rows.Add(
                txtAdi.Text.Trim(),
                txtSoyad.Text.Trim(),
                txtEmail.Text.Trim(),
                cmbKayitTuru.SelectedItem);

            txtAdi.Clear();
            txtSoyad.Clear();
            txtEmail.Clear();
            txtSirketAdi.Clear();
            txtAdi.Focus();
        }

        private bool ValidateForm()
        {
            var isValid = true;

            isValid &= SetRequiredFieldError(txtAdi, "Ad alanı zorunludur.");
            isValid &= SetRequiredFieldError(txtSoyad, "Soyad alanı zorunludur.");
            isValid &= SetRequiredFieldError(txtEmail, "Email alanı zorunludur.");

            if (isValid && !IsValidEmail(txtEmail.Text.Trim()))
            {
                errorProvider1.SetError(txtEmail, "Geçerli bir email adresi girin.");
                isValid = false;
            }

            if (panel1.Visible)
            {
                isValid &= SetRequiredFieldError(txtSirketAdi, "Kurumsal kayıt için şirket adı zorunludur.");
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
