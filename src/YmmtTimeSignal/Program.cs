using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace YmmtTimeSignal
{
    internal static class Program
    {
        private const string AppGuid = "YMMT_TIME_SIGNAL_SINGLE_INSTANCE_MUTEX_GUID_20261026";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            // Self-test mode for automated verification without launching UI
            if (args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                RunSelfTest();
                return;
            }

            try
            {
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    SetProcessDPIAware();
                }
            }
            catch
            {
            }

            using (var mutex = new Mutex(false, @"Global\" + AppGuid, out bool createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "夢限大みゅーたいぷ 時報ツール は既にタスクトレイで起動しています。",
                        "時報ツール",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplicationContext());
            }
        }

        private static void RunSelfTest()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            Console.WriteLine($"[Test] Scanning baseDir: {baseDir}");

            var scanner = new VoiceScanner();
            var voiceSets = scanner.Scan(baseDir);

            int installedCount = 0;
            foreach (var vs in voiceSets)
            {
                Console.WriteLine($"[Test] Character: {vs.Definition.DisplayName} ({vs.Definition.Prefix}), Installed: {vs.IsInstalled}, VoiceCount: {vs.HourlyVoices.Count}");
                if (vs.IsInstalled)
                {
                    installedCount++;
                    // Test WAV normalization and volume scaling for hour 0
                    if (vs.HourlyVoices.TryGetValue(0, out string wavPath))
                    {
                        using (var ms = WavAudioPlayer.BuildNormalizedPcm16Stream(wavPath, 80))
                        {
                            if (ms != null && ms.Length > 44)
                            {
                                Console.WriteLine($"  -> Hour 0 WAV successfully normalized: {Path.GetFileName(wavPath)}, StreamLength: {ms.Length} bytes");
                            }
                            else
                            {
                                Console.WriteLine($"  -> ERROR: Failed to normalize {wavPath}");
                                Environment.Exit(1);
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"[Test] Complete. Installed characters count: {installedCount}");
            if (installedCount < 5)
            {
                Console.WriteLine("[Test] Warning: Not all 5 characters are found in current directory.");
            }
            Environment.Exit(0);
        }
    }
}
