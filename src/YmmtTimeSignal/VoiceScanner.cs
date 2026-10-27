using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace YmmtTimeSignal
{
    public class CharacterDef
    {
        public string Prefix { get; set; }
        public string DisplayName { get; set; }

        public CharacterDef(string prefix, string displayName)
        {
            Prefix = prefix;
            DisplayName = displayName;
        }
    }

    public class CharacterVoiceSet
    {
        public CharacterDef Definition { get; set; }
        public Dictionary<int, string> HourlyVoices { get; } = new Dictionary<int, string>();
        public string StartupVoice { get; set; }
        public bool IsInstalled => HourlyVoices.Count > 0 || !string.IsNullOrEmpty(StartupVoice);

        public CharacterVoiceSet(CharacterDef definition)
        {
            Definition = definition;
        }
    }

    public class VoiceScanner
    {
        private static readonly List<CharacterDef> KnownCharacters = new List<CharacterDef>
        {
            new CharacterDef("arale", "仲町あられ"),
            new CharacterDef("nonoka", "宮永ののか"),
            new CharacterDef("ritsu", "峰月律"),
            new CharacterDef("miyako", "藤都子"),
            new CharacterDef("yuno", "千石ユノ")
        };

        private static readonly Regex TimeSignalPattern = new Regex(
            @"^(?<prefix>[a-zA-Z0-9]+)_時報_(?<hour>[0-9]{1,2})時(_re)?\.wav$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex StartupVoicePattern = new Regex(
            @"^(?<prefix>[a-zA-Z0-9]+)_13_起動音(_re)?\.wav$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public List<CharacterVoiceSet> Scan(string baseDir)
        {
            var dict = new Dictionary<string, CharacterVoiceSet>(StringComparer.OrdinalIgnoreCase);

            // Pre-populate known characters
            foreach (var charDef in KnownCharacters)
            {
                dict[charDef.Prefix] = new CharacterVoiceSet(charDef);
            }

            if (!Directory.Exists(baseDir))
            {
                return new List<CharacterVoiceSet>(dict.Values);
            }

            // Enumerate files up to 3 subfolder levels
            var wavFiles = FindWavFilesRecursively(baseDir, maxDepth: 3);

            foreach (var file in wavFiles)
            {
                var fileName = Path.GetFileName(file);
                var match = TimeSignalPattern.Match(fileName);
                if (match.Success)
                {
                    var prefix = match.Groups["prefix"].Value.ToLowerInvariant();
                    if (int.TryParse(match.Groups["hour"].Value, out int hour) && hour >= 0 && hour <= 23)
                    {
                        if (!dict.TryGetValue(prefix, out var voiceSet))
                        {
                            voiceSet = new CharacterVoiceSet(new CharacterDef(prefix, prefix));
                            dict[prefix] = voiceSet;
                        }

                        // Prefer _re or whatever file comes, but don't overwrite if already set unless preferred
                        if (!voiceSet.HourlyVoices.ContainsKey(hour))
                        {
                            voiceSet.HourlyVoices[hour] = file;
                        }
                    }
                }
                else
                {
                    var matchStartup = StartupVoicePattern.Match(fileName);
                    if (matchStartup.Success)
                    {
                        var prefix = matchStartup.Groups["prefix"].Value.ToLowerInvariant();
                        if (!dict.TryGetValue(prefix, out var voiceSet))
                        {
                            voiceSet = new CharacterVoiceSet(new CharacterDef(prefix, prefix));
                            dict[prefix] = voiceSet;
                        }

                        if (string.IsNullOrEmpty(voiceSet.StartupVoice))
                        {
                            voiceSet.StartupVoice = file;
                        }
                    }
                }
            }

            return new List<CharacterVoiceSet>(dict.Values);
        }

        private List<string> FindWavFilesRecursively(string dir, int maxDepth)
        {
            var result = new List<string>();
            ScanDirectory(dir, 0, maxDepth, result);
            return result;
        }

        private void ScanDirectory(string currentDir, int currentDepth, int maxDepth, List<string> accumulator)
        {
            try
            {
                var files = Directory.GetFiles(currentDir, "*.wav");
                accumulator.AddRange(files);
            }
            catch
            {
                // Ignore directories that cannot be accessed
            }

            if (currentDepth < maxDepth)
            {
                try
                {
                    var subDirs = Directory.GetDirectories(currentDir);
                    foreach (var subDir in subDirs)
                    {
                        var dirName = Path.GetFileName(subDir);
                        // Skip common non-asset folders
                        if (dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                            dirName.Equals(".gemini", StringComparison.OrdinalIgnoreCase) ||
                            dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                            dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                            dirName.Equals("src", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        ScanDirectory(subDir, currentDepth + 1, maxDepth, accumulator);
                    }
                }
                catch
                {
                    // Ignore inaccessible directories
                }
            }
        }
    }
}
