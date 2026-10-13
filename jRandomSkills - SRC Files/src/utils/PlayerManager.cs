using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;
using src.player;
using CounterStrikeSharp.API.Core;

namespace src.utils
{
    public static class PlayerManager
    {
        private static readonly ConcurrentDictionary<uint, jSkill_PlayerInfo> playersByKey = [];

        private static readonly Dictionary<nint, uint> keyByController = [];
        private static int cachedKeyMapTick = int.MinValue;
        private static readonly Dictionary<uint, CCSPlayerController> controllerByKey = [];

        public static uint Key(this CCSPlayerController player)
        {
            if (keyByController.TryGetValue(player.Handle, out var key)) return key;
            key = player.EntityHandle.Raw;
            if (player.Handle != nint.Zero) keyByController[player.Handle] = key;
            return key;
        }

        public static void ResetKeyCache()
        {
            keyByController.Clear();
            controllerByKey.Clear();
            cachedKeyMapTick = int.MinValue;
            cachedSnapshotTick = int.MinValue;
        }

        private static int cachedTick = int.MinValue;
        private static List<CCSPlayerController> cachedControllers = [];

        private static int cachedBombTick = int.MinValue;
        private static CC4? cachedBomb;

        private static int cachedIdleTick = int.MinValue;
        private static bool cachedIdle = true;

        private static bool serverActive;

        public static void SetServerActive(bool active) => serverActive = active;

        public static bool IsServerIdle()
        {
            if (!serverActive) return true;

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
            if (!serverActive) return [];

            EnsureTickCache();
            return cachedControllers;
        }

        public static CC4? GetTickBomb()
        {
            if (!serverActive) return null;

            if (cachedBomb != null && cachedBomb.IsValid) return cachedBomb;
            if (bombPlanted) return null;

            int tick = Server.TickCount;
            if (cachedBombTick != int.MinValue && tick >= cachedBombTick && tick - cachedBombTick < BombRescanTicks) return null;

            cachedBombTick = tick;
            cachedBomb = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").FirstOrDefault();
            return cachedBomb != null && cachedBomb.IsValid ? cachedBomb : null;
        }

        private const int BombRescanTicks = 64;
        private static bool bombPlanted;

        public static void MarkBombPlanted()
        {
            bombPlanted = true;
            cachedBomb = null;
        }

        public static void ResetBombPlanted()
        {
            bombPlanted = false;
            cachedBombTick = int.MinValue;
        }

        public static void NoteSpawnedEntity(CEntityInstance entity)
        {
            if (entity.DesignerName != "weapon_c4") return;
            cachedBomb = new CC4(entity.Handle);
        }

        public static void ResetBombCache()
        {
            cachedBomb = null;
            cachedBombTick = int.MinValue;
            bombPlanted = false;
        }

        public static void Register(jSkill_PlayerInfo playerInfo)
        {
            ResetKeyCache();
            if (playerInfo == null) return;
            playersByKey[playerInfo.PlayerKey] = playerInfo;
        }

        public static void UnregisterPlayer(uint playerKey)
        {
            ResetKeyCache();
            playersByKey.TryRemove(playerKey, out _);
        }

        public static List<jSkill_PlayerInfo> RemoveStale(uint playerIndex, uint currentKey)
        {
            List<jSkill_PlayerInfo> removed = [];
            foreach (var info in playersByKey.Values)
            {
                if (info.PlayerIndex != playerIndex || info.PlayerKey == currentKey) continue;
                if (playersByKey.TryRemove(info.PlayerKey, out var stale))
                    removed.Add(stale);
            }
            return removed;
        }

        public static jSkill_PlayerInfo? GetPlayer(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return null;

            playersByKey.TryGetValue(player.Key(), out var playerInfo);
            return playerInfo;
        }

        public static jSkill_PlayerInfo? GetPlayerByKey(uint playerKey)
        {
            playersByKey.TryGetValue(playerKey, out var playerInfo);
            return playerInfo;
        }

        public static CCSPlayerController? GetController(uint playerKey)
        {
            int tick = Server.TickCount;
            if (tick != cachedKeyMapTick)
            {
                cachedKeyMapTick = tick;
                controllerByKey.Clear();
                foreach (var player in GetTickPlayers())
                    if (player != null && player.IsValid)
                        controllerByKey[player.Key()] = player;
            }

            if (controllerByKey.TryGetValue(playerKey, out var cached) && cached.IsValid)
                return cached;

            var controller = new CHandle<CCSPlayerController>(playerKey).Value;
            return controller != null && controller.IsValid ? controller : null;
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

            foreach (var info in playersByKey.Values)
            {
                if (info.Skill != skill) continue;

                var controller = GetController(info.PlayerKey);
                if (controller != null)
                    buffer.Add(controller);
            }
        }

        public readonly record struct AlivePlayer(CCSPlayerController Controller, CCSPlayerPawn Pawn, uint Key, CsTeam Team, float X, float Y, float Z, float ViewZ);

        private static int cachedSnapshotTick = int.MinValue;
        private static readonly List<AlivePlayer> aliveSnapshot = [];

        public static List<AlivePlayer> GetAliveSnapshot()
        {
            int tick = Server.TickCount;
            if (tick == cachedSnapshotTick) return aliveSnapshot;
            cachedSnapshotTick = tick;

            aliveSnapshot.Clear();
            foreach (var player in GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;

                var team = player.Team;
                if (team != CsTeam.Terrorist && team != CsTeam.CounterTerrorist) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || pawn.Health <= 0) continue;

                var origin = pawn.AbsOrigin;
                if (origin == null) continue;

                aliveSnapshot.Add(new AlivePlayer(player, pawn, player.Key(), team, origin.X, origin.Y, origin.Z, pawn.ViewOffset.Z));
            }

            return aliveSnapshot;
        }

        public static IEnumerable<jSkill_PlayerInfo> GetAllPlayers()
        {
            return playersByKey.Values;
        }

        public static int GetPlayerCountBySkill(Skills skills)
        {
            return playersByKey.Values.Count(p => p.Skill == skills);
        }

        public static void Clear()
        {
            playersByKey.Clear();
            ResetKeyCache();
        }

    }
}
