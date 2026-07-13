using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using src.player;
using System.Collections.Concurrent;
using System.Reflection;
using static src.jRandomSkills;

namespace src.utils
{
    public static class SkillsInfo
    {
        private static readonly string configsFolder = Path.Combine(Instance.ModuleDirectory, "configs");
        private static readonly string configPath = Path.Combine(configsFolder, "skillsInfo.json");
        private static readonly object fileLock = new();

        private static SkillsInfoModel config = LoadSkillsInfo();
        public static SkillsInfoModel LoadedConfig => config;

        private static SkillsInfoModel? _indexedConfig;
        private static ConcurrentDictionary<string, DefaultSkillInfo> _byName = new();
        private static readonly ConcurrentDictionary<(Type Type, string Key), MemberInfo?> _memberCache = new();

        public static SkillsInfoModel LoadSkillsInfo()
        {
            lock (fileLock)
            {
                var newConfig = new SkillsInfoModel();

                if (!File.Exists(configPath))
                {
                    Instance.Logger.LogInformation("Config file does not exist. Create a new skills info file...");
                    SaveConfig(newConfig);
                    return config = newConfig;
                }

                try
                {
                    string json;
                    using (var fs = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                        json = sr.ReadToEnd();

                    var root = JsonConvert.DeserializeObject<JArray>(json);
                    if (root != null)
                        foreach (var skillObj in root)
                        {
                            var name = skillObj["Name"]?.ToString();
                            if (string.IsNullOrEmpty(name)) continue;

                            var instance = newConfig.FirstOrDefault(x => x.Name == name.ToString());
                            if (instance != null) JsonConvert.PopulateObject(skillObj.ToString(), instance);
                        }
                }
                catch
                {
                    Instance.Logger.LogError("Error when loading the skills info file.");
                }

                return config = newConfig;
            }
        }

        public static void SaveConfig(SkillsInfoModel config)
        {
            lock (fileLock)
            {
                try
                {
                    Directory.CreateDirectory(configsFolder);
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);

                    string tempPath = $"{configPath}.temp";
                    File.WriteAllText(tempPath, json);

                    File.Copy(tempPath, configPath, overwrite: true);
                    File.Delete(tempPath);
                }
                catch
                {
                    Instance.Logger.LogError("Error when saving the skills info file.");
                }
            }
        }

        public static T GetValue<T>(object skill, string key)
        {
            if (config == null) return default!;

            EnsureIndex();
            if (!_byName.TryGetValue(skill.ToString()!, out var skillConfig) || skillConfig == null)
                return default!;

            var member = _memberCache.GetOrAdd((skillConfig.GetType(), key), k =>
            {
                MemberInfo? m = k.Type.GetProperty(k.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                m ??= k.Type.GetField(k.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                return m;
            });

            object? value = member switch
            {
                PropertyInfo p => p.GetValue(skillConfig),
                FieldInfo f => f.GetValue(skillConfig),
                _ => null
            };

            if (value == null) return default!;

            Type targetType = typeof(T);
            Type? underlyingType = Nullable.GetUnderlyingType(targetType);
            return (T)Convert.ChangeType(value, underlyingType ?? targetType);
        }

        private static void EnsureIndex()
        {
            if (ReferenceEquals(_indexedConfig, config)) return;

            var dict = new ConcurrentDictionary<string, DefaultSkillInfo>();
            foreach (var s in config)
                dict[s.Name] = s;

            _byName = dict;
            _indexedConfig = config;
            _memberCache.Clear();
        }

        public class SkillsInfoModel : ConcurrentBag<DefaultSkillInfo>
        {
            public string Name { get; set; } = "Default";
            public SkillsInfoModel()
            {
                foreach (var skill in
                    Assembly.GetExecutingAssembly().GetTypes()
                        .Where(t => typeof(DefaultSkillInfo).IsAssignableFrom(t) && t.Name == "SkillConfig")
                        .Select(t =>
                        {
                            var ctor = t.GetConstructors().FirstOrDefault(c => c.GetParameters().All(p => p.IsOptional));
                            if (ctor == null) return null;
                            var args = ctor.GetParameters().Select(p => Type.Missing).ToArray();
                            return ctor.Invoke(args) as DefaultSkillInfo;
                        })
                        .Where(instance => instance != null)
                        .Cast<DefaultSkillInfo>())
                    Add(skill);
            }
        }

        public class DefaultSkillInfo(Skills skill, bool active = true, string color = "#ffffff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common)
        {
            public bool NeedsTeammates { get; set; } = needsTeammates;
            public bool DisableOnFreezeTime { get; set; } = disableOnFreezeTime;
            public int OnlyTeam { get; set; } = (int)onlyTeam;
            public string Color { get; set; } = color;
            public bool Active { get; set; } = active;
            public string Name { get; set; } = skill.ToString();
            public float? HudDuration { get; set; } = hudDuration;
            public float? DescriptionHudDuration { get; set; } = descriptionHudDuration;
            public string RequiredPermission { get; set; } = requiredPermission;
            public int MaxPerServer { get; set; } = maxPerServer;
            public string Rarity { get; set; } = rarity.ToString();
        }

    }
}