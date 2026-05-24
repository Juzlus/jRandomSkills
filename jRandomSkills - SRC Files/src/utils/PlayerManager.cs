using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;
using src.player;
using CounterStrikeSharp.API.Core;

namespace src.utils
{
    public static class PlayerManager
    {
        private static readonly ConcurrentDictionary<uint, jSkill_PlayerInfo> playersByIndex = [];

        public static void Register(jSkill_PlayerInfo playerInfo)
        {
            if (playerInfo == null) return;
            playersByIndex[playerInfo.PlayerIndex] = playerInfo;
        }

        public static void UnregisterPlayer(uint playerIndex)
        {
            playersByIndex.TryRemove(playerIndex, out _);
        }

        public static jSkill_PlayerInfo? GetPlayerByIndex(uint? playerIndex)
        {
            if (playerIndex == null) return null;

            playersByIndex.TryGetValue((uint)playerIndex, out var playerInfo);
            return playerInfo;
        }

        public static CCSPlayerController? GetPlayerEvent(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid)
                return null;

            if (!player.ControllingBot)
                return player;

            return Utilities.GetPlayers().FirstOrDefault(p => 
                p != null &&
                p.IsValid &&
                p.IsBot &&
                p.OriginalControllerOfCurrentPawn.Value != null && p.OriginalControllerOfCurrentPawn.Value == player)
                ?? player;
        }

        public static CCSPlayerController? GetPlayerFromEvent(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid)
                return null;

            if (!player.IsBot)
                return player;

            return Utilities.GetPlayers().FirstOrDefault(p =>
                p != null &&
                p.IsValid &&
                !p.IsBot &&
                player.OriginalControllerOfCurrentPawn.Value != null && player.OriginalControllerOfCurrentPawn.Value == p)
                ?? player;
        }

        public static IEnumerable<jSkill_PlayerInfo> GetAllPlayers()
        {
            return playersByIndex.Values;
        }

        public static IEnumerable<jSkill_PlayerInfo> GetAlivePlayers()
        {
            return playersByIndex.Values.Where(p =>
            {
                try
                {
                    var controller = Utilities.GetPlayerFromIndex((int)p.PlayerIndex);
                    return controller != null && controller.IsValid && controller.PawnIsAlive;
                }
                catch
                {
                    return false;
                }
            });
        }

        public static IEnumerable<jSkill_PlayerInfo> GetPlayersByTeam(CsTeam team)
        {
            return playersByIndex.Values.Where(p =>
            {
                try
                {
                    var controller = Utilities.GetPlayerFromIndex((int)p.PlayerIndex);
                    return controller != null && controller.IsValid && controller.Team == team;
                }
                catch
                {
                    return false;
                }
            });
        }

        public static int GetTeamPlayerCount(CsTeam team)
        {
            return GetPlayersByTeam(team).Count();
        }

        public static int GetPlayerCountBySkill(Skills skills)
        {
            return playersByIndex.Values.Count(p => p.Skill == skills);
        }

        public static bool UpdatePlayerSkill(uint playerIndex, Skills skill, Skills specialSkill = Skills.None)
        {
            if (playersByIndex.TryGetValue(playerIndex, out var playerInfo))
            {
                playerInfo.Skill = skill;
                playerInfo.SpecialSkill = specialSkill;
                return true;
            }
            return false;
        }

        public static void Clear()
        {
            playersByIndex.Clear();
        }

        public static void SyncWithPlugin(jRandomSkills instance)
        {
            if (instance?.SkillPlayer == null) return;

            foreach (var player in instance.SkillPlayer)
                Register(player);
        }
    }
}
