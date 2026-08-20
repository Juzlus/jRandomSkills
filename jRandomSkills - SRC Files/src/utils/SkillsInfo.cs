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
        private static readonly ConcurrentDictionary<(DefaultSkillInfo Config, string Key, Type Target), object?> _valueCache = new();

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
                    bool needsRewrite = root == null;
                    var present = new HashSet<string>(StringComparer.Ordinal);

                    if (root != null)
                        foreach (var skillObj in root)
                        {
                            var name = skillObj["Name"]?.ToString();
                            if (string.IsNullOrEmpty(name)) continue;

                            var instance = newConfig.FirstOrDefault(x => x.Name == name.ToString());
                            if (instance == null) continue;

                            present.Add(name);

                            if (skillObj is JObject current && HasMissingKeys(current, instance))
                                needsRewrite = true;

                            JsonConvert.PopulateObject(skillObj.ToString(), instance);
                        }

                    if (newConfig.Any(s => !string.IsNullOrEmpty(s.Name) && !present.Contains(s.Name)))
                        needsRewrite = true;

                    if (needsRewrite)
                    {
                        SaveConfig(newConfig);
                        Instance.Logger.LogInformation("skillsInfo.json was missing keys; rewritten with the defaults filled in.");
                    }
                }
                catch
                {
                    Instance.Logger.LogError("Error when loading the skills info file.");
                }

                return config = newConfig;
            }
        }

        private static bool HasMissingKeys(JObject current, DefaultSkillInfo expectedInstance)
        {
            var expected = JObject.FromObject(expectedInstance);

            foreach (var property in expected.Properties())
                if (current[property.Name] == null) return true;

            return false;
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

        public static DefaultSkillInfo? GetSkillConfig(Skills skill)
        {
            if (config == null) return null;

            EnsureIndex();
            return _byName.TryGetValue(SkillNames.Get(skill), out var skillConfig) ? skillConfig : null;
        }

        public static T GetValue<T>(object skill, string key)
        {
            if (config == null) return default!;

            EnsureIndex();
            if (!_byName.TryGetValue(skill.ToString()!, out var skillConfig) || skillConfig == null)
                return default!;

            object? cached = _valueCache.GetOrAdd((skillConfig, key, typeof(T)), k => Resolve(k.Config, k.Key, k.Target));
            return cached == null ? default! : (T)cached;
        }

        private static object? Resolve(DefaultSkillInfo skillConfig, string key, Type targetType)
        {
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

            if (value == null) return null;

            Type? underlyingType = Nullable.GetUnderlyingType(targetType);
            return Convert.ChangeType(value, underlyingType ?? targetType);
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
            _valueCache.Clear();
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

        public class DefaultSkillInfo(Skills skill, bool active = true, string color = "#ffffff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, bool disableOnPistolRound = false)
        {
            public bool NeedsTeammates { get; set; } = needsTeammates;
            public bool DisableOnFreezeTime { get; set; } = disableOnFreezeTime;
            public bool DisableOnPistolRound { get; set; } = disableOnPistolRound;
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

    public static class SkillNames
    {
        private static readonly string[] names = BuildNames();

        private static string[] BuildNames()
        {
            var values = Enum.GetValues<Skills>();
            int max = 0;
            foreach (var value in values)
                if ((int)value > max) max = (int)value;

            var table = new string[max + 1];
            foreach (var value in values)
                table[(int)value] = value.ToString();

            return table;
        }

        public static string Get(Skills skill)
        {
            int index = (int)skill;
            if (index < 0 || index >= names.Length) return skill.ToString();
            return names[index] ?? skill.ToString();
        }
    }
}