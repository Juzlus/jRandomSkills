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

            var skillConfig = LoadedConfig.FirstOrDefault(s => s.Name == skill.ToString());
            if (skillConfig == null) return default!;

            var prop = skillConfig.GetType().GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
            {
                var value = prop.GetValue(skillConfig);
                if (value == null) return default!;
                else return (T)Convert.ChangeType(value, typeof(T));
            }

            var field = skillConfig.GetType().GetField(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                var value = field.GetValue(skillConfig);
                if (value == null) return default!;
                else return (T)Convert.ChangeType(value, typeof(T));
            }

            return default!;
        }

        public class SkillsInfoModel : ConcurrentBag<DefaultSkillInfo>
        {
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

        public class DefaultSkillInfo(Skills skill, bool active = true, string color = "#ffffff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "")
        {
            public bool NeedsTeammates { get; set; } = needsTeammates;
            public bool DisableOnFreezeTime { get; set; } = disableOnFreezeTime;
            public int OnlyTeam { get; set; } = (int)onlyTeam;
            public string Color { get; set; } = color;
            public bool Active { get; set; } = active;
            public string Name { get; set; } = skill.ToString();
            public string RequiredPermission { get; set; } = requiredPermission;
        }
    }
}