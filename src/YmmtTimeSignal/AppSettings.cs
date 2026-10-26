using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace YmmtTimeSignal
{
    [DataContract]
    public class AppSettings
    {
        [DataMember(Name = "isInitialized")]
        public bool IsInitialized { get; set; } = false;

        [DataMember(Name = "selectedCharacters")]
        public List<string> SelectedCharacters { get; set; } = new List<string>();

        [DataMember(Name = "volume")]
        public int Volume { get; set; } = 80;

        [DataMember(Name = "intervalMinutes")]
        public int IntervalMinutes { get; set; } = 60;

        [DataMember(Name = "isMuted")]
        public bool IsMuted { get; set; } = false;

        [DataMember(Name = "quietRules")]
        public List<QuietScheduleRule> QuietRules { get; set; } = GetDefaultQuietRules();

        public static List<QuietScheduleRule> GetDefaultQuietRules()
        {
            return new List<QuietScheduleRule>
            {
                new QuietScheduleRule(false, 23, 0, true, 7, 0, false, false, true),
                new QuietScheduleRule(false, 12, 0, true, 12, 0, false, true, false),
                new QuietScheduleRule(false, 14, 0, true, 15, 0, false, false, false)
            };
        }

        public static AppSettings Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new AppSettings();
            }

            try
            {
                var bytes = File.ReadAllBytes(filePath);
                int offset = 0;
                // Strip UTF-8 BOM if present
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    offset = 3;
                }

                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using (var ms = new MemoryStream(bytes, offset, bytes.Length - offset))
                {
                    var settings = (AppSettings)serializer.ReadObject(ms);
                    if (settings != null)
                    {
                        if (settings.SelectedCharacters == null)
                        {
                            settings.SelectedCharacters = new List<string>();
                        }
                        if (settings.IntervalMinutes != 10 &&
                            settings.IntervalMinutes != 15 &&
                            settings.IntervalMinutes != 20 &&
                            settings.IntervalMinutes != 30 &&
                            settings.IntervalMinutes != 60)
                        {
                            settings.IntervalMinutes = 60;
                        }
                        if (settings.Volume <= 0)
                        {
                            settings.Volume = 80;
                        }
                        if (settings.QuietRules == null)
                        {
                            settings.QuietRules = GetDefaultQuietRules();
                        }
                        while (settings.QuietRules.Count < 3)
                        {
                            var defaults = GetDefaultQuietRules();
                            settings.QuietRules.Add(defaults[settings.QuietRules.Count]);
                        }
                        return settings;
                    }
                }
            }
            catch
            {
                // Fallback to default if corrupted
            }

            return new AppSettings();
        }

        public void Save(string filePath)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using (var ms = new MemoryStream())
                {
                    serializer.WriteObject(ms, this);
                    var utf8WithoutBom = new UTF8Encoding(false);
                    var json = utf8WithoutBom.GetString(ms.ToArray());
                    File.WriteAllText(filePath, json, utf8WithoutBom);
                }
            }
            catch
            {
                // Ignore save errors during shutdown/read-only cases
            }
        }
    }
}
