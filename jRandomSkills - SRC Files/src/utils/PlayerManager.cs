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

        private static int cachedIdleTick = int.MinValue;
        private static bool cachedIdle = true;

        public static bool IsServerIdle()
        {
            int tick = Server.TickCount;
            if (tick == cachedIdleTick) return cachedIdle;
            cachedIdleTick = tick;

            cachedIdle = true;
            foreach (var player in GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;
                if (player.IsBot || player.IsHLTV) continue;

                cachedIdle = false;
                break;
            }

            return cachedIdle;
        }

        private static int cachedPawnMapTick = int.MinValue;
        private static readonly Dictionary<nint, CCSPlayerController> controllersByPawn = [];

        private static int cachedBotMapTick = int.MinValue;
        private static readonly Dictionary<nint, CCSPlayerController> botsByController = [];
        private static readonly HashSet<nint> tickPlayerHandles = [];

        private static void EnsureTickCache()
        {
            int tick = Server.TickCount;
            if (tick == cachedTick) return;
            cachedTick = tick;
            cachedControllers = Utilities.GetPlayers();
        }

        private static void EnsureBotMapCache()
        {
            int tick = Server.TickCount;
            if (tick == cachedBotMapTick) return;
            cachedBotMapTick = tick;

            botsByController.Clear();
            tickPlayerHandles.Clear();

            foreach (var player in GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;

                tickPlayerHandles.Add(player.Handle);
                if (!player.IsBot) continue;

                var original = player.OriginalControllerOfCurrentPawn.Value;
                if (original != null)
                    botsByController[original.Handle] = player;
            }
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

            EnsureBotMapCache();
            return botsByController.TryGetValue(player.Handle, out var bot) ? bot : player;
        }

        public static CCSPlayerController? GetPlayerFromEvent(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid)
                return null;

            if (!player.IsBot)
                return player;

            var original = player.OriginalControllerOfCurrentPawn.Value;
            if (original == null || !original.IsValid || original.IsBot) return player;

            EnsureBotMapCache();
            return tickPlayerHandles.Contains(original.Handle) ? original : player;
        }

        public static CCSPlayerController? GetControllerByPawn(nint pawnHandle)
        {
            if (pawnHandle == nint.Zero) return null;

            int tick = Server.TickCount;
            if (tick != cachedPawnMapTick)
            {
                cachedPawnMapTick = tick;
                controllersByPawn.Clear();

                foreach (var player in GetTickPlayers())
                {
                    if (player == null || !player.IsValid) continue;

                    var handle = player.Pawn?.Value?.Handle;
                    if (handle != null && handle.Value != nint.Zero)
                        controllersByPawn[handle.Value] = player;
                }
            }

            return controllersByPawn.TryGetValue(pawnHandle, out var controller) ? controller : null;
        }

        public static void FillSkillHolders(Skills skill, List<CCSPlayerController> buffer)
        {
            buffer.Clear();

            foreach (var info in playersByIndex.Values)
            {
                if (info.Skill != skill) continue;

                var controller = Utilities.GetPlayerFromIndex((int)info.PlayerIndex);
                if (controller != null && controller.IsValid)
                    buffer.Add(controller);
            }
        }

        public static IEnumerable<jSkill_PlayerInfo> GetAllPlayers()
        {
            return playersByIndex.Values;
        }

        public static int GetPlayerCountBySkill(Skills skills)
        {
            return playersByIndex.Values.Count(p => p.Skill == skills);
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
