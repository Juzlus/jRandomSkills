using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.command
{
    public static class VoteSystem
    {
        private static readonly ConcurrentDictionary<VoteData, byte> votes = [];

        private static void StartVoteTimer(VoteData vote, string commandName)
        {
            SafeTimer.Kill(vote.ActiveTimer);

            vote.ActiveTimer = SafeTimer.Add(vote.TimeToVote, () =>
            {
                if (!votes.ContainsKey(vote) || !vote.GetActive()) return;

                vote.SetActive(false);
                vote.TimeToNextSameVoting = vote.TimeToNextVoting;
                Localization.PrintTranslationToChatAll($" {ChatColors.Red}{{0}}", ["vote_timeout"], false, [commandName]);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        /// <summary>
        /// Validates and normalises the arguments of a vote. The result is later executed as a console
        /// command, so anything that could chain extra commands (';', quotes, newlines...) is rejected.
        /// Returns false when the arguments are not acceptable for the given vote type.
        /// </summary>
        public static bool TryNormalizeArgs(VoteType voteType, string? rawArgs, out string? args)
        {
            args = null;
            string[] parts = (rawArgs ?? "").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

            switch (voteType)
            {
                case VoteType.ChangeMap:
                    if (parts.Length == 0 || !IsSafeMapName(parts[0])) return false;
                    args = parts[0].ToLowerInvariant();
                    return true;

                case VoteType.SetScore:
                    if (parts.Length < 2
                        || !short.TryParse(parts[0], out short ct) || !short.TryParse(parts[1], out short t)
                        || ct < 0 || t < 0)
                        return false;
                    args = $"{ct} {t}";
                    return true;

                default:
                    return parts.Length == 0;
            }
        }

        private static bool IsSafeMapName(string map)
        {
            if (map.Length == 0 || map.Length > 128) return false;
            foreach (char c in map)
                if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/')) return false;
            return !map.Contains("..");
        }

        private static (float TimeToVote, float Percentages, float TimeToNext, float TimeToNextSame, int MinPlayers) GetVoteSettings(VoteType voteType)
        {
            var cfg = Config.LoadedConfig.VotingCommands;
            return voteType switch
            {
                VoteType.StartGame => (cfg.StartGameCommand.TimeToVote, cfg.StartGameCommand.PercentagesToSuccess, cfg.StartGameCommand.TimeToNextVoting, cfg.StartGameCommand.TimeToNextSameVoting, cfg.StartGameCommand.MinimumPlayersToStartVoting),
                VoteType.PauseGame => FromVotingCommand(cfg.PauseCommand),
                VoteType.ShuffleTeam => FromVotingCommand(cfg.ShuffleCommand),
                VoteType.SwapTeam => FromVotingCommand(cfg.SwapCommand),
                VoteType.ChangeMap => FromVotingCommand(cfg.ChangeMapCommand),
                VoteType.SetScore => FromVotingCommand(cfg.SetScoreCommand),
                _ => (10, 60, 5, 2, 2),
            };
        }

        private static (float, float, float, float, int) FromVotingCommand(Config.VotingCommand c)
            => (c.TimeToVote, c.PercentagesToSuccess, c.TimeToNextVoting, c.TimeToNextSameVoting, c.MinimumPlayersToStartVoting);

        private static VoteData? CreateVote(VoteType voteType, string? args = null)
        {
            var (timeToVote, percentages, timeToNext, timeToNextSame, minPlayers) = GetVoteSettings(voteType);
            var vote = new VoteData(timeToVote,
                () => {
                    Server.ExecuteCommand($"{VoteTypeCommands.GetCommand(voteType)}{(!string.IsNullOrEmpty(args) ? $" {args}" : "")}");
                }, percentages, timeToNext, timeToNextSame, minPlayers, voteType, args);

            if (vote.MinimumPlayersToStartVoting > Utilities.GetPlayers().Count(p => !p.IsBot))
                return null;

            votes.TryAdd(vote, 0);
            string commandName = $"!{VoteTypeCommands.GetCommand(vote.Type)?.Replace("css_", "")}{(!string.IsNullOrEmpty(vote?.Args) ? $" {vote?.Args}" : "")}";

            Localization.PrintTranslationToChatAll($" {ChatColors.Lime}{{0}}", ["vote_started"], false, [commandName]);
            foreach (var player in Utilities.GetPlayers())
                player.EmitSound("UIPanorama.tab_mainmenu_news");

            StartVoteTimer(vote!, commandName);

            float[] times = [vote!.TimeToVote, vote.TimeToVote + vote.TimeToNextVoting, vote.TimeToVote + vote.TimeToNextSameVoting];
            SafeTimer.Add(times.Max(), () =>
            {
                if (!votes.ContainsKey(vote)) return;
                votes.TryRemove(vote, out _);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

            return vote;
        }

        public static void Vote(this CCSPlayerController player, VoteType voteType, string? args = null)
        {
            if (!TryNormalizeArgs(voteType, args, out args))
            {
                string key = voteType == VoteType.SetScore ? "correct_form_setscore" : "invalid_map";
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate(key)}");
                return;
            }

            var vote = votes.Keys.FirstOrDefault(v => v.Type == voteType && v.Args == args && v.GetActive());
            if (vote == null)
            {
                if (votes.Keys.Any(v => v.NextVoting() > DateTime.Now))
                {
                    player.PrintToChat($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_wait")}");
                    return;
                }
                else if (votes.Keys.Any(v => v.Type == voteType && v.NextSameVoting() > DateTime.Now))
                {
                    player.PrintToChat($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_same_wait")}");
                    return;
                }

                vote = CreateVote(voteType, args);
            }

            if (vote == null)
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_not_enough_players")}");
                return;
            }

            if (!vote.PlayersVoted.TryAdd(player.SteamID, 0))
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate("vote_alredy_voted")}");
            else CheckVote(vote);
        }

        private static void CheckVote(VoteData vote)
        {
            int voted = vote.PlayersVoted.Count;
            int playerCount = Utilities.GetPlayers().Count(p => !p.IsBot);
            int playersNeeded = (int)Math.Ceiling(playerCount * (vote.PercentagesToSuccess / 100f));
            string commandName = $"!{VoteTypeCommands.GetCommand(vote.Type)?.Replace("css_", "")}{(!string.IsNullOrEmpty(vote?.Args) ? $" {vote?.Args}" : "")}";

            if (voted >= playersNeeded)
            {
                SafeTimer.Kill(vote!.ActiveTimer);
                vote.SuccessAction.Invoke();
                vote.SetActive(false);
            }
            else
            {
                StartVoteTimer(vote!, commandName);
                Localization.PrintTranslationToChatAll($" {ChatColors.Yellow}{{0}} '': {ChatColors.Green}{voted}/{playersNeeded}", ["vote_vote"], false);
            }
        }
    }

    public class VoteData(float timeToVote, Action successAction, float percentagesToSuccess, float timeToNextVoting, float timeToNextSameVoting, int minimumPlayersToStartVoting, VoteType type, string? args = null)
    {
        private bool Active { get; set; } = true;
        public float TimeToVote { get; set; } = timeToVote;
        public Action SuccessAction { get; set; } = successAction;
        public float PercentagesToSuccess { get; set; } = percentagesToSuccess;
        public float TimeToNextVoting { get; set; } = timeToNextVoting;
        public float TimeToNextSameVoting { get; set; } = timeToNextSameVoting;
        public int MinimumPlayersToStartVoting { get; set; } = minimumPlayersToStartVoting;
        public VoteType Type { get; set; } = type;
        public string? Args { get; set; } = args;
        public ConcurrentDictionary<ulong, byte> PlayersVoted { get; set; } = [];
        public Timer? ActiveTimer { get; set; }
        private DateTime CreatedTime { get; set; } = DateTime.Now;

        public void SetActive(bool active)
        {
            Active = active;
        }

        public bool GetActive()
        {
            return Active;
        }

        public DateTime NextVoting()
        {
            return CreatedTime.AddSeconds(TimeToVote + TimeToNextVoting);
        }

        public DateTime NextSameVoting()
        {
            return CreatedTime.AddSeconds(TimeToVote + TimeToNextSameVoting);
        }
    }

    public enum VoteType
    {
        StartGame,
        PauseGame,
        ShuffleTeam,
        SwapTeam,
        ChangeMap,
        SetScore,
    }

    public static class VoteTypeCommands
    {
        private static readonly ConcurrentDictionary<VoteType, string> names = new(
        [
            new KeyValuePair<VoteType, string>(VoteType.StartGame, "css_start"),
            new KeyValuePair<VoteType, string>(VoteType.PauseGame, "css_pause"),
            new KeyValuePair<VoteType, string>(VoteType.ShuffleTeam, "css_shuffle"),
            new KeyValuePair<VoteType, string>(VoteType.SwapTeam, "css_swap"),
            new KeyValuePair<VoteType, string>(VoteType.ChangeMap, "css_map"),
            new KeyValuePair<VoteType, string>(VoteType.SetScore, "css_setscore"),
        ]);

        public static string GetCommand(VoteType type) => names[type];
    }
}
