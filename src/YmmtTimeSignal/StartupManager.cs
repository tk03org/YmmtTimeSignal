using System;
using Microsoft.Win32;

namespace YmmtTimeSignal
{
    public static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "YmmtTimeSignal";

        public static bool IsRegistered()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(AppName) as string;
                        return !string.IsNullOrEmpty(val);
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static void SetRegistered(bool enable, string exePath)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue(AppName, $"\"{exePath}\"");
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}
