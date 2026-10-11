using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using src.menu;
using src.utils;
using static src.jRandomSkills;

namespace src.modules
{
    public enum WeaponMode { Normal, Pistol, AkOnly, AkHsOnly, DeagleOnly, DeagleHsOnly, AwpOnly }

    // Pistol rounds at the start of a match and !ak votes for AK / AK headshot / Deagle / Deagle headshot / AWP
    // only rounds. The retakes weapon allocation asks this module first; skills that hand out guns are kept out
    // of the draw while a mode runs; in headshot modes body shots from guns do no damage.
    public static class WeaponRounds
    {
        private static Config.WeaponRoundsSettings Settings => Config.LoadedConfig.Modules.WeaponRounds;

        private static readonly WeaponMode[] VotableModes =
            [WeaponMode.AkOnly, WeaponMode.AkHsOnly, WeaponMode.DeagleOnly, WeaponMode.DeagleHsOnly, WeaponMode.AwpOnly];

        private sealed class Vote
        {
            public WeaponMode Mode;
            public readonly HashSet<ulong> Yes = [];
            public readonly HashSet<ulong> No = [];
            public CounterStrikeSharp.API.Modules.Timers.Timer? Timer;
        }

        private static WeaponMode votedMode = WeaponMode.Normal;
        private static int votedRoundsLeft;
        private static Vote? vote;
        private static DateTime cooldownUntil = DateTime.MinValue;
        private static readonly Dictionary<ulong, DateTime> playerCooldownUntil = [];
        private static bool loaded;

        // Mode of the round in progress.
        public static WeaponMode RoundMode { get; private set; } = WeaponMode.Normal;

        public static bool IsHeadshotOnly => RoundMode is WeaponMode.AkHsOnly or WeaponMode.DeagleHsOnly;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;

            foreach (var alias in Settings.Alias.Split(',').Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)))
                Instance.AddCommand($"css_{alias}", "Vote for AK / Deagle / AWP only rounds", Command_Vote);

            Instance.RegisterListener<Listeners.OnMapStart>(_ => Reset());
            Instance.RegisterEventHandler<EventRoundStart>(OnRoundStart);
            Instance.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        }

        private static void Reset()
        {
            vote?.Timer?.Kill();
            vote = null;
            votedMode = WeaponMode.Normal;
            votedRoundsLeft = 0;
            RoundMode = WeaponMode.Normal;
            cooldownUntil = DateTime.MinValue;
            playerCooldownUntil.Clear();
        }

        // Skills that give guns or bend the weapon rules stay out of the draw during pistol / voted rounds.
        public static bool IsSkillDisabled(string skillName)
        {
            if (!Settings.Enabled || RoundMode == WeaponMode.Normal) return false;
            return Settings.DisabledSkills.Contains(skillName, StringComparer.OrdinalIgnoreCase);
        }

        private static int RoundsPlayed()
        {
            try
            {
                var rules = Instance.GameRules;
                if (rules == null || rules.Handle == nint.Zero)
                    rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
                if (rules == null) return 0;
                if (rules.WarmupPeriod) return -1;
                return rules.TotalRoundsPlayed;
            }
            catch { return 0; }
        }

        private static HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            if (!Settings.Enabled) { RoundMode = WeaponMode.Normal; return HookResult.Continue; }

            int played = RoundsPlayed();
            if (played < 0) { RoundMode = WeaponMode.Normal; return HookResult.Continue; }

            if (Instance.IsRetakesActive && played < Settings.PistolRounds)
                RoundMode = WeaponMode.Pistol;
            else if (votedMode != WeaponMode.Normal)
            {
                RoundMode = votedMode;
                if (Settings.ModeDurationRounds > 0 && --votedRoundsLeft <= 0)
                {
                    var ended = votedMode;
                    votedMode = WeaponMode.Normal;
                    Instance.AddTimer(3f, () => Broadcast("wr_mode_over", ModeName(null, ended)), TimerFlags.STOP_ON_MAPCHANGE);
                }
            }
            else
                RoundMode = WeaponMode.Normal;

            if (RoundMode != WeaponMode.Normal)
                foreach (var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot))
                    player.PrintToChat($" {Tag}{ChatColors.Gold}{player.GetTranslationWithoutIlliterate("wr_round_mode", ModeName(player, RoundMode))}");

            return HookResult.Continue;
        }

        // Retakes off: no allocation service, so the mode's weapons are handed out on spawn instead.
        private static HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            if (!Settings.Enabled || RoundMode == WeaponMode.Normal || Instance.IsRetakesActive) return HookResult.Continue;

            var player = @event.Userid;
            if (player == null || !player.IsValid) return HookResult.Continue;

            Server.NextFrame(() =>
            {
                if (player == null || !player.IsValid || !player.PawnIsAlive) return;
                player.RemoveWeapons();
                GiveModeWeapons(player);
            });
            return HookResult.Continue;
        }

        // Hands out the round's weapons; false when the round is a normal one (caller allocates as usual).
        public static bool TryAllocate(CCSPlayerController player)
        {
            if (!Settings.Enabled || RoundMode == WeaponMode.Normal) return false;
            if (player == null || !player.IsValid) return false;

            GiveModeWeapons(player);
            return true;
        }

        private static void GiveModeWeapons(CCSPlayerController player)
        {
            bool terrorist = player.Team == CsTeam.Terrorist;

            switch (RoundMode)
            {
                case WeaponMode.Pistol:
                    player.GiveNamedItem("item_kevlar");
                    player.GiveNamedItem(terrorist ? "weapon_glock" : "weapon_usp_silencer");
                    break;
                case WeaponMode.AkOnly:
                case WeaponMode.AkHsOnly:
                    player.GiveNamedItem(CsItem.KevlarHelmet);
                    player.GiveNamedItem("weapon_ak47");
                    break;
                case WeaponMode.DeagleOnly:
                case WeaponMode.DeagleHsOnly:
                    player.GiveNamedItem(CsItem.KevlarHelmet);
                    player.GiveNamedItem("weapon_deagle");
                    break;
                case WeaponMode.AwpOnly:
                    player.GiveNamedItem(CsItem.KevlarHelmet);
                    player.GiveNamedItem("weapon_awp");
                    break;
            }

            player.GiveNamedItem(CsItem.Knife);

            if (player.Team == CsTeam.CounterTerrorist && Instance.IsRetakesActive)
            {
                var itemServices = player.PlayerPawn.Value?.ItemServices;
                if (itemServices != null)
                    new CCSPlayer_ItemServices(itemServices.Handle).HasDefuser = true;
            }
        }

        // Headshot modes: gun hits anywhere but the head do nothing.
        public static void OnTakeDamagePre(CBaseEntity entity, CTakeDamageInfo info)
        {
            if (!IsHeadshotOnly || entity == null || info == null) return;
            if (entity.DesignerName != "player") return;

            var attacker = info.Attacker?.Value;
            if (attacker == null || !attacker.IsValid || attacker.DesignerName != "player") return;
            if (attacker.Handle == entity.Handle) return;

            if (!SkillUtils.IsBulletDamage(info)) return;

            var hitGroup = SkillUtils.GetHitGroup(info);
            // Unknown hit group (offset missing on this build): let the hit through rather than make guns useless.
            if (hitGroup is HitGroup_t.HITGROUP_GENERIC or HitGroup_t.HITGROUP_INVALID or HitGroup_t.HITGROUP_HEAD) return;

            info.Damage = 0;
        }

        private static string Tag => $"{ChatColors.Green}[{Config.LoadedConfig.PluginName}]{ChatColors.Default} ";

        public static string ModeName(CCSPlayerController? player, WeaponMode mode)
        {
            string key = mode switch
            {
                WeaponMode.Pistol => "wr_mode_pistol",
                WeaponMode.AkOnly => "wr_mode_ak",
                WeaponMode.AkHsOnly => "wr_mode_akhs",
                WeaponMode.DeagleOnly => "wr_mode_deagle",
                WeaponMode.DeagleHsOnly => "wr_mode_deaglehs",
                WeaponMode.AwpOnly => "wr_mode_awp",
                _ => "wr_mode_normal",
            };
            return player != null ? player.GetTranslationWithoutIlliterate(key) : Localization.GetTranslationWithoutIlliterate(key);
        }

        private static void Broadcast(string key, params object[] args)
        {
            foreach (var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot))
                player.PrintToChat($" {Tag}{ChatColors.Gold}{player.GetTranslationWithoutIlliterate(key, args)}");
        }

        private static List<CCSPlayerController> Humans() =>
            Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV).ToList();

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
        private static void Command_Vote(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid || player.IsBot || !Settings.Enabled) return;

            if (vote != null)
            {
                OpenYesNo(player);
                return;
            }

            if (cooldownUntil > DateTime.Now)
            {
                player.PrintToChat($" {Tag}{ChatColors.Red}{player.GetTranslationWithoutIlliterate("wr_cooldown", (int)(cooldownUntil - DateTime.Now).TotalSeconds)}");
                return;
            }

            // One player cannot keep starting votes: a personal wait on top of the server-wide one.
            if (playerCooldownUntil.TryGetValue(player.SteamID, out var ownUntil) && ownUntil > DateTime.Now)
            {
                player.PrintToChat($" {Tag}{ChatColors.Red}{player.GetTranslationWithoutIlliterate("wr_player_cooldown", (int)(ownUntil - DateTime.Now).TotalSeconds)}");
                return;
            }

            if (Humans().Count < Settings.MinimumPlayersToStartVoting)
            {
                player.PrintToChat($" {Tag}{ChatColors.Red}{player.GetTranslationWithoutIlliterate("wr_not_enough")}");
                return;
            }

            // A mode typed straight after the command (!ak awp) starts the vote without the menu.
            string arg = command.GetArg(1).Trim().ToLowerInvariant().Replace("-", "").Replace("_", "");
            WeaponMode? typed = arg switch
            {
                "ak" or "ak47" or "akonly" => WeaponMode.AkOnly,
                "akhs" or "ak47hs" or "akheadshot" => WeaponMode.AkHsOnly,
                "deagle" or "deagleonly" => WeaponMode.DeagleOnly,
                "deaglehs" or "deagleheadshot" => WeaponMode.DeagleHsOnly,
                "awp" or "awponly" => WeaponMode.AwpOnly,
                "normal" or "off" or "end" => WeaponMode.Normal,
                _ => null,
            };
            if (typed != null)
            {
                StartVote(player, typed.Value);
                return;
            }

            var items = new List<SimpleMenu.Item>();
            foreach (var mode in VotableModes)
            {
                var chosen = mode;
                items.Add(new SimpleMenu.Item(ModeName(player, chosen), p => StartVote(p, chosen)));
            }
            if (votedMode != WeaponMode.Normal)
                items.Add(new SimpleMenu.Item(ModeName(player, WeaponMode.Normal), p => StartVote(p, WeaponMode.Normal)));

            SimpleMenu.Open(player, player.GetTranslationWithoutIlliterate("wr_menu_title"), player.GetTranslationWithoutIlliterate("wr_menu_info"), items);
        }

        private static void StartVote(CCSPlayerController starter, WeaponMode mode)
        {
            if (vote != null || !starter.IsValid) return;

            vote = new Vote { Mode = mode };
            vote.Yes.Add(starter.SteamID);
            playerCooldownUntil[starter.SteamID] = DateTime.Now.AddSeconds(Math.Max(0f, Settings.PlayerCooldownSeconds));

            foreach (var player in Humans())
            {
                player.PrintToChat($" {Tag}{ChatColors.Lime}{player.GetTranslationWithoutIlliterate("wr_vote_started", starter.PlayerName, ModeName(player, mode))}");
                player.EmitSound("UIPanorama.tab_mainmenu_news");
                if (player.SteamID != starter.SteamID)
                    OpenYesNo(player);
            }

            vote.Timer = Instance.AddTimer(Settings.VoteSeconds, () => FinishVote(false), TimerFlags.STOP_ON_MAPCHANGE);
            CheckVote();
        }

        private static void OpenYesNo(CCSPlayerController player)
        {
            if (vote == null) return;
            if (vote.Yes.Contains(player.SteamID) || vote.No.Contains(player.SteamID))
            {
                player.PrintToChat($" {Tag}{ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_alredy_voted")}");
                return;
            }

            string question = player.GetTranslationWithoutIlliterate("wr_vote_question", ModeName(player, vote.Mode));
            SimpleMenu.Open(player, player.GetTranslationWithoutIlliterate("wr_menu_title"), question,
            [
                new SimpleMenu.Item(player.GetTranslationWithoutIlliterate("wr_yes"), p => CastVote(p, true)),
                new SimpleMenu.Item(player.GetTranslationWithoutIlliterate("wr_no"), p => CastVote(p, false)),
            ]);
        }

        private static void CastVote(CCSPlayerController player, bool yes)
        {
            if (vote == null || !player.IsValid) return;
            if (vote.Yes.Contains(player.SteamID) || vote.No.Contains(player.SteamID)) return;

            (yes ? vote.Yes : vote.No).Add(player.SteamID);
            player.PrintToChat($" {Tag}{ChatColors.Lime}{player.GetTranslationWithoutIlliterate("wr_voted", player.GetTranslationWithoutIlliterate(yes ? "wr_yes" : "wr_no"))}");
            CheckVote();
        }

        private static int Needed(int humans) => Math.Max(1, (int)Math.Ceiling(humans * (Settings.PercentagesToSuccess / 100f)));

        private static void CheckVote()
        {
            if (vote == null) return;

            int humans = Humans().Count;
            int needed = Needed(humans);

            if (vote.Yes.Count >= needed) { FinishVote(true); return; }
            if (vote.No.Count > humans - needed) { FinishVote(false); return; }

            Broadcast("wr_progress", ModeName(null, vote.Mode), vote.Yes.Count, needed);
        }

        private static void FinishVote(bool passed)
        {
            var current = vote;
            if (current == null) return;

            current.Timer?.Kill();
            vote = null;
            cooldownUntil = DateTime.Now.AddSeconds(Settings.CooldownSeconds);

            foreach (var player in Humans())
                SimpleMenu.Close(player);

            if (passed)
            {
                votedMode = current.Mode;
                votedRoundsLeft = Settings.ModeDurationRounds;
                Broadcast(current.Mode == WeaponMode.Normal ? "wr_vote_passed_normal" : "wr_vote_passed", ModeName(null, current.Mode));
            }
            else
                Broadcast("wr_vote_failed", ModeName(null, current.Mode), current.Yes.Count, Needed(Humans().Count));
        }
    }
}
