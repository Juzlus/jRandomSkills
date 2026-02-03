using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using static src.jRandomSkills;

namespace src.utils
{
    public static class Config
    {
        private static readonly string configsFolder = Path.Combine(Instance.ModuleDirectory, "configs");
        private static readonly string configPath = Path.Combine(configsFolder, "config.json");
        private static readonly object fileLock = new();

        private static SettingsModel config = LoadConfig();
        public static SettingsModel LoadedConfig => config;

        public static SettingsModel LoadConfig()
        {
            lock (fileLock)
            {
                var newConfig = new SettingsModel();

                if (!File.Exists(configPath))
                {
                    Instance.Logger.LogInformation("Config file does not exist. Create a new config file...");
                    SaveConfig(newConfig);
                    return config = newConfig;
                }

                try
                {
                    string json;
                    using (var fs = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                        json = sr.ReadToEnd();
                    newConfig = JsonConvert.DeserializeObject<SettingsModel>(json) ?? new SettingsModel();
                }
                catch
                {
                    Instance.Logger.LogError("Error when loading the config file.");
                }

                if (newConfig.DisplayAlwaysDescription)
                    newConfig.SkillDescriptionDuration = 9999;
                return config = newConfig;
            }
        }

        public static void SaveConfig(SettingsModel config)
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
                    Instance.Logger.LogError("Error when saving the config file.");
                }
            }
        }

        public class SettingsModel
        {
            public int GameMode { get; set; }
            public bool YourSkillChatInfo { get; set; }
            public bool KillerSkillChatInfo { get; set; }
            public bool TeamMateSkillChatInfo { get; set; }
            public bool SummaryAfterTheRound { get; set; }
            public bool DebugMode { get; set; }
            public string? AlternativeSkillButton { get; set; }
            public float SkillTimeBeforeStart { get; set; }
            public float SkillHudDuration { get; set; }
            public float SkillDescriptionDuration { get; set; }
            public bool DisplayAlwaysDescription { get; set; }
            public bool DisableSpectateHUD { get; set; }
            public bool FlashingHtmlHudFix { get; set; }
            public bool CS2TraceRayDebug { get; set; }
            public string DisableHUDOnDeathPermission { get; set; }
            public bool DisableSkillsOnRoundEnd { get; set; }
            public LanguageSystem LanguageSystem { get; set; }
            public HtmlHudCustomisation HtmlHudCustomisation {  get; set; }
            public ChatMessage ChatMessage { get; set; }
            public NormalCommands NormalCommands { get; set; }
            public VotingCommands VotingCommands { get; set; }

            public SettingsModel()
            {
                GameMode = (int)GameModes.NoRepeat;
                YourSkillChatInfo = true;
                KillerSkillChatInfo = true;
                TeamMateSkillChatInfo = true;
                SummaryAfterTheRound = true;
                DebugMode = false;
                AlternativeSkillButton = null;
                SkillTimeBeforeStart = 7;
                SkillHudDuration = 999;
                SkillDescriptionDuration = 999;
                DisplayAlwaysDescription = false;
                FlashingHtmlHudFix = true;
                CS2TraceRayDebug = false;
                DisableSpectateHUD = false;
                DisableHUDOnDeathPermission = "@jRandomSkills/death";
                DisableSkillsOnRoundEnd = false;

                LanguageSystem = new LanguageSystem
                {
                    DefaultLangCode = "en",
                    DisableGeoLite = false,
                    LanguageInfos =
                    [
                        new LanguageInfo("ZH, CN, TW, HK, MO, SG", "zh"),
                        new LanguageInfo("PT-BR, PT, BR, AO, CV, GW, MZ, ST, TL", "pt-br"),
                        new LanguageInfo("FR, MC, HT", "fr"),
                        new LanguageInfo("DE, AT, CH, LI, LU, BE", "de"),
                        new LanguageInfo("PL", "pl"),
                        new LanguageInfo("EN, GB, US", "en")
                    ]
                };

                HtmlHudCustomisation = new HtmlHudCustomisation
                {
                    HeaderLineColor = "#FFFFFF",
                    HeaderLineSize = "ml",
                    SkillLineSize = "l",
                    InfoLineColor = "#FFFFFF",
                    InfoLineSize = "sm",
                    SkillDescriptionLineColor = "#999999",
                    SkillDescriptionLineSize = "sm",
                    WSADMenuSelectInfoLineColor = "#999999",
                    WSADMenuSelectInfoLineSize = "sm",
                    WSADMenuItemLineColor = "white",
                    WSADMenuItemHoverLineColor = "orange",
                    WSADMenuItemLineSize = "sm",
                    WSADMenuControllsLineSize = "sm",
                    WSADMenuControllsLineColor1 = "cyan",
                    WSADMenuControllsLineColor2 = "white",
                    WSADMenuControllsLineColor3 = "green",
                };

                ChatMessage = new ChatMessage
                {
                    MaxWidth = 1280,
                    LineSymbol = '―',
                    LineColor = "\x04",
                    LineShow = true,
                    InfoPlayerNameColor = "\x02",
                    InfoSkillColor = "\x06",
                    InfoMessageShow = true,
                    TagFormat = "\x02◢◆◤ {TAG} ◥◆◣",
                };

                NormalCommands = new NormalCommands
                {
                    SetSkillCommand = new NormalCommand("ustawskill, ustaw_skill, setskill, set_skill, definirhabilidade, configurarhabilidade, 设置技能, 配置技能", "@jRandomSkills/admin"),
                    SkillsListCommand = new NormalCommand("supermoc, skille, listamocy, supermoce, skills, listaHabilidades, habilidades, 技能列表, 超能力列表", "@jRandomSkills/admin"),
                    UseSkillCommand = new NormalCommand("t, useSkill, usarHabilidade, 技能使用, 使用技能", "@jRandomSkills/admin"),
                    HealCommand = new NormalCommand("heal, ulecz, curar, tratar, 治疗, 治愈", "@jRandomSkills/admin"),
                    ConsoleCommand = new NormalCommand("console, sv, 控制台, 服务器", "@jRandomSkills/owner"),
                    HudCommand = new NormalCommand("hud, hood", ""),
                    SetStaticSkillCommand = new NormalCommand("ustawstatycznyskill, ustaw_statyczny_skill, setstaticskill, set_static_skill", "@jRandomSkills/admin"),
                    ChangeLanguageCommand = new NormalCommand("lang, language, changelang, change_lang, jezyk, język", ""),
                    ReloadCommand = new NormalCommand("reload, refresh", "@jRandomSkills/admin"),
                };

                VotingCommands = new VotingCommands
                {
                    StartGameCommand = new StartGameCommand(true, "start, go, começar, iniciar, 开始, 启动", "@jRandomSkills/admin", "mp_freezetime 15; mp_forcecamera 0; mp_overtime_enable 1; sv_cheats 0", "mp_freezetime 0; mp_forcecamera 0; mp_overtime_enable 1; sv_cheats 1", 15, 60, 15, 500, 2),
                    ChangeMapCommand = new VotingCommand(true, "map, mapa, changemap, zmienmape, zmienmape, mudarMapa, trocarMapa, 更换地图, 更改地图", "@jRandomSkills/admin", 25, 90, 15, 500, 2),
                    SwapCommand = new VotingCommand(true, "swap, zmiana, trocar, 交换, 切换", "@jRandomSkills/admin", 15, 90, 15, 20, 2),
                    ShuffleCommand = new VotingCommand(true, "shuffle, embaralhar, 随机排序, 洗牌", "@jRandomSkills/admin", 15, 90, 15, 20, 2),
                    PauseCommand = new VotingCommand(true, "pause, unpause, pausar, despausar, 暂停, 恢复", "@jRandomSkills/admin", 15, 60, 15, 2, 2),
                    SetScoreCommand = new VotingCommand(true, "setscore, wynik, definirPontuacao, configurarPontos, 设置分数, 调整分数", "@jRandomSkills/owner", 15, 90, 15, 90, 2),
                };
            }
        }

        public class ChatMessage
        {
            public required float MaxWidth { get; set; }
            public required char LineSymbol { get; set; }
            public required string LineColor { get; set; }
            public required bool LineShow { get; set; }
            public required string InfoPlayerNameColor { get; set; }
            public required string InfoSkillColor { get; set; }
            public required bool InfoMessageShow { get; set; }
            public required string TagFormat { get; set; }
        }

        public class HtmlHudCustomisation
        {
            public required string HeaderLineColor { get; set; }
            public required string HeaderLineSize { get; set; }
            public required string SkillLineSize { get; set; }
            public required string InfoLineColor { get; set; }
            public required string InfoLineSize { get; set; }
            public required string SkillDescriptionLineColor { get; set; }
            public required string SkillDescriptionLineSize { get; set; }
            public required string WSADMenuSelectInfoLineColor { get; set; }
            public required string WSADMenuSelectInfoLineSize { get; set; }
            public required string WSADMenuItemLineColor { get; set; }
            public required string WSADMenuItemHoverLineColor { get; set; }
            public required string WSADMenuItemLineSize { get; set; }
            public required string WSADMenuControllsLineSize { get; set; }
            public required string WSADMenuControllsLineColor1 { get; set; }
            public required string WSADMenuControllsLineColor2 { get; set; }
            public required string WSADMenuControllsLineColor3 { get; set; }
        }

        public class LanguageSystem
        {
            public required string DefaultLangCode { get; set; }
            public required bool DisableGeoLite { get; set; }
            public required LanguageInfo[] LanguageInfos { get; set; }
        }

        public class LanguageInfo(string isoCodes, string fileName)
        {
            public string IsoCodes { get; set; } = isoCodes;
            public string FileName { get; set; } = fileName;
        }

        public class NormalCommand(string alias, string permissions)
        {
            public string Alias { get; set; } = alias;
            public string Permissions { get; set; } = permissions;
        }

        public class NormalCommands
        {
            public required NormalCommand SetSkillCommand { get; set; }
            public required NormalCommand SkillsListCommand { get; set; }
            public required NormalCommand UseSkillCommand { get; set; }
            public required NormalCommand HealCommand { get; set; }
            public required NormalCommand ConsoleCommand { get; set; }
            public required NormalCommand HudCommand { get; set; }
            public required NormalCommand SetStaticSkillCommand { get; set; }
            public required NormalCommand ChangeLanguageCommand { get; set; }
            public required NormalCommand ReloadCommand { get; set; }
        }

        public class VotingCommand(bool enableVoting, string alias, string permissions, float timeToVote, float percentagesToSuccess, float timeToNextVoting, float timeToNextSameVoting, int minimumPlayersToStartVoting) : NormalCommand(alias, permissions)
        {
            public bool EnableVoting { get; set; } = enableVoting;
            public float TimeToVote { get; set; } = timeToVote;
            public float PercentagesToSuccess { get; set; } = percentagesToSuccess;
            public float TimeToNextVoting { get; set; } = timeToNextVoting;
            public float TimeToNextSameVoting { get; set; } = timeToNextSameVoting;
            public int MinimumPlayersToStartVoting { get; set; } = minimumPlayersToStartVoting;
        }

        public class StartGameCommand(bool enableVoting, string alias, string permissions, string startParams, string svStartParams, float timeToVote, float percentagesToSuccess, float timeToNextVoting, float timeToNextSameVoting, int minimumPlayersToStartVoting)
        {
            public bool EnableVoting { get; set; } = enableVoting;
            public string Alias { get; set; } = alias;
            public string Permissions { get; set; } = permissions;
            public string StartParams { get; set; } = startParams;
            public string SVStartParams { get; set; } = svStartParams;
            public float TimeToVote { get; set; } = timeToVote;
            public float PercentagesToSuccess { get; set; } = percentagesToSuccess;
            public float TimeToNextVoting { get; set; } = timeToNextVoting;
            public float TimeToNextSameVoting { get; set; } = timeToNextSameVoting;
            public int MinimumPlayersToStartVoting { get; set; } = minimumPlayersToStartVoting;
        }

        public class VotingCommands
        {
            public required StartGameCommand StartGameCommand { get; set; }
            public required VotingCommand ChangeMapCommand { get; set; }
            public required VotingCommand SwapCommand { get; set; }
            public required VotingCommand ShuffleCommand { get; set; }
            public required VotingCommand PauseCommand { get; set; }
            public required VotingCommand SetScoreCommand { get; set; }
        }

        public enum GameModes
        {
            Normal = 0,
            TeamSkills = 1,
            SameSkills = 2,
            NoRepeat = 3,
            FullRandom = 4,
            Debug = 5
        }
    }
}