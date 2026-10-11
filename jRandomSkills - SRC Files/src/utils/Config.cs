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

        private static DebugCategory debugFlags;

        private static SettingsModel config = LoadConfig();
        public static SettingsModel LoadedConfig => config;

        public static DebugCategory DebugFlags => debugFlags;
        public static bool DebugEnabled(DebugCategory category) => (debugFlags & category) != 0;

        public static SettingsModel LoadConfig()
        {
            lock (fileLock)
            {
                var newConfig = new SettingsModel();

                Instance.Logger.LogInformation("config path: {Path} (exists={Exists})", configPath, File.Exists(configPath));

                if (!File.Exists(configPath))
                {
                    Instance.Logger.LogError("config.json NOT FOUND at the path above; a fresh file with defaults is being written.");
                    SaveConfig(newConfig);
                    debugFlags = DebugCategories.Parse(newConfig.DebugMode);
                    return config = newConfig;
                }

                try
                {
                    string json;
                    using (var fs = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                        json = sr.ReadToEnd();
                    newConfig = JsonConvert.DeserializeObject<SettingsModel>(json) ?? new SettingsModel();

                    if (HasMissingKeys(json) || IsLegacyDebugMode(json))
                        SaveConfig(newConfig);
                }
                catch (Exception ex)
                {
                    Instance.Logger.LogError("Error when loading the config file: {Message}", ex.Message);

                    if (config != null)
                    {
                        Instance.Logger.LogError("config.json was not applied; the previously loaded settings are kept.");
                        return config;
                    }
                }

                if (newConfig.DisplayAlwaysDescription)
                    newConfig.SkillDescriptionDuration = 9999;

                debugFlags = DebugCategories.Parse(newConfig.DebugMode);
                ApplyRarityTables(newConfig);
                return config = newConfig;
            }
        }

        private static void ApplyRarityTables(SettingsModel model)
        {
            RarityManager.SetRarityPercentages(ToRarityTable(model.SkillsChance));
            RarityManager.SetVipRarityPercentages(ToRarityTable(model.VIPSkillsChance));
        }

        private static Dictionary<Rarity, float> ToRarityTable(Dictionary<string, float>? source)
        {
            var table = new Dictionary<Rarity, float>();
            if (source == null) return table;

            foreach (var kv in source)
                if (Enum.TryParse<Rarity>(kv.Key, true, out var rarity))
                    table[rarity] = kv.Value;

            return table;
        }

        private static bool HasMissingKeys(string json)
        {
            try
            {
                var current = Newtonsoft.Json.Linq.JObject.Parse(json);
                var expected = Newtonsoft.Json.Linq.JObject.FromObject(new SettingsModel());

                return HasMissingKeys(current, expected);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasMissingKeys(Newtonsoft.Json.Linq.JObject current, Newtonsoft.Json.Linq.JObject expected)
        {
            foreach (var property in expected.Properties())
            {
                var currentValue = current[property.Name];
                if (currentValue == null) return true;

                if (property.Value is Newtonsoft.Json.Linq.JObject expectedChild
                    && currentValue is Newtonsoft.Json.Linq.JObject currentChild
                    && HasMissingKeys(currentChild, expectedChild)) return true;
            }

            return false;
        }

        private static bool IsLegacyDebugMode(string json)
        {
            try
            {
                var token = Newtonsoft.Json.Linq.JObject.Parse(json)[nameof(SettingsModel.DebugMode)];
                return token != null && token.Type == Newtonsoft.Json.Linq.JTokenType.Boolean;
            }
            catch
            {
                return false;
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

                    File.Move(tempPath, configPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    Instance.Logger.LogError("Error when saving the config file: {Message}", ex.Message);
                }
            }
        }

        public class SettingsModel
        {
            public string ConfigName { get; set; }
            public int GameMode { get; set; }
            public bool YourSkillChatInfo { get; set; }
            public bool KillerSkillChatInfo { get; set; }
            public bool TeamMateSkillChatInfo { get; set; }
            public bool SummaryAfterTheRound { get; set; }
            public bool EnableBotSkills { get; set; }
            public bool EnableBotKickDebug { get; set; }
            public bool EnableFullForceUpdate { get; set; }
            [JsonConverter(typeof(DebugModeConverter))]
            public int DebugMode { get; set; }
            public bool PerfMode { get; set; }
            public string? AlternativeSkillButton { get; set; }
            public float SkillTimeBeforeStart { get; set; }
            public float SkillHudDuration { get; set; }
            public float SkillDescriptionDuration { get; set; }
            public bool DisplayAlwaysDescription { get; set; }
            public bool DisableSpectateHUD { get; set; }
            public bool HideHudForOtherPlugins { get; set; }
            public bool EnableFlashingHtmlHudFix { get; set; }
            public bool TraceRayBeam { get; set; }
            public string DisableHUDOnDeathPermission { get; set; }
            public bool DisableSkillsOnRoundEnd { get; set; }
            public string VIPFlag { get; set; }
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, float> SkillsChance { get; set; }
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, float> VIPSkillsChance { get; set; }
            public int? CurseSkillPerPlayer { get; set; }
            public bool ShowDecoyRing { get; set; }
            public WeaponPools Weapons { get; set; }
            public LanguageSystem LanguageSystem { get; set; }
            public HtmlHudCustomisation HtmlHudCustomisation { get; set; }
            public ChatMessage ChatMessage { get; set; }
            public NormalCommands NormalCommands { get; set; }
            public VotingCommands VotingCommands { get; set; }
            public ModulesSettings Modules { get; set; }
            public EntitySpawnSafetySettings EntitySpawnSafety { get; set; }
            public CombosSettings Combos { get; set; }
            public ServerInfoSettings ServerInfo { get; set; }
            // Name shown in the chat tag line and the plugin list.
            public string PluginName { get; set; } = "TiredPowers";
            // Skills that are loaded but never drawn; admins hand them out with css_setskill (test skills).
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AdminOnlySkills { get; set; } = [];
            // Put a joining player straight onto the smaller team (no team menu). With retakes on, the retakes
            // queue does this (QueueSettings.ShouldAutoJoinGame in retakes.json); this covers retakes off.
            public bool AutoAssignTeamOnJoin { get; set; } = true;
            // End the warmup as soon as the map is up (and on any later warmup round), so play starts at once.
            // The retakes "waiting for players" hold (GameSettings.MinimumPlayers in retakes.json) still applies.
            public bool SkipWarmup { get; set; } = true;
            // Skills work from the moment they are drawn, freeze time included: DisableOnFreezeTime in
            // skillsInfo.json and the skills' own freeze-time checks are ignored.
            public bool SkillsUsableInFreezeTime { get; set; } = true;

            public SettingsModel()
            {
                ConfigName = "Default";
                GameMode = (int)GameModes.NoRepeat;
                YourSkillChatInfo = true;
                KillerSkillChatInfo = true;
                TeamMateSkillChatInfo = true;
                SummaryAfterTheRound = true;
                EnableBotSkills = true;
                EnableBotKickDebug = false;
                EnableFullForceUpdate = false;
                DebugMode = 0;
                PerfMode = false;
                AlternativeSkillButton = "Use";
                SkillTimeBeforeStart = 7;
                SkillHudDuration = -1;
                SkillDescriptionDuration = 7;
                DisplayAlwaysDescription = true;
                EnableFlashingHtmlHudFix = false;
                TraceRayBeam = false;
                DisableSpectateHUD = false;
                HideHudForOtherPlugins = true;
                DisableHUDOnDeathPermission = "@jRandomSkills/death";
                DisableSkillsOnRoundEnd = false;
                VIPFlag = "@css/vip";

                SkillsChance = new Dictionary<string, float>
                {
                    ["Common"] = 0.70f,
                    ["Uncommon"] = 0.14f,
                    ["Rare"] = 0.10f,
                    ["Epic"] = 0.05f,
                    ["Legendary"] = 0.01f,
                };

                VIPSkillsChance = new Dictionary<string, float>
                {
                    ["Common"] = 0.55f,
                    ["Uncommon"] = 0.23f,
                    ["Rare"] = 0.14f,
                    ["Epic"] = 0.07f,
                    ["Legendary"] = 0.01f,
                };
                CurseSkillPerPlayer = null;
                ShowDecoyRing = true;

                Weapons = new WeaponPools
                {
                    Rifle =
                    [
                        "weapon_ak47", "weapon_m4a1", "weapon_m4a1_silencer",
                        "weapon_famas", "weapon_galilar", "weapon_aug", "weapon_sg556",
                        "weapon_mp9", "weapon_mac10", "weapon_bizon", "weapon_mp7",
                        "weapon_ump45", "weapon_p90", "weapon_mp5sd", "weapon_ssg08",
                        "weapon_awp", "weapon_scar20", "weapon_g3sg1", "weapon_nova",
                        "weapon_xm1014", "weapon_mag7", "weapon_sawedoff", "weapon_m249",
                        "weapon_negev"
                    ],
                    Pistol =
                    [
                        "weapon_deagle", "weapon_revolver", "weapon_glock", "weapon_usp_silencer",
                        "weapon_cz75a", "weapon_fiveseven", "weapon_p250", "weapon_tec9",
                        "weapon_elite", "weapon_hkp2000"
                    ],
                    Grenade =
                    [
                        "weapon_hegrenade", "weapon_flashbang", "weapon_smokegrenade",
                        "weapon_molotov", "weapon_incgrenade", "weapon_decoy"
                    ],
                };

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
                        new LanguageInfo("RU, KZ, BY, KG, TJ, UZ, TM, AM, AZ, GE, MD", "ru"),
                        new LanguageInfo("TR", "tr"),
                        new LanguageInfo("CZ", "cs"),
                        new LanguageInfo("PL", "pl"),
                        new LanguageInfo("EN, GB, US", "en")
                    ]
                };

                HtmlHudCustomisation = new HtmlHudCustomisation
                {
                    HeaderLineColor = "#FFFFFF",
                    HeaderLineSize = "",
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
                    WSADMenuVisibleItems = 3,
                    WSADMenuControllsLineSize = "sm",
                    WSADMenuControllsLineColor1 = "cyan",
                    WSADMenuControllsLineColor2 = "white",
                    WSADMenuControllsLineColor3 = "green",
                    VerticalOffsetLines = 1,
                    NoticeLineColor = "#FFD700",
                    NoticeLiftLines = 4,
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
                    HealthCommand = new NormalCommand("sethealth, set_health, health", "@jRandomSkills/admin"),
                    PlantedBomb = new NormalCommand("plantedbomb, planted_bomb, bomb", "@jRandomSkills/admin"),
                    BotPlace = new NormalCommand("botplace, bot_place", "@jRandomSkills/admin"),
                    ConsoleCommand = new NormalCommand("console, sv, 控制台, 服务器", "@jRandomSkills/owner"),
                    HudCommand = new NormalCommand("hud, hood", ""),
                    SetStaticSkillCommand = new NormalCommand("ustawstatycznyskill, ustaw_statyczny_skill, setstaticskill, set_static_skill", "@jRandomSkills/admin"),
                    ChangeLanguageCommand = new NormalCommand("lang, language, changelang, change_lang, jezyk, język", ""),
                    ReloadCommand = new NormalCommand("reload, refresh", "@jRandomSkills/admin"),
                    NextCommand = new NormalCommand("next_skill", "@jRandomSkills/admin"),
                    CheckEntityCommand = new NormalCommand("ent, entity, checkentity, check_entity, sprawdzencje, checkent, check_ent, 检查实体", "@jRandomSkills/owner"),
                };

                VotingCommands = new VotingCommands
                {
                    StartGameCommand = new StartGameCommand(true, "start, go, começar, iniciar, 开始, 启动", "@jRandomSkills/admin", "mp_freezetime 15; mp_forcecamera 0; mp_overtime_enable 1; sv_cheats 0", "mp_freezetime 0; mp_forcecamera 0; mp_overtime_enable 1; sv_cheats 1", 15, 60, 15, 500, 2),
                    ChangeMapCommand = new VotingCommand(true, "map, mapa, changemap, zmienmape, mudarMapa, trocarMapa, 更换地图, 更改地图", "@jRandomSkills/admin", 25, 90, 15, 500, 2),
                    SwapCommand = new VotingCommand(true, "swap, zmiana, trocar, 交换, 切换", "@jRandomSkills/admin", 15, 90, 15, 20, 2),
                    ShuffleCommand = new VotingCommand(true, "shuffle, embaralhar, 随机排序, 洗牌", "@jRandomSkills/admin", 15, 90, 15, 20, 2),
                    PauseCommand = new VotingCommand(true, "pause, unpause, pausar, despausar, 暂停, 恢复", "@jRandomSkills/admin", 15, 60, 15, 2, 2),
                    SetScoreCommand = new VotingCommand(true, "setscore, wynik, definirPontuacao, configurarPontos, 设置分数, 调整分数", "@jRandomSkills/owner", 15, 90, 15, 90, 2),
                };

                Modules = new ModulesSettings();
                EntitySpawnSafety = new EntitySpawnSafetySettings();
                Combos = new CombosSettings();
                ServerInfo = new ServerInfoSettings();
            }
        }

        public class ServerInfoSettings
        {
            // Lines shown to a player right after the welcome line when they join, and repeated in chat to
            // everyone every AdvertIntervalSeconds (0 disables the repeat). Chat colour codes are allowed.
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Lines { get; set; } =
            [
                "Server By : Tired",
                "Hosting By ORI",
                "Shavo GAY",
            ];
            public int AdvertIntervalSeconds { get; set; } = 300;
        }

        public class CombosSettings
        {
            // The most skills a player can hold at once (1 = classic, one skill). Extra skills are drawn
            // from the pool and never clash with the ones already held. Double Trouble adds on top of this.
            public int SkillsPerPlayer { get; set; } = 2;
            // Chance (0-1) that a player wins an extra skill each round; rolled once per extra slot, so with
            // SkillsPerPlayer 3 the third skill needs two wins in a row. 0.1 = roughly every tenth round.
            public float ExtraSkillChance { get; set; } = 0.1f;
            // Two skills fired by the use key would trigger together; keep false so a player gets at most one.
            public bool AllowMultipleUseKeySkills { get; set; } = false;
            // Skills that are never combined with anything (they change or copy the whole skill, or are combos themselves).
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> SoloSkills { get; set; } =
            [
                "None", "Gambler", "Chameleon", "Duplicator", "Thief", "Inheritance", "Deactivator", "Rage",
            ];
            // Skills in the same group are never held together (they fight over the same mechanic).
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<List<string>> ClashGroups { get; set; } =
            [
                ["Flash", "Berserker", "Adrenaline", "EntryRush", "Chicken", "Dwarf"],
                ["Dash", "Pilot", "BunnyHop", "PawelJumper", "Astronaut", "Noclip", "Grapple", "Blink", "GroundSlam"],
                ["Ghost", "Ninja", "C4Camouflage", "Impostor", "Chicken", "Dwarf", "Illusionist"],
                ["Cypher", "FalconEye", "ThirdEye", "Spectator", "Iana"],
                ["GodMode", "Jester", "SecondLife", "Phoenix", "ReZombie"],
                ["AntyHead", "OnlyHead"],
                ["Anomaly", "Rewind", "TimeLord"],
                ["Glue", "HomingNades", "Weightless"],
                ["Baseball", "FrozenDecoy", "GravityDecoy", "MagneticDecoy", "FireRain", "ThunderGod"],
                ["HealingSmoke", "ToxicSmoke", "SmokeJumper"],
                ["Shade", "Teleporter", "ReturnToSender", "Behind", "Catapult", "Push"],
                ["OneShot", "Aimbot", "Soldier", "GlassCannon", "Momentum", "Assassin", "Punisher"],
                ["Armored", "TrueArmor", "ReactiveArmor"],
                ["Cutter", "LongKnife", "ThrowingKnife"],
                ["Pickpocket", "RobinHood"],
                ["Wallhack", "Cypher", "FalconEye"],
            ];
        }

        public class EntitySpawnSafetySettings
        {
            // "Auto": block entity spawning unless the running CS2 version is listed below; "On": always block; "Off": never block.
            public string Mode { get; set; } = "Auto";
            // CS2 versions (csgo/steam.inf PatchVersion) verified to work with the installed CounterStrikeSharp.
            // CounterStrikeSharp 1.0.375 targets 1.41.8.2.
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> VerifiedGameVersions { get; set; } = ["1.41.8.2"];
            // true: the top-of-screen site text (one point_worldtext per player, GameHUD style) is still created while
            // spawning is blocked; set false if the server crashes right after round start with it on.
            public bool AllowScreenText { get; set; } = true;
            // Skills that need to spawn entities; they are left out of the draw while spawning is blocked.
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Skills { get; set; } =
            [
                "AirBombs", "C4Camouflage", "Chicken", "Cypher", "ExplodingBarrel", "ExplosiveChicken", "FalconEye", "Flashlight",
                "Fortnite", "Ghost", "Grapple", "GuidedBullet", "HealingChicken", "Iana", "Illusionist", "Jackal", "LongKnife",
                "LongZeus", "Nightmare", "Ninja", "Pilot", "Rage", "Replicator", "Rewind", "Ricochet", "Spectator",
                "ThirdEye", "ThrowingKnife", "Tripwire", "Wallhack",
            ];
        }

        public class ModulesSettings
        {
            // Retakes game mode (https://github.com/b3none/cs2-retakes). Its own settings live in configs/retakes.json.
            public RetakesModuleSettings Retakes { get; set; } = new();
            public InstadefuseModuleSettings Instadefuse { get; set; } = new();
            public ClutchAnnounceModuleSettings ClutchAnnounce { get; set; } = new();
            public GunsModuleSettings Guns { get; set; } = new();
            public WeaponRoundsSettings WeaponRounds { get; set; } = new();
            public MapVoteSettings MapVote { get; set; } = new();
        }

        public class WeaponRoundsSettings
        {
            // Pistol rounds at the start of every match, and !ak votes for AK / AK headshot / Deagle /
            // Deagle headshot / AWP only rounds. A passed vote applies from the next round.
            public bool Enabled { get; set; } = true;
            public string Alias { get; set; } = "ak, wvote, weaponvote, mode";
            // The first rounds of a match (after a map change or restart) are pistol rounds.
            public int PistolRounds { get; set; } = 2;
            public float VoteSeconds { get; set; } = 20f;
            public float PercentagesToSuccess { get; set; } = 60f;
            public int MinimumPlayersToStartVoting { get; set; } = 2;
            // Wait between two weapon votes.
            public float CooldownSeconds { get; set; } = 60f;
            // Wait before the same player may start another weapon vote.
            public float PlayerCooldownSeconds { get; set; } = 180f;
            // How many rounds a voted mode lasts; 0 keeps it until the next vote (vote "Normal rounds" to end it).
            public int ModeDurationRounds { get; set; } = 0;
            // Skills that hand out guns or change the weapon rules; left out of the draw during pistol / voted rounds.
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> DisabledSkills { get; set; } =
            [
                "RandomWeapon", "SniperElite", "GuidedBullet", "WeaponsSwap", "Glaz", "Zeus", "LongZeus", "PrimaryBan",
                "TakeAmmo", "Disarmament", "BlastShot", "ThrowingKnife", "Grenadier", "Smoker", "Scavenger", "InfiniteAmmo",
            ];
        }

        public class MapVoteSettings
        {
            // !rtv (rock the vote), !nominate and the end-of-map "change or stay" vote. Only maps in MapPool are offered.
            public bool Enabled { get; set; } = true;
            public string RtvAlias { get; set; } = "rtv, rockthevote";
            public string NominateAlias { get; set; } = "nominate, nom";
            public string NextMapAlias { get; set; } = "nextmap";
            // Competitive maps shipped with CS2. Maps not installed on the server are dropped with a console note.
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> MapPool { get; set; } =
            [
                "de_dust2", "de_mirage", "de_inferno", "de_nuke", "de_overpass", "de_ancient", "de_anubis", "de_vertigo", "de_train",
            ];
            // Workshop maps in the pool: name -> workshop ID (host_workshop_map). Leave the ID empty to skip the map.
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, string> WorkshopMaps { get; set; } = new()
            {
                ["de_cache"] = "",
            };
            public float RtvPercentage { get; set; } = 60f;
            public int MinimumPlayersToRtv { get; set; } = 2;
            public float MapVoteSeconds { get; set; } = 20f;
            public int MapsInVote { get; set; } = 5;
            // Round (counting from 1) at which players vote to change the map or stay; 0 turns it off.
            public int EndOfMapVoteRound { get; set; } = 27;
            public float EndOfMapVoteSeconds { get; set; } = 25f;
            // Seconds between a passed !rtv map vote and the map change.
            public float ChangeDelaySeconds { get; set; } = 5f;
        }

        public class GunsModuleSettings
        {
            // !guns lets players pick the rifle and pistol they get each retakes round (there is no buying in retakes).
            public bool Enabled { get; set; } = true;
            public string Alias { get; set; } = "guns, gun, weapons, w, bronie";
            // "CS2MenuManager": drawn by the CS2MenuManager shared library (players pick the style with !mm);
            // "Wasd": the plugin's own WASD menu. Falls back to Wasd when the library is missing.
            public string MenuStyle { get; set; } = "CS2MenuManager";
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> PrimaryT { get; set; } =
            [
                "weapon_ak47", "weapon_galilar", "weapon_sg556", "weapon_awp", "weapon_ssg08", "weapon_g3sg1",
                "weapon_mac10", "weapon_mp7", "weapon_mp5sd", "weapon_ump45", "weapon_p90", "weapon_bizon",
                "weapon_nova", "weapon_xm1014", "weapon_sawedoff", "weapon_m249", "weapon_negev",
            ];
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> PrimaryCT { get; set; } =
            [
                "weapon_m4a1_silencer", "weapon_m4a1", "weapon_famas", "weapon_aug", "weapon_awp", "weapon_ssg08", "weapon_scar20",
                "weapon_mp9", "weapon_mp7", "weapon_mp5sd", "weapon_ump45", "weapon_p90", "weapon_bizon",
                "weapon_nova", "weapon_xm1014", "weapon_mag7", "weapon_m249", "weapon_negev",
            ];
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Secondary { get; set; } =
            [
                "weapon_deagle", "weapon_usp_silencer", "weapon_hkp2000", "weapon_glock", "weapon_p250",
                "weapon_fiveseven", "weapon_tec9", "weapon_cz75a", "weapon_revolver", "weapon_elite",
            ];
        }

        public class RetakesModuleSettings
        {
            public bool Enabled { get; set; } = true;
            // Skills that rely on buying, carrying/planting the bomb or on normal spawns are left out of the draw while retakes runs.
            public bool DisableIncompatibleSkills { get; set; } = true;
            // Keep the game's "The bomb has been planted" banner off the players' screens (it covers the skill HUD).
            public bool HideBombPlantedAlert { get; set; } = true;
            // Round-start site call as a picture (https URL to a PNG/JPG) instead of the "SITE A" text; one per site.
            // Leave empty to keep the text banner.
            public string SiteImageA { get; set; } = "https://cdn.michael.cool/d/A.png";
            public string SiteImageB { get; set; } = "https://cdn.michael.cool/d/B.png";
            public float SiteBannerSeconds { get; set; } = 4f;
            // "Html" (default): big "SITE A" / "SITE B" text at the top of the centre HUD box (no entity, re-sent a few
            // ticks apart). "Screen": the same text as a free point_worldtext in front of the eyes (never parented to
            // the pawn). "Image": the SiteImage picture in the centre HUD (re-sent every tick). "Off": no banner.
            public string SiteBannerStyle { get; set; } = "Html";
            // true: when the standalone RetakesSiteAnnounce plugin is loaded, leave the on-screen call to it.
            // false: draw the built-in banner anyway.
            public bool DeferToStandaloneSiteAnnounce { get; set; } = true;
            // Who gets the site call (chat line, banner and voice): CTs are the ones retaking; Ts spawn on the site.
            public bool SiteCallCtOnly { get; set; } = true;
            // The "Retake A: 1 Ts vs 2 CTs" chat line each round (off: the banner alone, no chat spam).
            public bool SiteCallChat { get; set; } = false;
            // The agent voice line ("bombsite A") each round; also needs EnableBombsiteAnnouncementVoices in retakes.json.
            public bool SiteCallVoice { get; set; } = false;
            // Show the site banner a second time a few seconds after the bomb is planted (stock retakes behaviour).
            public bool RepeatSiteCallAfterPlant { get; set; } = false;
            // Screen text placement, CS2-GameHUD / InfoTop convention: X right, Y up, Z distance from the eyes.
            // InfoTop's defaults are X 0, Y 40, Z 80 with font 20 and 0.25 units per pixel.
            public string ScreenBannerMethod { get; set; } = "Pawn"; // kept for old configs; every method is a free entity now
            public float ScreenBannerX { get; set; } = 0f;
            public float ScreenBannerY { get; set; } = 40f;
            public float ScreenBannerZ { get; set; } = 80f;
            public float ScreenBannerFontSize { get; set; } = 40f;
            public float ScreenBannerUnitsPerPx { get; set; } = 0.25f;
            public string ScreenBannerFont { get; set; } = "Arial Bold";
            // Dark box behind the text, as InfoTop draws it (border size in units; 0 = no box).
            public float ScreenBannerBackgroundBorder { get; set; } = 0.5f;
            public string ScreenBannerColorA { get; set; } = "#FF5050";
            public string ScreenBannerColorB { get; set; } = "#50A0FF";
            // true: the banner is alone on screen, lifted to the top, and the skill text comes back when it ends.
            // false: the banner sits above the skill text in the same box, so the skill never disappears.
            public bool SiteBannerHidesSkill { get; set; } = true;
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> IncompatibleSkills { get; set; } =
            [
                "AreaReaper", "Bankrupt", "Bounty", "C4Camouflage", "ChillOut", "EnemySpawn", "ExpensiveAmmo",
                "HotBomb", "MoneySwap", "Pickpocket", "Planter", "Retreat", "Saper", "ReturnToSender", "RichBoy",
                "RobinHood", "ShortBomb", "Watchmaker",
            ];
            // Skills built around the retakes mode; they are only drawn while it runs.
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> RetakesOnlySkills { get; set; } = ["BombGuardian", "BombSense"];
        }

        public class InstadefuseModuleSettings
        {
            public bool Enabled { get; set; } = true;
            // Grenades or fire closer than this to the bomb block an instant defuse.
            public float InfernoThreatRadius { get; set; } = 250f;
            // When the last T is dead but there is not enough time left to defuse, the bomb explodes at once
            // (T win) without hurting anyone instead of running out its timer.
            public bool ExplodeWithoutDamage { get; set; } = true;
        }

        public class ClutchAnnounceModuleSettings
        {
            public bool Enabled { get; set; } = true;
            // Smallest number of enemies the last player alive must face for the round win to count as a clutch.
            public int MinimumEnemies { get; set; } = 1;
        }

        public class WeaponPools
        {
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public required List<string> Rifle { get; set; }
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public required List<string> Pistol { get; set; }
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public required List<string> Grenade { get; set; }
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
            public required int WSADMenuVisibleItems { get; set; }
            public required string WSADMenuControllsLineSize { get; set; }
            public required string WSADMenuControllsLineColor1 { get; set; }
            public required string WSADMenuControllsLineColor2 { get; set; }
            public required string WSADMenuControllsLineColor3 { get; set; }
            // Empty lines appended under the skill HUD; each one lifts the whole box a little (0 = game default position).
            public int VerticalOffsetLines { get; set; } = 1;
            // Colour of notices (retakes site call, plant prompt) drawn under the skill inside the HUD box.
            public string NoticeLineColor { get; set; } = "#FFD700";
            // Empty lines under a round-start announcement (site call): more lines push the banner higher on screen.
            public int NoticeLiftLines { get; set; } = 4;
            // Ticks between two sends of an unchanged HUD (1 = every tick like the original plugin, 6 = the most
            // before the centre text starts to blink). Fewer sends = less network traffic per player.
            public int HudResendTicks { get; set; } = 6;
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
            public required NormalCommand HealthCommand { get; set; }
            public required NormalCommand PlantedBomb { get; set; }
            public required NormalCommand BotPlace { get; set; }
            public required NormalCommand ConsoleCommand { get; set; }
            public required NormalCommand HudCommand { get; set; }
            public required NormalCommand SetStaticSkillCommand { get; set; }
            public required NormalCommand ChangeLanguageCommand { get; set; }
            public required NormalCommand ReloadCommand { get; set; }
            public required NormalCommand NextCommand { get; set; }
            public required NormalCommand CheckEntityCommand { get; set; }
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