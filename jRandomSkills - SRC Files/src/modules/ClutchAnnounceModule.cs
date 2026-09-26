using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.modules
{
    // Announces a clutch: the last player alive on a team wins the round against one or more enemies.
    // Same idea as B3none's cs2-clutch-announce, written from scratch for jRandomSkills.
    public class ClutchAnnounceModule
    {
        private readonly BasePlugin _host;

        // Team -> (last player alive, number of enemies alive when they became the last one)
        private readonly Dictionary<CsTeam, (uint PlayerIndex, int Enemies)> _clutchers = [];

        public ClutchAnnounceModule(BasePlugin host)
        {
            _host = host;
        }

        public void Load()
        {
            _host.RegisterEventHandler<EventRoundStart>(OnRoundStart);
            _host.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
            _host.RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        }

        private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            _clutchers.Clear();
            return HookResult.Continue;
        }

        private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            // The victim's pawn is still flagged alive for a moment; count once the death has gone through.
            Server.NextFrame(CheckForClutchers);
            return HookResult.Continue;
        }

        private void CheckForClutchers()
        {
            var alive = Utilities.GetPlayers()
                .Where(p => p.IsValid && !p.IsHLTV && p.PawnIsAlive && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
                .ToList();

            foreach (var team in new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist })
            {
                if (_clutchers.ContainsKey(team)) continue;

                var teamAlive = alive.Where(p => p.Team == team).ToList();
                if (teamAlive.Count != 1) continue;

                int enemies = alive.Count(p => p.Team != team);
                if (enemies < Math.Max(1, Config.LoadedConfig.Modules.ClutchAnnounce.MinimumEnemies)) continue;

                _clutchers[team] = (teamAlive[0].Index, enemies);
            }
        }

        private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            var winner = (CsTeam)@event.Winner;
            if (!_clutchers.TryGetValue(winner, out var clutch)) return HookResult.Continue;

            var player = Utilities.GetPlayerFromIndex((int)clutch.PlayerIndex);
            if (player == null || !player.IsValid || player.Team != winner) return HookResult.Continue;

            Server.PrintToChatAll($" {_host.Localizer["clutch_announce.prefix"]}{_host.Localizer["clutch_announce.clutched", player.PlayerName, clutch.Enemies]}");
            return HookResult.Continue;
        }
    }
}
