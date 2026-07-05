using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Commands;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using src.command;
using src.player;
using src.utils;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using WASDSharedAPI;
using static CounterStrikeSharp.API.Core.Listeners;

namespace src
{
    public partial class jRandomSkills : BasePlugin
    {
#pragma warning disable CS8618
        public static jRandomSkills Instance { get; private set; }
#pragma warning restore CS8618
        public IEnumerable<jSkill_PlayerInfo> SkillPlayer => PlayerManager.GetAllPlayers();
        public Random Random { get; } = new Random();
        public CCSGameRules? GameRules { get; set; }
        private ConcurrentBag<string> ManifestResources { get; set; } = ["models/sprays/spray_plane.vmdl"];
        public IWasdMenuManager? MenuManager;
        // Skills that were enabled at least once this round; used to reset only those on round change (not all 124).
        public static readonly ConcurrentDictionary<string, byte> ActiveSkillsThisRound = new();

        public override string ModuleName => "[CS2] [ jRandomSkills ]";
        public override string ModuleAuthor => "D3X, Juzlus";
        public override string ModuleDescription => "Plugin adds random skills every round for CS2 by D3X. Modified by Juzlus.";
        public override string ModuleVersion => "1.2.2.b6";

        public override void Load(bool hotReload)
        {
            Instance = this;

            Config.LoadConfig();
            SkillsInfo.LoadSkillsInfo();
            Localization.Load();
            Debug.Load();
            PlayerOnTick.Load();
            Event.Load();
            Command.Load();
            WASDMenuAPI.WASDMenuAPI.LoadPlugin(Instance, hotReload);
            LoadAllSkills();
            PlayerManager.SyncWithPlugin(Instance);

            Instance.RegisterListener<OnServerPrecacheResources>(LoadManifest);

            Task.Run(async () =>
            {
                await Task.Delay(3500);
                PrintInfoToConsole();
            });
        }

        internal void AddToManifest(string prop)
        {
            if (!ManifestResources.Contains(prop))
                ManifestResources.Add(prop);
        }

        internal void LoadManifest(ResourceManifest manifest)
        {
            foreach (var prop in ManifestResources)
                manifest.AddResource(prop);
        }

        internal void LoadAllSkills()
        {
            foreach (var skill in Enum.GetValues(typeof(Skills)))
                if (SkillsInfo.GetValue<bool>(skill, "active"))
                    SkillAction(skill.ToString()!, "LoadSkill");

            Debug.WriteToDebug($"jRandomSkills v{Instance.ModuleVersion} ({SkillData.Skills.Count - 1}/{SkillsInfo.LoadedConfig.Count - 1} Skills) loaded!");
            Debug.WriteToDebug($"GameModes: {(Config.GameModes)Config.LoadedConfig.GameMode}, Lang: {Config.LoadedConfig.LanguageSystem.DefaultLangCode}");
            foreach (var skill in SkillData.Skills)
                Debug.WriteToDebug($"Loaded: {skill.Skill}");
        }

        private static readonly ConcurrentDictionary<(string Skill, string Method), MethodInfo?> _skillMethodCache = new();

        internal object? SkillAction(string skill, string methodName, object[]? param = null)
        {
            if (string.IsNullOrEmpty(skill))
                return null;

            if (methodName == "EnableSkill")
                ActiveSkillsThisRound.TryAdd(skill, 0);

            var method = _skillMethodCache.GetOrAdd((skill, methodName), key =>
            {
                string className = $"src.player.skills.{key.Skill}";

                Type? type = Type.GetType(className)
                    ?? Assembly.GetExecutingAssembly().GetType(className)
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a =>
                        {
                            try { return a.GetTypes(); }
                            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
                            catch { return []; }
                        })
                        .FirstOrDefault(t => t != null && string.Equals(t.FullName, className, StringComparison.Ordinal));

                if (type == null || !typeof(ISkill).IsAssignableFrom(type))
                {
                    Server.PrintToConsole($"Could not find or load {className}");
                    return null;
                }

                return type.GetMethod(key.Method, BindingFlags.Static | BindingFlags.Public);
            });

            return method?.Invoke(null, param);
        }

        internal new void AddCommand(string name, string description, CommandInfo.CommandCallback handler)
        {
            var definition = new CommandDefinition(name, description, handler);
            CommandDefinitions.Add(definition);
            CommandManager.RegisterCommand(definition);
        }

        internal bool IsPlayerValid(CCSPlayerController? player)
        {
            return player != null && player.IsValid && player.PlayerPawn?.Value != null && player.PlayerPawn.Value.IsValid && player.PlayerPawn.Value.LifeState == (byte)LifeState_t.LIFE_ALIVE;
        }

        public uint[] footstepSoundEvents = [3109879199, 70939233, 1342713723, 2722081556, 1909915699, 3193435079, 2300993891, 3847761506, 4084367249, 1342713723, 3847761506, 2026488395, 2745524735, 2684452812, 2265091453, 1269567645, 520432428, 3266483468, 1346129716, 2061955732, 2240518199, 2829617974, 1194677450, 1803111098, 3749333696, 29217150, 1692050905, 2207486967, 2633527058, 3342414459, 988265811, 540697918, 1763490157, 3755338324, 3161194970, 3753692454, 3166948458, 3997353267, 3161194970, 3753692454, 3166948458, 3997353267, 809738584, 3368720745, 3295206520, 3184465677, 123085364, 3123711576, 737696412, 1403457606, 1770765328, 892882552, 3023174225, 4163677892, 3952104171, 4082928848, 1019414932, 1485322532, 1161855519, 1557420499, 1163426340, 809738584, 3368720745, 2708661994, 2479376962, 3295206520, 1404198078, 1194093029, 1253503839, 2189706910, 1218015996, 96240187, 1116700262, 84876002, 1598540856, 2231399653];
        public uint[] silentSoundEvents = [2551626319, 765706800, 765706800, 2860219006, 2162652424, 2551626319, 2162652424, 117596568, 117596568, 740474905, 1661204257, 3009312615, 1506215040, 115843229, 3299941720, 1016523349, 2684452812, 2067683805, 2067683805, 1016523349, 4160462271, 1543118744, 585390608, 3802757032, 2302139631, 2546391140, 144629619, 4152012084, 4113422219, 1627020521, 2899365092, 819435812, 3218103073, 961838155, 1535891875, 1826799645, 3460445620, 1818046345, 3666896632, 3099536373, 1440734007, 1409986305, 1939055066, 782454593, 4074593561, 1540837791, 3257325156];

        private static async void PrintInfoToConsole()
        {
            string? versionFromGithub = await GetLatestVersion();
            var diffrentConfig = JsonSerializer.Serialize(Config.LoadedConfig) != JsonSerializer.Serialize(new Config.SettingsModel());
            var diffrentSkillsInfo = JsonSerializer.Serialize(SkillsInfo.LoadedConfig) != JsonSerializer.Serialize(new SkillsInfo.SkillsInfoModel());

            // Top border
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Magenta;
            Console.WriteLine($"\n************************************************************************************************************\n");

            // ASCII tag
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.WriteLine("      || '||''|.                         '||                      .|'''.|  '||       ||  '||  '||         \r\n     ...  ||   ||   ....   .. ...      .. ||    ...   .. .. ..    ||..  '   ||  ..  ...   ||   ||   ....  \r\n      ||  ||''|'   '' .||   ||  ||   .'  '||  .|  '|.  || || ||    ''|||.   || .'    ||   ||   ||  ||. '  \r\n      ||  ||   |.  .|' ||   ||  ||   |.   ||  ||   ||  || || ||  .     '||  ||'|.    ||   ||   ||  . '|.. \r\n      || .||.  '|' '|..'|' .||. ||.  '|..'||.  '|..|' .|| || ||. |'....|'  .||. ||. .||. .||. .||. |'..|' \r\n   .. |'                                                                                                  \r\n    ''  ");

            // Version info
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.Write($"\njRandomSkills ");

            if (versionFromGithub == null)
            {
                Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Yellow;
                Console.Write($"v{Instance.ModuleVersion} (failed to get version from github)");
            }
            else if (versionFromGithub == Instance.ModuleVersion)
            {
                Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Green;
                Console.Write($"v{Instance.ModuleVersion} (latest version)");
            }
            else
            {
                Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Red;
                Console.Write($"v{Instance.ModuleVersion} (new version {versionFromGithub} detected)");
            }

            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.WriteLine($" ({SkillData.Skills.Count - 1}/{SkillsInfo.LoadedConfig.Count - 1} Skills) loaded!");

            if (versionFromGithub != null && versionFromGithub != Instance.ModuleVersion)
            {
                Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Red;
                Console.WriteLine($"\n#########################################################");
                Console.WriteLine($"# Download the new version from:                        #");
                Console.WriteLine($"# https://github.com/Juzlus/jRandomSkills/releases      #");
                Console.WriteLine($"#########################################################");
            }

            // Config preset
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.Write("\nConfig preset: ");
            Console.ForegroundColor = Config.LoadedConfig.ConfigName == "Default" && diffrentConfig ? (ConsoleColor)CS2ConsoleColors.Yellow : (ConsoleColor)CS2ConsoleColors.LightBlue;
            Console.Write($"{(Config.LoadedConfig.ConfigName == "Default" && diffrentConfig ? "(CUSTOM)" : Config.LoadedConfig.ConfigName)}");

            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.Write("\nSkillsInfo preset: ");
            Console.ForegroundColor = SkillsInfo.LoadedConfig.Name == "Default" && diffrentSkillsInfo ? (ConsoleColor)CS2ConsoleColors.Yellow : (ConsoleColor)CS2ConsoleColors.LightBlue;
            Console.WriteLine($"{(SkillsInfo.LoadedConfig.Name == "Default" && diffrentSkillsInfo ? "(CUSTOM)" : SkillsInfo.LoadedConfig.Name)}");

            // Main config info
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.Write("\nGameMode: ");
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.LightBlue;
            Console.Write($"{(Config.GameModes)Config.LoadedConfig.GameMode} ({Config.LoadedConfig.GameMode})");

            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.Write(", Lang: ");
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.LightBlue;
            Console.Write(Config.LoadedConfig.LanguageSystem.DefaultLangCode);

            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.Write(", DebugMode: ");
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.LightBlue;
            Console.WriteLine(Config.LoadedConfig.DebugMode);

            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.Write("SkillHudDuration: ");
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.LightBlue;
            Console.Write(Config.LoadedConfig.SkillHudDuration == -1 ? "infinity" : Config.LoadedConfig.SkillHudDuration);

            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.Write(", SkillButton: ");
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.LightBlue;
            Console.Write(Config.LoadedConfig.AlternativeSkillButton ?? "(NULL)");

            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.Write(", HtmlHudFix: ");
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.LightBlue;
            Console.WriteLine(Config.LoadedConfig.EnableFlashingHtmlHudFix);

            // Dependences
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.WriteLine($"\nDependences:");
            var files = new Dictionary<string, string> {
                { "Newtonsoft Json", "./Newtonsoft.Json.dll" },
                { "WASDMenuAPI", "./WASDMenuAPI.dll" },
                { "MaxMind", "./MaxMind.Db.dll" },
                { "GeoLite2", "./packages/GeoLite2-Country.mmdb" },
                { "RayTraceApi", "./../../shared/RayTraceApi/RayTraceApi.dll" },
                { "RayTraceImpl", "./../../plugins/RayTraceImpl/RayTraceImpl.dll" },
                { "RayTrace MetaMod", "./../../../metamod/RayTrace.vdf" },
                { "jRandomSkills gamedata", "./../../gamedata/jRandomSkills.gamedata.json" }
            };

            foreach (var fileInfo in files)
            {
                string fullPath = Path.GetFullPath(Path.Combine(Instance.ModuleDirectory, fileInfo.Value));
                if (File.Exists(fullPath))
                {
                    Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Green3;
                    Console.WriteLine($"- {fileInfo.Key} [OK]");
                }
                else
                {
                    Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.LightRed;
                    Console.WriteLine($"- {fileInfo.Key} (Missing: {fileInfo.Value})");
                }
            }

            // Skills info
            List<Skills> enabled = [];
            List<Skills> disabled = [];

            foreach (Skills skill in Enum.GetValues(typeof(Skills)))
                if (skill.ToString() == "None")
                    continue;
                else if (SkillData.Skills.Any(s => s.Skill == skill))
                    enabled.Add(skill);
                else
                    disabled.Add(skill);

            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.WriteLine($"\nEnabled skills ({enabled.Count}):");
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Green;
            Console.WriteLine(" " + string.Join("\n ", enabled.Chunk(10).Select(group => string.Join(", ", group))));

            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Cyan;
            Console.WriteLine($"\nDisabled skills ({disabled.Count}):");
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Red;
            Console.WriteLine(" " + string.Join("\n ", disabled.Chunk(10).Select(group => string.Join(", ", group))));

            // Bottom border
            Console.ForegroundColor = (ConsoleColor)CS2ConsoleColors.Magenta;
            Console.WriteLine($"\n************************************************************************************************************\n");
            Console.ResetColor();
        }

        private static async Task<string?> GetLatestVersion()
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("jRandomSkills", "1.0"));
            const string URL = "https://api.github.com/repos/Juzlus/jRandomSkills/releases/latest";

            try
            {
                string response = await client.GetStringAsync(URL);
                using JsonDocument doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("tag_name", out JsonElement value))
                    return value.GetString()?.Replace("v", "");
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal jSkill_PlayerInfo? GetPlayerInfoByIndex(uint playerIndex)
        {
            return PlayerManager.GetPlayerByIndex(playerIndex);
        }
    }

    public class jSkill_PlayerInfo
    {
        public required bool IsBot { get; set; }
        public required string PlayerName { get; set; }
        public required uint PlayerIndex { get; set; }
        public Skills Skill { get; set; }
        public Skills SpecialSkill { get; set; }
        public float? SkillChance { get; set; }
        public bool IsDrawing { get; set; }
        public DateTime SkillHudExpired { get; set; } = Config.LoadedConfig.SkillHudDuration == -1 ? DateTime.MaxValue : DateTime.Now.AddSeconds(Config.LoadedConfig.SkillHudDuration);
        public DateTime SkillDescriptionHudExpired { get; set; } = Config.LoadedConfig.SkillDescriptionDuration == -1 ? DateTime.MaxValue : DateTime.Now.AddSeconds(Config.LoadedConfig.SkillDescriptionDuration);
        public string? PrintHTML { get; set; }
        public bool DisplayHUD { get; set; }
        public bool SkillUsed = false;
    }

    public class jSkill_SkillInfo(Skills skill, string color, bool display)
    {
        public Skills Skill { get; } = skill;
        public string Color { get; set; } = color;
        public bool Display { get; } = display;

        public static implicit operator Skills(jSkill_SkillInfo v) => v?.Skill ?? Skills.None;
    }

    public static class SkillData
    {
        public static ConcurrentBag<jSkill_SkillInfo> Skills { get; } = [];

        private static Dictionary<Skills, jSkill_SkillInfo>? _bySkill;

        public static jSkill_SkillInfo? GetInfo(Skills skill)
        {
            var map = _bySkill;
            if (map == null)
            {
                map = new Dictionary<Skills, jSkill_SkillInfo>();
                foreach (var s in Skills)
                    map[s.Skill] = s;
                _bySkill = map;
            }
            return map.TryGetValue(skill, out var info) ? info : null;
        }

        public static void Invalidate() => _bySkill = null;
    }

    public enum CS2ConsoleColors
    {
        Black = ConsoleColor.Black,
        White = ConsoleColor.DarkBlue,
        Orange = ConsoleColor.DarkGreen,
        Yellow = ConsoleColor.DarkCyan,
        LightGreen = ConsoleColor.DarkRed,
        Green = ConsoleColor.DarkMagenta,
        Green2 = ConsoleColor.DarkYellow,
        Green3 = ConsoleColor.Gray,
        Cyan = ConsoleColor.DarkGray,
        LightBlue = ConsoleColor.Blue,
        Blue = ConsoleColor.Green,
        DarkPurple = ConsoleColor.Cyan,
        Purple = ConsoleColor.Red,
        Magenta = ConsoleColor.Magenta,
        LightRed = ConsoleColor.Yellow,
        Red = ConsoleColor.White,
    }
}