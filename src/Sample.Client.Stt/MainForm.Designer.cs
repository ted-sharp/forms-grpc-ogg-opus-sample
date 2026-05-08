namespace Sample.Client.Stt
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.GroupBox grpEngine;
        private System.Windows.Forms.RadioButton rbMoonshine;
        private System.Windows.Forms.RadioButton rbWhisper;
        private System.Windows.Forms.RadioButton rbAzure;
        private System.Windows.Forms.Button btnTranscribe;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.TextBox txtResult;

        private void InitializeComponent()
        {
            this.grpEngine = new System.Windows.Forms.GroupBox();
            this.rbMoonshine = new System.Windows.Forms.RadioButton();
            this.rbWhisper = new System.Windows.Forms.RadioButton();
            this.rbAzure = new System.Windows.Forms.RadioButton();
            this.btnTranscribe = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.txtResult = new System.Windows.Forms.TextBox();
            this.grpEngine.SuspendLayout();
            this.SuspendLayout();
            //
            // grpEngine
            //
            this.grpEngine.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.grpEngine.Controls.Add(this.rbMoonshine);
            this.grpEngine.Controls.Add(this.rbWhisper);
            this.grpEngine.Controls.Add(this.rbAzure);
            this.grpEngine.Location = new System.Drawing.Point(12, 12);
            this.grpEngine.Name = "grpEngine";
            this.grpEngine.Size = new System.Drawing.Size(576, 60);
            this.grpEngine.TabIndex = 0;
            this.grpEngine.TabStop = false;
            this.grpEngine.Text = "STT エンジン";
            //
            // rbMoonshine
            //
            this.rbMoonshine.AutoSize = true;
            this.rbMoonshine.Checked = true;
            this.rbMoonshine.Location = new System.Drawing.Point(12, 25);
            this.rbMoonshine.Name = "rbMoonshine";
            this.rbMoonshine.Size = new System.Drawing.Size(170, 19);
            this.rbMoonshine.TabIndex = 0;
            this.rbMoonshine.TabStop = true;
            this.rbMoonshine.Text = "Moonshine ja base (軽量)";
            this.rbMoonshine.UseVisualStyleBackColor = true;
            //
            // rbWhisper
            //
            this.rbWhisper.AutoSize = true;
            this.rbWhisper.Location = new System.Drawing.Point(195, 25);
            this.rbWhisper.Name = "rbWhisper";
            this.rbWhisper.Size = new System.Drawing.Size(195, 19);
            this.rbWhisper.TabIndex = 1;
            this.rbWhisper.Text = "Whisper large-v3 (高精度)";
            this.rbWhisper.UseVisualStyleBackColor = true;
            //
            // rbAzure
            //
            this.rbAzure.AutoSize = true;
            this.rbAzure.Location = new System.Drawing.Point(403, 25);
            this.rbAzure.Name = "rbAzure";
            this.rbAzure.Size = new System.Drawing.Size(160, 19);
            this.rbAzure.TabIndex = 2;
            this.rbAzure.Text = "Azure Speech (クラウド)";
            this.rbAzure.UseVisualStyleBackColor = true;
            //
            // btnTranscribe
            //
            this.btnTranscribe.Location = new System.Drawing.Point(12, 84);
            this.btnTranscribe.Name = "btnTranscribe";
            this.btnTranscribe.Size = new System.Drawing.Size(140, 32);
            this.btnTranscribe.TabIndex = 1;
            this.btnTranscribe.Text = "文字起こし開始";
            this.btnTranscribe.UseVisualStyleBackColor = true;
            this.btnTranscribe.Click += new System.EventHandler(this.btnTranscribe_Click);
            //
            // btnCancel
            //
            this.btnCancel.Enabled = false;
            this.btnCancel.Location = new System.Drawing.Point(158, 84);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 32);
            this.btnCancel.TabIndex = 2;
            this.btnCancel.Text = "キャンセル";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // lblStatus
            //
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Location = new System.Drawing.Point(12, 124);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(576, 20);
            this.lblStatus.TabIndex = 3;
            this.lblStatus.Text = "状態: 待機中";
            //
            // progressBar
            //
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar.Location = new System.Drawing.Point(12, 148);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(576, 18);
            this.progressBar.TabIndex = 4;
            this.progressBar.Visible = false;
            //
            // txtResult
            //
            this.txtResult.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.txtResult.Location = new System.Drawing.Point(12, 176);
            this.txtResult.Multiline = true;
            this.txtResult.Name = "txtResult";
            this.txtResult.ReadOnly = true;
            this.txtResult.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtResult.Size = new System.Drawing.Size(576, 256);
            this.txtResult.TabIndex = 5;
            this.txtResult.WordWrap = true;
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(600, 444);
            this.Controls.Add(this.txtResult);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnTranscribe);
            this.Controls.Add(this.grpEngine);
            this.MinimumSize = new System.Drawing.Size(500, 344);
            this.Name = "MainForm";
            this.Text = "Sample.Client.Stt (STT)";
            this.grpEngine.ResumeLayout(false);
            this.grpEngine.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
