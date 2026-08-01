namespace WinFormsApp
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.Label lblAdi;
        private System.Windows.Forms.TextBox txtAdi;
        private System.Windows.Forms.Label lblSoyad;
        private System.Windows.Forms.TextBox txtSoyad;
        private System.Windows.Forms.Label lblEmail;
        private System.Windows.Forms.TextBox txtEmail;
        private System.Windows.Forms.Label lblKayitTuru;
        private System.Windows.Forms.ComboBox cmbKayitTuru;

        // Kasıtlı olarak designer'ın verdiği varsayılan ad korunuyor (panel2 değil "panel1"):
        // gerçek projelerde sık rastlanan, hiç yeniden adlandırılmamış container'ları simüle eder.
        // UIA AutomationId bu yüzden "panel1" gibi anlamsız bir değer taşıyacak.
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label lblSirketAdi;
        private System.Windows.Forms.TextBox txtSirketAdi;

        private System.Windows.Forms.Button btnKaydet;
        private System.Windows.Forms.DataGridView dgvKayitlar;
        private System.Windows.Forms.DataGridViewTextBoxColumn colAdi;
        private System.Windows.Forms.DataGridViewTextBoxColumn colSoyad;
        private System.Windows.Forms.DataGridViewTextBoxColumn colEmail;
        private System.Windows.Forms.DataGridViewTextBoxColumn colTur;
        private System.Windows.Forms.ErrorProvider errorProvider1;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            this.lblAdi = new System.Windows.Forms.Label();
            this.txtAdi = new System.Windows.Forms.TextBox();
            this.lblSoyad = new System.Windows.Forms.Label();
            this.txtSoyad = new System.Windows.Forms.TextBox();
            this.lblEmail = new System.Windows.Forms.Label();
            this.txtEmail = new System.Windows.Forms.TextBox();
            this.lblKayitTuru = new System.Windows.Forms.Label();
            this.cmbKayitTuru = new System.Windows.Forms.ComboBox();
            this.panel1 = new System.Windows.Forms.Panel();
            this.lblSirketAdi = new System.Windows.Forms.Label();
            this.txtSirketAdi = new System.Windows.Forms.TextBox();
            this.btnKaydet = new System.Windows.Forms.Button();
            this.dgvKayitlar = new System.Windows.Forms.DataGridView();
            this.colAdi = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colSoyad = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colEmail = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colTur = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.errorProvider1 = new System.Windows.Forms.ErrorProvider(this.components);

            ((System.ComponentModel.ISupportInitialize)(this.dgvKayitlar)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider1)).BeginInit();
            this.panel1.SuspendLayout();
            this.SuspendLayout();

            // lblAdi
            this.lblAdi.AutoSize = true;
            this.lblAdi.Location = new System.Drawing.Point(12, 15);
            this.lblAdi.Name = "lblAdi";
            this.lblAdi.Size = new System.Drawing.Size(24, 15);
            this.lblAdi.Text = "Ad:";

            // txtAdi
            this.txtAdi.Location = new System.Drawing.Point(112, 12);
            this.txtAdi.Name = "txtAdi";
            this.txtAdi.Size = new System.Drawing.Size(200, 23);

            // lblSoyad
            this.lblSoyad.AutoSize = true;
            this.lblSoyad.Location = new System.Drawing.Point(12, 44);
            this.lblSoyad.Name = "lblSoyad";
            this.lblSoyad.Size = new System.Drawing.Size(45, 15);
            this.lblSoyad.Text = "Soyad:";

            // txtSoyad
            this.txtSoyad.Location = new System.Drawing.Point(112, 41);
            this.txtSoyad.Name = "txtSoyad";
            this.txtSoyad.Size = new System.Drawing.Size(200, 23);

            // lblEmail
            this.lblEmail.AutoSize = true;
            this.lblEmail.Location = new System.Drawing.Point(12, 73);
            this.lblEmail.Name = "lblEmail";
            this.lblEmail.Size = new System.Drawing.Size(40, 15);
            this.lblEmail.Text = "Email:";

            // txtEmail
            this.txtEmail.Location = new System.Drawing.Point(112, 70);
            this.txtEmail.Name = "txtEmail";
            this.txtEmail.Size = new System.Drawing.Size(200, 23);

            // lblKayitTuru
            this.lblKayitTuru.AutoSize = true;
            this.lblKayitTuru.Location = new System.Drawing.Point(12, 102);
            this.lblKayitTuru.Name = "lblKayitTuru";
            this.lblKayitTuru.Size = new System.Drawing.Size(66, 15);
            this.lblKayitTuru.Text = "Kayıt Türü:";

            // cmbKayitTuru
            this.cmbKayitTuru.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbKayitTuru.Location = new System.Drawing.Point(112, 99);
            this.cmbKayitTuru.Name = "cmbKayitTuru";
            this.cmbKayitTuru.Size = new System.Drawing.Size(200, 23);
            this.cmbKayitTuru.Items.AddRange(new object[] { "Bireysel", "Kurumsal" });
            this.cmbKayitTuru.SelectedIndexChanged += new System.EventHandler(this.cmbKayitTuru_SelectedIndexChanged);

            // panel1
            this.panel1.Controls.Add(this.txtSirketAdi);
            this.panel1.Controls.Add(this.lblSirketAdi);
            this.panel1.Location = new System.Drawing.Point(12, 131);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(300, 34);
            this.panel1.Visible = false;

            // lblSirketAdi
            this.lblSirketAdi.AutoSize = true;
            this.lblSirketAdi.Location = new System.Drawing.Point(0, 8);
            this.lblSirketAdi.Name = "lblSirketAdi";
            this.lblSirketAdi.Size = new System.Drawing.Size(70, 15);
            this.lblSirketAdi.Text = "Şirket Adı:";

            // txtSirketAdi
            this.txtSirketAdi.Location = new System.Drawing.Point(100, 5);
            this.txtSirketAdi.Name = "txtSirketAdi";
            this.txtSirketAdi.Size = new System.Drawing.Size(200, 23);

            // btnKaydet
            this.btnKaydet.Location = new System.Drawing.Point(112, 178);
            this.btnKaydet.Name = "btnKaydet";
            this.btnKaydet.Size = new System.Drawing.Size(100, 30);
            this.btnKaydet.Text = "Kaydet";
            this.btnKaydet.UseVisualStyleBackColor = true;
            this.btnKaydet.Click += new System.EventHandler(this.btnKaydet_Click);

            // colAdi
            this.colAdi.HeaderText = "Ad";
            this.colAdi.Name = "colAdi";
            this.colAdi.ReadOnly = true;

            // colSoyad
            this.colSoyad.HeaderText = "Soyad";
            this.colSoyad.Name = "colSoyad";
            this.colSoyad.ReadOnly = true;

            // colEmail
            this.colEmail.HeaderText = "Email";
            this.colEmail.Name = "colEmail";
            this.colEmail.ReadOnly = true;
            this.colEmail.Width = 160;

            // colTur
            this.colTur.HeaderText = "Kayıt Türü";
            this.colTur.Name = "colTur";
            this.colTur.ReadOnly = true;

            // dgvKayitlar
            this.dgvKayitlar.AllowUserToAddRows = false;
            this.dgvKayitlar.AllowUserToDeleteRows = false;
            this.dgvKayitlar.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvKayitlar.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
                this.colAdi, this.colSoyad, this.colEmail, this.colTur });
            this.dgvKayitlar.Location = new System.Drawing.Point(12, 220);
            this.dgvKayitlar.Name = "dgvKayitlar";
            this.dgvKayitlar.ReadOnly = true;
            this.dgvKayitlar.RowHeadersWidth = 25;
            this.dgvKayitlar.Size = new System.Drawing.Size(400, 150);

            // MainForm
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(430, 390);
            this.Controls.Add(this.dgvKayitlar);
            this.Controls.Add(this.btnKaydet);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.cmbKayitTuru);
            this.Controls.Add(this.lblKayitTuru);
            this.Controls.Add(this.txtEmail);
            this.Controls.Add(this.lblEmail);
            this.Controls.Add(this.txtSoyad);
            this.Controls.Add(this.lblSoyad);
            this.Controls.Add(this.txtAdi);
            this.Controls.Add(this.lblAdi);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.Text = "Müşteri Kayıt Formu";

            ((System.ComponentModel.ISupportInitialize)(this.dgvKayitlar)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider1)).EndInit();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
