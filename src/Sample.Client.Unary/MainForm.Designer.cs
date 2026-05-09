namespace Sample.Client.Unary
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.Button btnRecord;
        private System.Windows.Forms.Button btnPlay;
        private System.Windows.Forms.Button btnPause;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.TrackBar tbSeek;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblTime;
        private System.Windows.Forms.CheckBox chkRemoveSilence;
        private System.Windows.Forms.Label lblVadCaption;
        private System.Windows.Forms.TrackBar tbVadAggressiveness;
        private System.Windows.Forms.Label lblVadAggressiveness;
        private Sample.Client.Unary.Ui.WaveformView waveformView;

        private void InitializeComponent()
        {
            this.btnRecord = new System.Windows.Forms.Button();
            this.btnPlay = new System.Windows.Forms.Button();
            this.btnPause = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.tbSeek = new System.Windows.Forms.TrackBar();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblTime = new System.Windows.Forms.Label();
            this.chkRemoveSilence = new System.Windows.Forms.CheckBox();
            this.lblVadCaption = new System.Windows.Forms.Label();
            this.tbVadAggressiveness = new System.Windows.Forms.TrackBar();
            this.lblVadAggressiveness = new System.Windows.Forms.Label();
            this.waveformView = new Sample.Client.Unary.Ui.WaveformView();
            ((System.ComponentModel.ISupportInitialize)(this.tbSeek)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbVadAggressiveness)).BeginInit();
            this.SuspendLayout();
            //
            // btnRecord
            //
            this.btnRecord.Location = new System.Drawing.Point(12, 12);
            this.btnRecord.Name = "btnRecord";
            this.btnRecord.Size = new System.Drawing.Size(90, 36);
            this.btnRecord.TabIndex = 0;
            this.btnRecord.Text = "● 録音";
            this.btnRecord.UseVisualStyleBackColor = true;
            this.btnRecord.Click += new System.EventHandler(this.btnRecord_Click);
            //
            // btnPlay
            //
            this.btnPlay.Location = new System.Drawing.Point(108, 12);
            this.btnPlay.Name = "btnPlay";
            this.btnPlay.Size = new System.Drawing.Size(90, 36);
            this.btnPlay.TabIndex = 1;
            this.btnPlay.Text = "▶ 再生";
            this.btnPlay.UseVisualStyleBackColor = true;
            this.btnPlay.Click += new System.EventHandler(this.btnPlay_Click);
            //
            // btnPause
            //
            this.btnPause.Location = new System.Drawing.Point(204, 12);
            this.btnPause.Name = "btnPause";
            this.btnPause.Size = new System.Drawing.Size(90, 36);
            this.btnPause.TabIndex = 2;
            this.btnPause.Text = "⏸ 一時停止";
            this.btnPause.UseVisualStyleBackColor = true;
            this.btnPause.Click += new System.EventHandler(this.btnPause_Click);
            //
            // btnStop
            //
            this.btnStop.Location = new System.Drawing.Point(300, 12);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(90, 36);
            this.btnStop.TabIndex = 3;
            this.btnStop.Text = "■ 停止";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            //
            // tbSeek
            //
            this.tbSeek.Location = new System.Drawing.Point(12, 60);
            this.tbSeek.Maximum = 1000;
            this.tbSeek.Name = "tbSeek";
            this.tbSeek.Size = new System.Drawing.Size(460, 45);
            this.tbSeek.TabIndex = 4;
            this.tbSeek.TickStyle = System.Windows.Forms.TickStyle.None;
            this.tbSeek.Scroll += new System.EventHandler(this.tbSeek_Scroll);
            this.tbSeek.MouseDown += new System.Windows.Forms.MouseEventHandler(this.tbSeek_MouseDown);
            this.tbSeek.MouseUp += new System.Windows.Forms.MouseEventHandler(this.tbSeek_MouseUp);
            //
            // lblTime
            //
            this.lblTime.AutoSize = true;
            this.lblTime.Location = new System.Drawing.Point(12, 110);
            this.lblTime.Name = "lblTime";
            this.lblTime.Size = new System.Drawing.Size(80, 18);
            this.lblTime.TabIndex = 5;
            this.lblTime.Text = "00:00 / 00:00";
            //
            // lblStatus
            //
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new System.Drawing.Point(12, 135);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(60, 18);
            this.lblStatus.TabIndex = 6;
            this.lblStatus.Text = "状態: 待機中";
            //
            // chkRemoveSilence
            //
            this.chkRemoveSilence.AutoSize = true;
            this.chkRemoveSilence.Location = new System.Drawing.Point(12, 162);
            this.chkRemoveSilence.Name = "chkRemoveSilence";
            this.chkRemoveSilence.Size = new System.Drawing.Size(150, 22);
            this.chkRemoveSilence.TabIndex = 7;
            this.chkRemoveSilence.Text = "無音をカットする (VAD)";
            this.chkRemoveSilence.UseVisualStyleBackColor = true;
            //
            // lblVadCaption
            //
            this.lblVadCaption.AutoSize = true;
            this.lblVadCaption.Location = new System.Drawing.Point(190, 165);
            this.lblVadCaption.Name = "lblVadCaption";
            this.lblVadCaption.Size = new System.Drawing.Size(40, 18);
            this.lblVadCaption.TabIndex = 8;
            this.lblVadCaption.Text = "精度:";
            //
            // tbVadAggressiveness
            //
            this.tbVadAggressiveness.Location = new System.Drawing.Point(232, 158);
            this.tbVadAggressiveness.Maximum = 3;
            this.tbVadAggressiveness.Minimum = 0;
            this.tbVadAggressiveness.Name = "tbVadAggressiveness";
            this.tbVadAggressiveness.Size = new System.Drawing.Size(160, 45);
            this.tbVadAggressiveness.TabIndex = 9;
            this.tbVadAggressiveness.TickFrequency = 1;
            this.tbVadAggressiveness.Value = 0;
            this.tbVadAggressiveness.ValueChanged += new System.EventHandler(this.tbVadAggressiveness_ValueChanged);
            //
            // lblVadAggressiveness
            //
            this.lblVadAggressiveness.AutoSize = true;
            this.lblVadAggressiveness.Location = new System.Drawing.Point(400, 165);
            this.lblVadAggressiveness.Name = "lblVadAggressiveness";
            this.lblVadAggressiveness.Size = new System.Drawing.Size(60, 18);
            this.lblVadAggressiveness.TabIndex = 10;
            this.lblVadAggressiveness.Text = "ゆるめ";
            //
            // waveformView
            //
            this.waveformView.BackColor = System.Drawing.Color.Black;
            this.waveformView.ForeColor = System.Drawing.Color.LimeGreen;
            this.waveformView.Location = new System.Drawing.Point(12, 200);
            this.waveformView.Name = "waveformView";
            this.waveformView.Size = new System.Drawing.Size(460, 90);
            this.waveformView.TabIndex = 11;
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(490, 305);
            this.Controls.Add(this.waveformView);
            this.Controls.Add(this.lblVadAggressiveness);
            this.Controls.Add(this.tbVadAggressiveness);
            this.Controls.Add(this.lblVadCaption);
            this.Controls.Add(this.chkRemoveSilence);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.lblTime);
            this.Controls.Add(this.tbSeek);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.btnPause);
            this.Controls.Add(this.btnPlay);
            this.Controls.Add(this.btnRecord);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.Text = "Sample.Client.Unary (Unary)";
            ((System.ComponentModel.ISupportInitialize)(this.tbSeek)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbVadAggressiveness)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
