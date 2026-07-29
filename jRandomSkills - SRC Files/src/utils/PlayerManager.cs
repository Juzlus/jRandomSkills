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

        private static int cachedTick = int.MinValue;
        private static List<CCSPlayerController> cachedControllers = [];

        private static int cachedBombTick = int.MinValue;
        private static CC4? cachedBomb;

        private static void EnsureTickCache()
        {
            int tick = Server.TickCount;
            if (tick == cachedTick) return;
            cachedTick = tick;
            cachedControllers = Utilities.GetPlayers();
        }

        public static List<CCSPlayerController> GetTickPlayers()
        {
            EnsureTickCache();
            return cachedControllers;
        }

        public static CC4? GetTickBomb()
        {
            int tick = Server.TickCount;
            if (tick != cachedBombTick)
            {
                cachedBombTick = tick;
                cachedBomb = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").FirstOrDefault();
            }

            return cachedBomb != null && cachedBomb.IsValid ? cachedBomb : null;
        }

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

            return GetTickPlayers().FirstOrDefault(p =>
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

            return GetTickPlayers().FirstOrDefault(p =>
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
