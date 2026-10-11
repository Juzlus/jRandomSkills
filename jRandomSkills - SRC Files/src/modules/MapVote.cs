using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using src.menu;
using src.utils;
using static src.jRandomSkills;

namespace src.modules
{
    // !rtv / !nominate / !nextmap with a competitive-only map pool, and a "change map or stay" vote near the end
    // of the match (Modules.MapVote.EndOfMapVoteRound). A map picked at the end vote loads after the match ends.
    public static class MapVote
    {
        private static Config.MapVoteSettings Settings => Config.LoadedConfig.Modules.MapVote;

        private sealed class Poll
        {
            public readonly List<string> Maps = [];
            public readonly Dictionary<ulong, string> Votes = [];
            public CounterStrikeSharp.API.Modules.Timers.Timer? Timer;
            public Action<string?>? OnDone;
        }

        private sealed class StayVote
        {
            public readonly Dictionary<ulong, bool> Votes = []; // true = change
            public CounterStrikeSharp.API.Modules.Timers.Timer? Timer;
        }

        private static readonly HashSet<ulong> rtvVotes = [];
        private static readonly Dictionary<ulong, string> nominations = [];
        private static readonly List<string> activePool = [];
        private static Poll? poll;
        private static StayVote? stayVote;
        private static string? nextMap;
        private static bool endVoteDone;
        private static bool changePending;
        private static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;

            foreach (var alias in Split(Settings.RtvAlias))
                Instance.AddCommand($"css_{alias}", "Rock the vote: ask for a map change", Command_Rtv);
            foreach (var alias in Split(Settings.NominateAlias))
                Instance.AddCommand($"css_{alias}", "Nominate a map for the next map vote", Command_Nominate);
            foreach (var alias in Split(Settings.NextMapAlias))
                Instance.AddCommand($"css_{alias}", "Show the next map", Command_NextMap);

            Instance.RegisterListener<Listeners.OnMapStart>(OnMapStart);
            Instance.RegisterEventHandler<EventRoundStart>(OnRoundStart);
            Instance.RegisterEventHandler<EventCsWinPanelMatch>(OnMatchEnd);
        }

        private static IEnumerable<string> Split(string aliases) =>
            aliases.Split(',').Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a));

        private static void OnMapStart(string mapName)
        {
            poll?.Timer?.Kill();
            stayVote?.Timer?.Kill();
            poll = null;
            stayVote = null;
            rtvVotes.Clear();
            nominations.Clear();
            nextMap = null;
            endVoteDone = false;
            changePending = false;

            // Map files can only be checked once the server is up; drop pool entries this server does not have.
            Instance.AddTimer(2f, ValidatePool, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private static void ValidatePool()
        {
            activePool.Clear();
            var missing = new List<string>();

            foreach (var raw in Settings.MapPool)
            {
                string map = raw.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(map) || activePool.Contains(map)) continue;

                if (IsWorkshop(map) || Server.IsMapValid(map)) activePool.Add(map);
                else missing.Add(map);
            }

            foreach (var (map, id) in Settings.WorkshopMaps)
            {
                string name = map.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(name) || activePool.Contains(name)) continue;
                if (string.IsNullOrWhiteSpace(id)) { missing.Add($"{name} (no workshop ID)"); continue; }
                activePool.Add(name);
            }

            if (missing.Count > 0)
                Instance.Logger.LogWarning("[TiredPowers] MapVote: these maps are not available on this server and were left out of the vote: {Maps}", string.Join(", ", missing));
            Instance.Logger.LogInformation("[TiredPowers] MapVote pool: {Maps}", string.Join(", ", activePool));
        }

        private static bool IsWorkshop(string map) =>
            Settings.WorkshopMaps.TryGetValue(map, out var id) && !string.IsNullOrWhiteSpace(id);

        private static string Tag => $"{ChatColors.Green}[{Config.LoadedConfig.PluginName}]{ChatColors.Default} ";

        // "de_dust2" -> "Dust2" for menus and chat.
        public static string Pretty(string map)
        {
            string name = map.StartsWith("de_", StringComparison.OrdinalIgnoreCase) ? map[3..] : map;
            return name.Length == 0 ? map : char.ToUpperInvariant(name[0]) + name[1..];
        }

        private static List<CCSPlayerController> Humans() =>
            Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV).ToList();

        private static void Broadcast(string key, params object[] args)
        {
            foreach (var player in Humans())
                player.PrintToChat($" {Tag}{ChatColors.Gold}{player.GetTranslationWithoutIlliterate(key, args)}");
        }

        private static string CurrentMap => Server.MapName.ToLowerInvariant();

        private static string? FindMap(string input)
        {
            string key = input.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(key)) return null;
            return activePool.FirstOrDefault(m => m == key)
                ?? activePool.FirstOrDefault(m => m == $"de_{key}")
                ?? activePool.FirstOrDefault(m => m.Contains(key));
        }

        private static int RoundsPlayed()
        {
            try
            {
                var rules = Instance.GameRules;
                if (rules == null || rules.Handle == nint.Zero) return -1;
                return rules.WarmupPeriod ? -1 : rules.TotalRoundsPlayed;
            }
            catch { return -1; }
        }

        #region Commands
        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
        private static void Command_Rtv(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid || player.IsBot || !Settings.Enabled) return;

            if (poll != null) { OpenPollMenu(player); return; }
            if (stayVote != null) { OpenStayMenu(player); return; }

            if (changePending || nextMap != null)
            {
                player.PrintToChat($" {Tag}{ChatColors.Red}{player.GetTranslationWithoutIlliterate("mv_already_next", Pretty(nextMap ?? "?"))}");
                return;
            }

            var humans = Humans();
            if (humans.Count < Settings.MinimumPlayersToRtv)
            {
                player.PrintToChat($" {Tag}{ChatColors.Red}{player.GetTranslationWithoutIlliterate("mv_not_enough")}");
                return;
            }

            if (!rtvVotes.Add(player.SteamID))
            {
                player.PrintToChat($" {Tag}{ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_alredy_voted")}");
                return;
            }

            int needed = Math.Max(1, (int)Math.Ceiling(humans.Count * (Settings.RtvPercentage / 100f)));
            Broadcast("mv_rtv_vote", player.PlayerName, rtvVotes.Count, needed);

            if (rtvVotes.Count >= needed)
            {
                rtvVotes.Clear();
                StartPoll(map =>
                {
                    if (map == null) return;
                    changePending = true;
                    Broadcast("mv_changing", Pretty(map), (int)Settings.ChangeDelaySeconds);
                    Instance.AddTimer(Math.Max(1f, Settings.ChangeDelaySeconds), () => ChangeTo(map), TimerFlags.STOP_ON_MAPCHANGE);
                });
            }
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
        private static void Command_Nominate(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid || player.IsBot || !Settings.Enabled) return;

            string arg = command.GetArg(1);
            if (string.IsNullOrWhiteSpace(arg))
            {
                var items = activePool.Where(m => m != CurrentMap)
                    .Select(m => { string chosen = m; return new SimpleMenu.Item(Pretty(chosen), p => Nominate(p, chosen)); })
                    .ToList();
                SimpleMenu.Open(player, player.GetTranslationWithoutIlliterate("mv_menu_nominate"), "", items);
                return;
            }

            var map = FindMap(arg);
            if (map == null || map == CurrentMap)
            {
                player.PrintToChat($" {Tag}{ChatColors.Red}{player.GetTranslationWithoutIlliterate("mv_unknown_map", arg, string.Join(", ", activePool.Where(m => m != CurrentMap).Select(Pretty)))}");
                return;
            }

            Nominate(player, map);
        }

        private static void Nominate(CCSPlayerController player, string map)
        {
            nominations[player.SteamID] = map;
            Broadcast("mv_nominated", player.PlayerName, Pretty(map));
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
        private static void Command_NextMap(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid || player.IsBot) return;

            player.PrintToChat(nextMap != null
                ? $" {Tag}{ChatColors.Gold}{player.GetTranslationWithoutIlliterate("mv_nextmap", Pretty(nextMap))}"
                : $" {Tag}{ChatColors.Gold}{player.GetTranslationWithoutIlliterate("mv_nextmap_none")}");
        }
        #endregion

        #region Map poll
        private static void StartPoll(Action<string?> onDone)
        {
            if (poll != null) return;

            var current = CurrentMap;
            var maps = nominations.Values.Where(m => m != current).Distinct().Take(Settings.MapsInVote).ToList();
            var fill = activePool.Where(m => m != current && !maps.Contains(m)).OrderBy(_ => Instance.Random.Next()).ToList();
            maps.AddRange(fill.Take(Math.Max(0, Settings.MapsInVote - maps.Count)));
            nominations.Clear();

            if (maps.Count == 0)
            {
                Instance.Logger.LogWarning("[TiredPowers] MapVote: no other map in the pool is available on this server; nothing to vote on.");
                onDone(null);
                return;
            }

            poll = new Poll { OnDone = onDone };
            poll.Maps.AddRange(maps);

            foreach (var player in Humans())
            {
                player.PrintToChat($" {Tag}{ChatColors.Lime}{player.GetTranslationWithoutIlliterate("mv_poll_started")}");
                player.EmitSound("UIPanorama.tab_mainmenu_news");
                OpenPollMenu(player);
            }

            poll.Timer = Instance.AddTimer(Settings.MapVoteSeconds, FinishPoll, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private static void OpenPollMenu(CCSPlayerController player)
        {
            if (poll == null) return;
            if (poll.Votes.ContainsKey(player.SteamID))
            {
                player.PrintToChat($" {Tag}{ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_alredy_voted")}");
                return;
            }

            var items = poll.Maps.Select(m => { string chosen = m; return new SimpleMenu.Item(Pretty(chosen), p => CastPollVote(p, chosen)); }).ToList();
            SimpleMenu.Open(player, player.GetTranslationWithoutIlliterate("mv_poll_title"), player.GetTranslationWithoutIlliterate("mv_poll_info"), items);
        }

        private static void CastPollVote(CCSPlayerController player, string map)
        {
            if (poll == null || !player.IsValid || poll.Votes.ContainsKey(player.SteamID)) return;

            poll.Votes[player.SteamID] = map;
            player.PrintToChat($" {Tag}{ChatColors.Lime}{player.GetTranslationWithoutIlliterate("mv_voted", Pretty(map))}");

            if (poll.Votes.Count >= Humans().Count)
                FinishPoll();
        }

        private static void FinishPoll()
        {
            var current = poll;
            if (current == null) return;

            current.Timer?.Kill();
            poll = null;

            foreach (var player in Humans())
                SimpleMenu.Close(player);

            if (current.Votes.Count == 0)
            {
                Broadcast("mv_no_votes");
                current.OnDone?.Invoke(null);
                return;
            }

            var tally = current.Votes.Values.GroupBy(m => m).Select(g => (Map: g.Key, Count: g.Count())).ToList();
            int best = tally.Max(t => t.Count);
            var winners = tally.Where(t => t.Count == best).Select(t => t.Map).ToList();
            string winner = winners[Instance.Random.Next(winners.Count)];

            Broadcast("mv_poll_winner", Pretty(winner), best, current.Votes.Count);
            current.OnDone?.Invoke(winner);
        }
        #endregion

        #region End-of-map vote
        private static HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            if (!Settings.Enabled || Settings.EndOfMapVoteRound <= 0 || endVoteDone || nextMap != null || changePending) return HookResult.Continue;

            int played = RoundsPlayed();
            if (played < 0 || played + 1 < Settings.EndOfMapVoteRound) return HookResult.Continue;

            endVoteDone = true;
            if (Humans().Count == 0 || poll != null) return HookResult.Continue;

            stayVote = new StayVote();
            foreach (var player in Humans())
            {
                player.PrintToChat($" {Tag}{ChatColors.Lime}{player.GetTranslationWithoutIlliterate("mv_stay_started", played + 1)}");
                player.EmitSound("UIPanorama.tab_mainmenu_news");
                OpenStayMenu(player);
            }

            stayVote.Timer = Instance.AddTimer(Settings.EndOfMapVoteSeconds, FinishStayVote, TimerFlags.STOP_ON_MAPCHANGE);
            return HookResult.Continue;
        }

        private static void OpenStayMenu(CCSPlayerController player)
        {
            if (stayVote == null) return;
            if (stayVote.Votes.ContainsKey(player.SteamID))
            {
                player.PrintToChat($" {Tag}{ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_alredy_voted")}");
                return;
            }

            SimpleMenu.Open(player, player.GetTranslationWithoutIlliterate("mv_stay_title"), "",
            [
                new SimpleMenu.Item(player.GetTranslationWithoutIlliterate("mv_stay", Pretty(CurrentMap)), p => CastStayVote(p, false)),
                new SimpleMenu.Item(player.GetTranslationWithoutIlliterate("mv_change"), p => CastStayVote(p, true)),
            ]);
        }

        private static void CastStayVote(CCSPlayerController player, bool change)
        {
            if (stayVote == null || !player.IsValid || stayVote.Votes.ContainsKey(player.SteamID)) return;

            stayVote.Votes[player.SteamID] = change;
            player.PrintToChat($" {Tag}{ChatColors.Lime}{player.GetTranslationWithoutIlliterate("mv_voted", player.GetTranslationWithoutIlliterate(change ? "mv_change" : "mv_stay", Pretty(CurrentMap)))}");

            if (stayVote.Votes.Count >= Humans().Count)
                FinishStayVote();
        }

        private static void FinishStayVote()
        {
            var current = stayVote;
            if (current == null) return;

            current.Timer?.Kill();
            stayVote = null;

            foreach (var player in Humans())
                SimpleMenu.Close(player);

            int change = current.Votes.Values.Count(v => v);
            int stay = current.Votes.Count - change;

            if (change > stay)
            {
                StartPoll(map =>
                {
                    if (map == null) return;
                    nextMap = map;
                    Broadcast("mv_next_after_match", Pretty(map));
                });
            }
            else
                Broadcast("mv_staying", Pretty(CurrentMap), stay, change);
        }

        private static HookResult OnMatchEnd(EventCsWinPanelMatch @event, GameEventInfo info)
        {
            if (nextMap == null || changePending) return HookResult.Continue;

            changePending = true;
            string map = nextMap;
            float delay = Math.Max(3f, SkillUtils.CvarValue("mp_match_restart_delay", 10) - 1f);
            Broadcast("mv_changing", Pretty(map), (int)delay);
            Instance.AddTimer(delay, () => ChangeTo(map), TimerFlags.STOP_ON_MAPCHANGE);
            return HookResult.Continue;
        }
        #endregion

        private static void ChangeTo(string map)
        {
            if (Settings.WorkshopMaps.TryGetValue(map, out var id) && !string.IsNullOrWhiteSpace(id))
                Server.ExecuteCommand($"host_workshop_map {id.Trim()}");
            else
                Server.ExecuteCommand($"changelevel {map}");
        }
    }
}
