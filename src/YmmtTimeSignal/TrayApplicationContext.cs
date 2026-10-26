using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
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
                    if (!_settings.IsMuted)
                    {
                        PlayHourlyChime(now.Hour);
                    }
                }
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
            var titleItem = new ToolStripMenuItem("夢限大みゅーたいぷ 時報ツール")
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

        private void UpdateTrayIcon()
        {
            _trayIcon.Text = _settings.IsMuted
                ? "夢限大みゅーたいぷ 時報ツール (ミュート中)"
                : "夢限大みゅーたいぷ 時報ツール";

            // Draw a 32x32 clock icon with GDI+
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Circle background
                Color circleColor = _settings.IsMuted ? Color.FromArgb(140, 140, 140) : Color.FromArgb(70, 130, 240);
                using (var brush = new SolidBrush(circleColor))
                {
                    g.FillEllipse(brush, 2, 2, 27, 27);
                }

                using (var pen = new Pen(Color.White, 2f))
                {
                    g.DrawEllipse(pen, 2, 2, 27, 27);
                    // Clock hands
                    g.DrawLine(pen, 15.5f, 15.5f, 15.5f, 7f);   // 12 o'clock
                    g.DrawLine(pen, 15.5f, 15.5f, 22f, 15.5f);  // 3 o'clock
                }

                // If muted, draw red slash
                if (_settings.IsMuted)
                {
                    using (var pen = new Pen(Color.Red, 3f))
                    {
                        g.DrawLine(pen, 4, 4, 27, 27);
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    _trayIcon.Icon = Icon.FromHandle(hIcon);
                }
                finally
                {
                    // DestroyIcon API could be called, but simple disposal is fine
                }
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
