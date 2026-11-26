using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;
using src.player;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;

namespace src.utils
{
    public static class PlayerManager
    {
        private static readonly ConcurrentDictionary<uint, jSkill_PlayerInfo> playersByKey = [];
        private static readonly object playersSnapshotLock = new();
        private static jSkill_PlayerInfo[] playersSnapshot = [];
        private static volatile bool playersDirty = true;

        private static jSkill_PlayerInfo[] PlayersSnapshot()
        {
            if (!playersDirty) return playersSnapshot;
            lock (playersSnapshotLock)
            {
                if (playersDirty)
                {
                    playersDirty = false;
                    playersSnapshot = [.. playersByKey.Values];
                }
                return playersSnapshot;
            }
        }

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
        private static readonly CCSPlayerController?[] slotControllers = new CCSPlayerController?[65];

        private static int cachedBombTick = int.MinValue;
        private static CC4? cachedBomb;

        private static int cachedIdleTick = int.MinValue;
        private static bool cachedIdle = true;

        private static bool serverActive;

        public static void SetServerActive(bool active) => serverActive = active;

        public static bool IsServerIdle()
        {
            if (!serverActive) return true;

            int tick = SkillUtils.Tick;
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
            int tick = SkillUtils.Tick;
            if (tick == cachedTick) return;
            cachedTick = tick;

            int max = Math.Min(Server.MaxPlayers, slotControllers.Length);
            var list = new List<CCSPlayerController>(cachedControllers.Count + 2);
            for (int slot = 0; slot < max; slot++)
            {
                nint? pointer = EntitySystem.GetEntityByIndex((uint)slot + 1);
                if (pointer is not { } ptr || ptr == nint.Zero)
                {
                    slotControllers[slot] = null;
                    continue;
                }

                var controller = slotControllers[slot];
                if (controller == null || controller.Handle != ptr)
                    slotControllers[slot] = controller = new CCSPlayerController(ptr);

                if (controller.IsValid && controller.Connected == PlayerConnectedState.Connected)
                    list.Add(controller);
            }
            cachedControllers = list;
        }

        private static void EnsureBotMapCache()
        {
            int tick = SkillUtils.Tick;
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

        public static CCSPlayerController? GetControllerBySlot(int slot)
        {
            if (slot < 0 || slot >= slotControllers.Length) return null;
            if (!serverActive) return Utilities.GetPlayerFromSlot(slot);

            EnsureTickCache();
            var controller = slotControllers[slot];
            if (controller != null) return controller;

            nint? pointer = EntitySystem.GetEntityByIndex((uint)slot + 1);
            if (pointer is not { } ptr || ptr == nint.Zero) return null;
            return slotControllers[slot] = new CCSPlayerController(ptr);
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

            int tick = SkillUtils.Tick;
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
            playersDirty = true;
        }

        public static void UnregisterPlayer(uint playerKey)
        {
            ResetKeyCache();
            playersByKey.TryRemove(playerKey, out _);
            playersDirty = true;
        }

        public static List<jSkill_PlayerInfo> RemoveStale(uint playerIndex, uint currentKey)
        {
            List<jSkill_PlayerInfo> removed = [];
            foreach (var info in PlayersSnapshot())
            {
                if (info.PlayerIndex != playerIndex || info.PlayerKey == currentKey) continue;
                if (playersByKey.TryRemove(info.PlayerKey, out var stale))
                    removed.Add(stale);
            }
            if (removed.Count > 0) playersDirty = true;
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
            int tick = SkillUtils.Tick;
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

        private static int cachedPawnIndexTick = int.MinValue;
        private static readonly Dictionary<uint, CCSPlayerController> controllersByPawnIndex = [];

        public static CCSPlayerController? GetControllerByPawnIndex(uint pawnIndex)
        {
            if (pawnIndex == 0) return null;

            int tick = SkillUtils.Tick;
            if (tick != cachedPawnIndexTick)
            {
                cachedPawnIndexTick = tick;
                controllersByPawnIndex.Clear();

                foreach (var player in GetTickPlayers())
                {
                    if (player == null || !player.IsValid) continue;

                    uint raw = player.Pawn.Raw;
                    if (raw == 0 || raw == 0xFFFFFFFF) continue;
                    controllersByPawnIndex[raw & 0x7FFF] = player;
                }
            }

            return controllersByPawnIndex.TryGetValue(pawnIndex, out var controller) ? controller : null;
        }

        public static CCSPlayerController? GetControllerByPawn(nint pawnHandle)
        {
            if (pawnHandle == nint.Zero) return null;

            int tick = SkillUtils.Tick;
            if (tick != cachedPawnMapTick)
            {
                cachedPawnMapTick = tick;
                controllersByPawn.Clear();

                foreach (var player in GetTickPlayers())
                {
                    if (player == null || !player.IsValid) continue;

                    uint raw = player.Pawn.Raw;
                    if (raw == 0 || raw == 0xFFFFFFFF) continue;

                    nint? handle = EntitySystem.GetEntityByHandle(raw);
                    if (handle is { } pawnPtr && pawnPtr != nint.Zero)
                        controllersByPawn[pawnPtr] = player;
                }
            }

            return controllersByPawn.TryGetValue(pawnHandle, out var controller) ? controller : null;
        }

        public static void FillSkillHolders(Skills skill, List<CCSPlayerController> buffer)
        {
            buffer.Clear();

            foreach (var info in PlayersSnapshot())
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
            int tick = SkillUtils.Tick;
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

        public static jSkill_PlayerInfo[] GetAllPlayers()
        {
            return PlayersSnapshot();
        }

        public static int GetPlayerCountBySkill(Skills skills)
        {
            int count = 0;
            foreach (var p in PlayersSnapshot())
                if (p.Skill == skills) count++;
            return count;
        }

        public static void Clear()
        {
            playersByKey.Clear();
            playersDirty = true;
            ResetKeyCache();
        }

    }
}
