using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YmmtTimeSignal
{
    public class QuietScheduleDialog : Form
    {
        private class RowControls
        {
            public CheckBox ChkEnabled;
            public DateTimePicker DtpStart;
            public ComboBox CmbStartInc;
            public Label LblTilde;
            public DateTimePicker DtpEnd;
            public ComboBox CmbEndInc;
            public CheckBox ChkPinpoint;
            public ComboBox CmbRepeat;
        }

        private readonly List<RowControls> _rows = new List<RowControls>();
        public List<QuietScheduleRule> ResultRules { get; private set; }

        public QuietScheduleDialog(List<QuietScheduleRule> currentRules)
        {
            InitializeComponent(currentRules);
        }

        private void InitializeComponent(List<QuietScheduleRule> currentRules)
        {
            Text = "指定時間停止の設定";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = SystemFonts.MessageBoxFont ?? new Font("Yu Gothic UI", 9f);

            int padX = 12;
            int padY = 12;
            int rowGap = 6;
            int scrollW = SystemInformation.VerticalScrollBarWidth;

            // Tight, exact control widths based on actual font metrics
            int dtpWidth = TextRenderer.MeasureText("00:00", Font).Width + scrollW + 6;
            int cmbIncWidth = TextRenderer.MeasureText("含まない", Font).Width + scrollW + 8;
            int cmbRepWidth = TextRenderer.MeasureText("繰り返す", Font).Width + scrollW + 8;

            int currentY = padY;
            int maxRowRight = 0;

            for (int i = 0; i < 3; i++)
            {
                var rule = (currentRules != null && i < currentRules.Count)
                    ? currentRules[i]
                    : new QuietScheduleRule(false, 0, 0, true, 0, 0, false, false, true);

                var rc = new RowControls();

                // 1. Enable CheckBox (AutoSize = true, no text -> exact checkbox square)
                rc.ChkEnabled = new CheckBox
                {
                    Text = string.Empty,
                    Checked = rule.Enabled,
                    AutoSize = true,
                    UseVisualStyleBackColor = true
                };
                Controls.Add(rc.ChkEnabled);

                // 2. Start Time (HH:mm with spin buttons)
                rc.DtpStart = new DateTimePicker
                {
                    Format = DateTimePickerFormat.Custom,
                    CustomFormat = "HH:mm",
                    ShowUpDown = true,
                    Value = new DateTime(2026, 1, 1, Math.Max(0, Math.Min(23, rule.StartHour)), Math.Max(0, Math.Min(59, rule.StartMinute)), 0),
                    Width = dtpWidth
                };
                Controls.Add(rc.DtpStart);

                // 3. Start Inclusive
                rc.CmbStartInc = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = cmbIncWidth
                };
                rc.CmbStartInc.Items.AddRange(new object[] { "含む", "含まない" });
                rc.CmbStartInc.SelectedIndex = rule.StartInclusive ? 0 : 1;
                Controls.Add(rc.CmbStartInc);

                // 4. Tilde ～
                rc.LblTilde = new Label
                {
                    Text = "～",
                    AutoSize = true
                };
                Controls.Add(rc.LblTilde);

                // 5. End Time
                rc.DtpEnd = new DateTimePicker
                {
                    Format = DateTimePickerFormat.Custom,
                    CustomFormat = "HH:mm",
                    ShowUpDown = true,
                    Value = new DateTime(2026, 1, 1, Math.Max(0, Math.Min(23, rule.EndHour)), Math.Max(0, Math.Min(59, rule.EndMinute)), 0),
                    Width = dtpWidth
                };
                Controls.Add(rc.DtpEnd);

                // 6. End Inclusive
                rc.CmbEndInc = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = cmbIncWidth
                };
                rc.CmbEndInc.Items.AddRange(new object[] { "含む", "含まない" });
                rc.CmbEndInc.SelectedIndex = rule.EndInclusive ? 0 : 1;
                Controls.Add(rc.CmbEndInc);

                // 7. Pinpoint CheckBox ("この時間のみ")
                rc.ChkPinpoint = new CheckBox
                {
                    Text = "この時間のみ",
                    Checked = rule.IsPinpoint,
                    AutoSize = true,
                    UseVisualStyleBackColor = true
                };
                Controls.Add(rc.ChkPinpoint);

                // 8. Repeat ComboBox ("繰り返す" / "1回のみ")
                rc.CmbRepeat = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Width = cmbRepWidth
                };
                rc.CmbRepeat.Items.AddRange(new object[] { "繰り返す", "1回のみ" });
                rc.CmbRepeat.SelectedIndex = rule.IsRepeat ? 0 : 1;
                Controls.Add(rc.CmbRepeat);

                // Use actual rendered Heights to align all controls on the exact same vertical center line
                int rowHeight = Math.Max(rc.DtpStart.Height, rc.CmbStartInc.Height);
                int centerY = currentY + rowHeight / 2;

                int x = padX;
                rc.ChkEnabled.Location = new Point(x, centerY - rc.ChkEnabled.PreferredSize.Height / 2);
                x += rc.ChkEnabled.PreferredSize.Width + 4;

                rc.DtpStart.Location = new Point(x, centerY - rc.DtpStart.Height / 2);
                x += rc.DtpStart.Width + 4;

                rc.CmbStartInc.Location = new Point(x, centerY - rc.CmbStartInc.Height / 2);
                x += rc.CmbStartInc.Width + 4;

                rc.LblTilde.Location = new Point(x, centerY - rc.LblTilde.PreferredSize.Height / 2);
                x += rc.LblTilde.PreferredSize.Width + 4;

                rc.DtpEnd.Location = new Point(x, centerY - rc.DtpEnd.Height / 2);
                x += rc.DtpEnd.Width + 4;

                rc.CmbEndInc.Location = new Point(x, centerY - rc.CmbEndInc.Height / 2);
                x += rc.CmbEndInc.Width + 10;

                rc.ChkPinpoint.Location = new Point(x, centerY - rc.ChkPinpoint.PreferredSize.Height / 2);
                x += rc.ChkPinpoint.PreferredSize.Width + 8;

                rc.CmbRepeat.Location = new Point(x, centerY - rc.CmbRepeat.Height / 2);
                x += rc.CmbRepeat.Width;

                if (x > maxRowRight) maxRowRight = x;

                // Wire up interactive enabling logic
                Action updateRowState = () =>
                {
                    bool enabled = rc.ChkEnabled.Checked;
                    bool isPinpoint = rc.ChkPinpoint.Checked;

                    rc.DtpStart.Enabled = enabled;
                    rc.ChkPinpoint.Enabled = enabled;
                    rc.CmbRepeat.Enabled = enabled;

                    rc.CmbStartInc.Enabled = enabled && !isPinpoint;
                    rc.LblTilde.Enabled = enabled && !isPinpoint;
                    rc.DtpEnd.Enabled = enabled && !isPinpoint;
                    rc.CmbEndInc.Enabled = enabled && !isPinpoint;
                };

                rc.ChkEnabled.CheckedChanged += (s, e) => updateRowState();
                rc.ChkPinpoint.CheckedChanged += (s, e) => updateRowState();
                updateRowState();

                _rows.Add(rc);
                currentY += rowHeight + rowGap;
            }

            int footerY = currentY + 6;

            // Standard Windows button dimensions scaled nicely to font
            int btnWidth = TextRenderer.MeasureText("キャンセル", Font).Width + 20;
            int btnHeight = Font.Height + 10;
            int btnGap = 6;

            // Note label (black text)
            var lblNote = new Label
            {
                Text = "※ 開始＝終了時刻の場合は翌日の同刻まで停止",
                ForeColor = SystemColors.ControlText,
                AutoSize = true
            };
            Controls.Add(lblNote);

            int minFooterRight = padX + lblNote.PreferredWidth + 12 + (btnWidth * 2) + btnGap;
            int contentRight = Math.Max(maxRowRight, minFooterRight);

            lblNote.Location = new Point(padX, footerY + (btnHeight - lblNote.PreferredHeight) / 2);

            // Cancel Button
            var btnCancel = new Button
            {
                Text = "キャンセル",
                DialogResult = DialogResult.Cancel,
                Size = new Size(btnWidth, btnHeight),
                Location = new Point(contentRight - btnWidth, footerY),
                UseVisualStyleBackColor = true
            };
            Controls.Add(btnCancel);

            // OK Button
            var btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Size = new Size(btnWidth, btnHeight),
                Location = new Point(btnCancel.Left - btnGap - btnWidth, footerY),
                UseVisualStyleBackColor = true
            };
            btnOk.Click += (s, e) => SaveResults(currentRules);
            Controls.Add(btnOk);

            ClientSize = new Size(contentRight + padX, footerY + btnHeight + padY);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void SaveResults(List<QuietScheduleRule> oldRules)
        {
            ResultRules = new List<QuietScheduleRule>();
            var now = DateTime.Now;

            for (int i = 0; i < _rows.Count; i++)
            {
                var rc = _rows[i];
                var oldRule = (oldRules != null && i < oldRules.Count) ? oldRules[i] : null;

                var rule = new QuietScheduleRule
                {
                    Enabled = rc.ChkEnabled.Checked,
                    StartHour = rc.DtpStart.Value.Hour,
                    StartMinute = rc.DtpStart.Value.Minute,
                    StartInclusive = (rc.CmbStartInc.SelectedIndex == 0),
                    EndHour = rc.DtpEnd.Value.Hour,
                    EndMinute = rc.DtpEnd.Value.Minute,
                    EndInclusive = (rc.CmbEndInc.SelectedIndex == 0),
                    IsPinpoint = rc.ChkPinpoint.Checked,
                    IsRepeat = (rc.CmbRepeat.SelectedIndex == 0),
                    ActivatedAtTicks = oldRule != null ? oldRule.ActivatedAtTicks : 0
                };

                if (rule.Enabled && (!rule.IsRepeat || (oldRule == null || !oldRule.Enabled)))
                {
                    rule.Activate(now);
                }

                ResultRules.Add(rule);
            }
        }
    }
}
