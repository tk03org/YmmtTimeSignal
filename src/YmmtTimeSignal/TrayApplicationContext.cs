using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YmmtTimeSignal
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly Timer _clockTimer;
        private readonly VoiceScanner _scanner;
        private readonly WavAudioPlayer _player;
        private readonly string _baseDir;
        private readonly string _settingsPath;
        private readonly AppSettings _settings;
        private readonly Random _random = new Random();

        private List<CharacterVoiceSet> _voiceSets;
        private long _lastPlayedSlotKey = -1;
        private int _currentTrackingHour = -1;
        private readonly HashSet<string> _playedInCurrentHour = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _lastPlayedPrefix = null;
        private bool _preventMenuClose = false;

        public TrayApplicationContext()
        {
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _settingsPath = Path.Combine(_baseDir, "settings.json");
            _settings = AppSettings.Load(_settingsPath);

            _scanner = new VoiceScanner();
            _player = new WavAudioPlayer();

            _voiceSets = _scanner.Scan(_baseDir);

            // Initialize default character selection on first launch
            if (!_settings.IsInitialized)
            {
                _settings.SelectedCharacters = _voiceSets
                    .Where(v => v.IsInstalled)
                    .Select(v => v.Definition.Prefix)
                    .ToList();
                _settings.IsInitialized = true;
                _settings.Save(_settingsPath);
            }

            _contextMenu = new ContextMenuStrip();
            _contextMenu.Closing += ContextMenu_Closing;

            _trayIcon = new NotifyIcon
            {
                Visible = true,
                ContextMenuStrip = _contextMenu
            };
            _trayIcon.DoubleClick += (s, e) => PlayTestSound();

            UpdateTrayIcon();
            RebuildContextMenu();

            PlayStartupSound();

            _clockTimer = new Timer
            {
                Interval = 1000
            };
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();
        }

        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            var now = DateTime.Now;

            // Check if any one-time schedule rule has expired
            CheckAndExpireRules(now);

            int interval = _settings.IntervalMinutes > 0 ? _settings.IntervalMinutes : 60;
            bool isTargetMinute = (interval == 60) ? (now.Minute == 0) : (now.Minute % interval == 0);

            if (isTargetMinute && now.Second <= 5)
            {
                long slotKey = ((long)now.Year * 100000000L)
                             + ((long)now.Month * 1000000L)
                             + ((long)now.Day * 10000L)
                             + ((long)now.Hour * 100L)
                             + now.Minute;

                if (_lastPlayedSlotKey != slotKey)
                {
                    _lastPlayedSlotKey = slotKey;
                    bool inQuietHours = IsCurrentlyInQuietHours(now, out _);
                    if (!_settings.IsMuted && !inQuietHours)
                    {
                        PlayHourlyChime(now.Hour);
                    }
                }
            }
        }

        public bool IsCurrentlyInQuietHours(DateTime now, out string activeSummary)
        {
            activeSummary = null;
            if (_settings.QuietRules != null)
            {
                for (int i = 0; i < _settings.QuietRules.Count; i++)
                {
                    var rule = _settings.QuietRules[i];
                    if (rule.IsMutedAt(now))
                    {
                        activeSummary = rule.GetSummaryText(i + 1);
                        return true;
                    }
                }
            }
            return false;
        }

        private void CheckAndExpireRules(DateTime now)
        {
            if (_settings.QuietRules == null) return;
            bool changed = false;

            for (int i = 0; i < _settings.QuietRules.Count; i++)
            {
                var rule = _settings.QuietRules[i];
                if (rule.HasExpired(now))
                {
                    rule.Enabled = false;
                    changed = true;
                }
            }

            if (changed)
            {
                _settings.Save(_settingsPath);
                RebuildContextMenu();
                UpdateTrayIcon();
            }
        }

        private void PlayHourlyChime(int hour)
        {
            var chosen = SelectNextCharacterForHour(hour);
            if (chosen == null) return;

            var soundFile = chosen.HourlyVoices[hour];
            _player.Play(soundFile, _settings.Volume);
        }

        public CharacterVoiceSet SelectNextCharacterForHour(int hour)
        {
            if (_currentTrackingHour != hour)
            {
                _currentTrackingHour = hour;
                _playedInCurrentHour.Clear();
            }

            var activeCandidates = _voiceSets
                .Where(v => v.IsInstalled &&
                            _settings.SelectedCharacters.Contains(v.Definition.Prefix) &&
                            v.HourlyVoices.ContainsKey(hour))
                .ToList();

            if (activeCandidates.Count == 0) return null;

            var unplayedCandidates = activeCandidates
                .Where(v => !_playedInCurrentHour.Contains(v.Definition.Prefix))
                .ToList();

            if (unplayedCandidates.Count == 0)
            {
                // All selected characters have played once in this hour; start a new cycle
                _playedInCurrentHour.Clear();

                if (activeCandidates.Count >= 2 && !string.IsNullOrEmpty(_lastPlayedPrefix))
                {
                    // Avoid playing the exact same character back-to-back across cycle boundaries
                    unplayedCandidates = activeCandidates
                        .Where(v => !v.Definition.Prefix.Equals(_lastPlayedPrefix, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (unplayedCandidates.Count == 0)
                {
                    unplayedCandidates = activeCandidates;
                }
            }

            var chosen = unplayedCandidates[_random.Next(unplayedCandidates.Count)];
            _playedInCurrentHour.Add(chosen.Definition.Prefix);
            _lastPlayedPrefix = chosen.Definition.Prefix;
            return chosen;
        }

        private void PlayTestSound()
        {
            int hour = DateTime.Now.Hour;
            var chosen = SelectNextCharacterForHour(hour);

            // If no selected character has this hour, try any installed character that has this hour
            if (chosen == null)
            {
                var fallbackCandidates = _voiceSets
                    .Where(v => v.IsInstalled && v.HourlyVoices.ContainsKey(hour))
                    .ToList();
                if (fallbackCandidates.Count > 0)
                {
                    chosen = fallbackCandidates[_random.Next(fallbackCandidates.Count)];
                }
            }

            if (chosen == null) return;

            var soundFile = chosen.HourlyVoices[hour];
            _player.Play(soundFile, _settings.Volume);
        }

        private void PlayStartupSound()
        {
            if (_settings.IsMuted) return;

            var candidates = _voiceSets
                .Where(v => v.IsInstalled &&
                            _settings.SelectedCharacters.Contains(v.Definition.Prefix) &&
                            !string.IsNullOrEmpty(v.StartupVoice))
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = _voiceSets
                    .Where(v => v.IsInstalled && !string.IsNullOrEmpty(v.StartupVoice))
                    .ToList();
            }

            if (candidates.Count > 0)
            {
                var chosen = candidates[_random.Next(candidates.Count)];
                _player.Play(chosen.StartupVoice, _settings.Volume);
            }
        }

        private void ContextMenu_Closing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            if (_preventMenuClose && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
            {
                e.Cancel = true;
                _preventMenuClose = false;
            }
        }

        private void RebuildContextMenu()
        {
            _contextMenu.Items.Clear();

            // Header title
            var titleItem = new ToolStripMenuItem("YmmtTimeSignal v1.1.2")
            {
                Enabled = false,
                Font = new Font(SystemFonts.MenuFont, FontStyle.Bold)
            };
            _contextMenu.Items.Add(titleItem);
            _contextMenu.Items.Add(new ToolStripSeparator());

            // Character checkboxes
            foreach (var charSet in _voiceSets)
            {
                var def = charSet.Definition;
                var item = new ToolStripMenuItem();

                if (charSet.IsInstalled)
                {
                    item.Text = def.DisplayName;
                    item.Checked = _settings.SelectedCharacters.Contains(def.Prefix);
                    item.Click += (s, e) =>
                    {
                        _preventMenuClose = true;
                        if (_settings.SelectedCharacters.Contains(def.Prefix))
                        {
                            _settings.SelectedCharacters.Remove(def.Prefix);
                            item.Checked = false;
                        }
                        else
                        {
                            _settings.SelectedCharacters.Add(def.Prefix);
                            item.Checked = true;
                        }
                        _settings.Save(_settingsPath);
                    };
                }
                else
                {
                    item.Text = $"{def.DisplayName} (未導入)";
                    item.Enabled = false;
                    item.Checked = false;
                }

                _contextMenu.Items.Add(item);
            }

            // Reload voice folders item
            var reloadItem = new ToolStripMenuItem("🔄 音源フォルダを再読み込み");
            reloadItem.Click += (s, e) =>
            {
                _voiceSets = _scanner.Scan(_baseDir);
                RebuildContextMenu();
            };
            _contextMenu.Items.Add(reloadItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // Interval submenu
            string currentIntervalLabel = _settings.IntervalMinutes == 60
                ? "毎時0分"
                : $"{_settings.IntervalMinutes}分おき";
            var intervalMenu = new ToolStripMenuItem($"🕒 再生間隔 (現在: {currentIntervalLabel})");
            var intervalOptions = new (int Minutes, string Label)[]
            {
                (60, "60分おき (毎時0分)"),
                (30, "30分おき (0, 30分)"),
                (20, "20分おき (0, 20, 40分)"),
                (15, "15分おき (0, 15, 30, 45分)"),
                (10, "10分おき (0, 10, 20, 30, 40, 50分)")
            };
            foreach (var opt in intervalOptions)
            {
                var intItem = new ToolStripMenuItem(opt.Label)
                {
                    Checked = (_settings.IntervalMinutes == opt.Minutes)
                };
                int minutesVal = opt.Minutes;
                intItem.Click += (s, e) =>
                {
                    _settings.IntervalMinutes = minutesVal;
                    _settings.Save(_settingsPath);
                    RebuildContextMenu();
                };
                intervalMenu.DropDownItems.Add(intItem);
            }
            _contextMenu.Items.Add(intervalMenu);

            // Volume submenu
            var volumeMenu = new ToolStripMenuItem($"🔊 音量 (現在: {_settings.Volume}%)");
            int[] volumeLevels = { 200, 150, 120, 100, 80, 60, 40, 20 };
            foreach (var vol in volumeLevels)
            {
                string volText = vol >= 150 ? $"{vol}% (ソフトリミッター付)" : $"{vol}%";
                var volItem = new ToolStripMenuItem(volText)
                {
                    Checked = (_settings.Volume == vol)
                };
                volItem.Click += (s, e) =>
                {
                    _settings.Volume = vol;
                    _settings.Save(_settingsPath);
                    RebuildContextMenu();
                    PlayTestSound();
                };
                volumeMenu.DropDownItems.Add(volItem);
            }
            _contextMenu.Items.Add(volumeMenu);

            // Quiet Schedule submenu
            int enabledQuietCount = _settings.QuietRules != null ? _settings.QuietRules.Count(r => r.Enabled) : 0;
            var quietMenu = new ToolStripMenuItem($"🌙 指定時間停止 (現在: {enabledQuietCount}件有効)");
            if (_settings.QuietRules != null)
            {
                for (int i = 0; i < _settings.QuietRules.Count; i++)
                {
                    int ruleIdx = i;
                    var rule = _settings.QuietRules[i];
                    var ruleItem = new ToolStripMenuItem(rule.GetSummaryText(ruleIdx + 1))
                    {
                        Checked = rule.Enabled
                    };
                    ruleItem.Click += (s, e) =>
                    {
                        rule.Enabled = !rule.Enabled;
                        if (rule.Enabled)
                        {
                            rule.Activate(DateTime.Now);
                        }
                        _settings.Save(_settingsPath);
                        RebuildContextMenu();
                        UpdateTrayIcon();
                    };
                    quietMenu.DropDownItems.Add(ruleItem);
                }
            }
            quietMenu.DropDownItems.Add(new ToolStripSeparator());
            var openSettingsItem = new ToolStripMenuItem("⚙ 時間帯の設定を開く...");
            openSettingsItem.Click += (s, e) => OpenQuietScheduleDialog();
            quietMenu.DropDownItems.Add(openSettingsItem);
            _contextMenu.Items.Add(quietMenu);

            // Mute toggle
            var muteItem = new ToolStripMenuItem("🔇 一時ミュート")
            {
                Checked = _settings.IsMuted
            };
            muteItem.Click += (s, e) =>
            {
                _settings.IsMuted = !_settings.IsMuted;
                _settings.Save(_settingsPath);
                UpdateTrayIcon();
                RebuildContextMenu();
            };
            _contextMenu.Items.Add(muteItem);

            // Test play
            var testItem = new ToolStripMenuItem("▶ 今すぐテスト再生");
            testItem.Click += (s, e) => PlayTestSound();
            _contextMenu.Items.Add(testItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // Auto startup toggle
            bool isAutoStart = StartupManager.IsRegistered();
            var startupItem = new ToolStripMenuItem("Windows起動時に自動起動")
            {
                Checked = isAutoStart
            };
            startupItem.Click += (s, e) =>
            {
                bool newState = !startupItem.Checked;
                string exePath = Application.ExecutablePath;
                StartupManager.SetRegistered(newState, exePath);
                startupItem.Checked = newState;
            };
            _contextMenu.Items.Add(startupItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // Exit
            var exitItem = new ToolStripMenuItem("✖ 終了");
            exitItem.Click += (s, e) => ExitApp();
            _contextMenu.Items.Add(exitItem);
        }

        private void OpenQuietScheduleDialog()
        {
            using (var dlg = new QuietScheduleDialog(_settings.QuietRules))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _settings.QuietRules = dlg.ResultRules;
                    _settings.Save(_settingsPath);
                    RebuildContextMenu();
                    UpdateTrayIcon();
                }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private void UpdateTrayIcon()
        {
            bool inQuiet = IsCurrentlyInQuietHours(DateTime.Now, out string quietSummary);

            if (_settings.IsMuted)
            {
                _trayIcon.Text = "夢限大みゅーたいぷ 時報ツール (一時ミュート中)";
            }
            else if (inQuiet)
            {
                string text = $"夢限大みゅーたいぷ 時報ツール (指定時間停止中: {quietSummary})";
                if (text.Length > 63) text = text.Substring(0, 60) + "...";
                _trayIcon.Text = text;
            }
            else
            {
                _trayIcon.Text = "夢限大みゅーたいぷ 時報ツール";
            }

            // Draw a 32x32 UFO or Moon icon with GDI+
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                if (inQuiet)
                {
                    DrawMoon(g);
                }
                else
                {
                    DrawUfo(g);
                }

                if (_settings.IsMuted)
                {
                    DrawMuteSlash(g);
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (var tempIcon = Icon.FromHandle(hIcon))
                    {
                        var oldIcon = _trayIcon.Icon;
                        _trayIcon.Icon = (Icon)tempIcon.Clone();
                        oldIcon?.Dispose();
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        private void DrawUfo(Graphics g)
        {
            // Dome outer: cx=16, cy=13, rx=7.5, ry=6.5
            using (var bDomeOuter = new SolidBrush(Color.FromArgb(192, 132, 252)))
            {
                g.FillEllipse(bDomeOuter, 8.5f, 6.5f, 15f, 13f);
            }

            // Dome inner: cx=16, cy=13, rx=6.5, ry=5.5
            using (var bDomeInner = new SolidBrush(Color.FromArgb(168, 85, 247)))
            {
                g.FillEllipse(bDomeInner, 9.5f, 7.5f, 13f, 11f);
            }

            // Saucer body: cx=16, cy=18, rx=14.5, ry=5.8
            using (var bSaucer = new SolidBrush(Color.FromArgb(126, 34, 206)))
            {
                g.FillEllipse(bSaucer, 1.5f, 12.2f, 29f, 11.6f);
            }

            // Saucer rim highlight: cx=16, cy=17.2, rx=12.5, ry=2.2
            using (var bRim = new SolidBrush(Color.FromArgb(147, 51, 234)))
            {
                g.FillEllipse(bRim, 3.5f, 15f, 25f, 4.4f);
            }
        }

        private void DrawMoon(Graphics g)
        {
            // 案C: True outer circle with ~70% thickness crescent
            float cx1 = 16f;
            float cy1 = 16f;
            float r1 = 12f;

            float cx2 = 26.5f;
            float cy2 = 16f;
            float r2 = 11f;

            float d = cx2 - cx1;
            float x = (r1 * r1 - r2 * r2 + d * d) / (2f * d);
            float y = (float)Math.Sqrt(Math.Max(0f, r1 * r1 - x * x));

            float angle1Top = (float)(Math.Atan2(-y, x) * 180.0 / Math.PI);
            float angle1Bottom = (float)(Math.Atan2(y, x) * 180.0 / Math.PI);

            float x2 = x - d;
            float angle2Top = (float)(Math.Atan2(-y, x2) * 180.0 / Math.PI);
            float angle2Bottom = (float)(Math.Atan2(y, x2) * 180.0 / Math.PI);

            float sweep1 = 360f - (angle1Bottom - angle1Top);
            float a2TopNorm = angle2Top < 0 ? angle2Top + 360f : angle2Top;
            float sweep2 = -(a2TopNorm - angle2Bottom);

            using (var path = new GraphicsPath())
            {
                path.AddArc(cx1 - r1, cy1 - r1, r1 * 2, r1 * 2, angle1Bottom, sweep1);
                path.AddArc(cx2 - r2, cy2 - r2, r2 * 2, r2 * 2, angle2Top, sweep2);
                path.CloseFigure();

                using (var brush = new SolidBrush(Color.FromArgb(250, 204, 21))) // #FACC15
                {
                    g.FillPath(brush, path);
                }
            }
        }

        private void DrawMuteSlash(Graphics g)
        {
            using (var shadowPen = new Pen(Color.FromArgb(160, 0, 0, 0), 4.5f))
            {
                g.DrawLine(shadowPen, 4.5f, 4.5f, 27.5f, 27.5f);
            }
            using (var pen = new Pen(Color.FromArgb(239, 68, 68), 3f))
            {
                g.DrawLine(pen, 4.5f, 4.5f, 27.5f, 27.5f);
            }
        }

        private void ExitApp()
        {
            _clockTimer.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _player.Dispose();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _clockTimer?.Dispose();
                _trayIcon?.Dispose();
                _player?.Dispose();
                _contextMenu?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
