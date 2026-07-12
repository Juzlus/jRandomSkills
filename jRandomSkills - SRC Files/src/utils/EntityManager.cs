using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;

namespace src.utils
{
    public static class EntityManager
    {
        public const uint SystemOwnerIndex = uint.MaxValue;
        private static readonly ConcurrentDictionary<uint, EntityData> trackedEntities = [];

        private struct EntityData
        {
            public uint EntityIndex;
            public uint PlayerIndex;
            public string EntityType;
            public DateTime CreatedAt;
        }

        private const int EntityBudget = 3500;
        private static int _cachedCount;
        private static int _cachedCountTick = -1000000;

        public static bool OverBudget()
        {
            int tick = Server.TickCount;
            if (tick - _cachedCountTick > 64 || tick < _cachedCountTick)
            {
                _cachedCountTick = tick;
                try { _cachedCount = Utilities.GetAllEntities().Count(); }
                catch { _cachedCount = 0; }
            }
            return _cachedCount >= EntityBudget;
        }

        public static void RegisterEntity(uint entityIndex, uint playerIndex, string entityType)
        {
            if (entityIndex == 0) return;

            trackedEntities[entityIndex] = new EntityData
            {
                EntityIndex = entityIndex,
                PlayerIndex = playerIndex,
                EntityType = entityType,
                CreatedAt = DateTime.UtcNow
            };
        }

        public static void RegisterExisting(CBaseEntity? entity, uint playerIndex, string entityType)
        {
            if (entity == null || !entity.IsValid) return;
            RegisterEntity(entity.Index, playerIndex, entityType);
        }

        public static List<uint> GetPlayerEntities(uint playerIndex, string? entityType = null)
        {
            return [.. trackedEntities
                .Where(kvp => kvp.Value.PlayerIndex == playerIndex && (string.IsNullOrEmpty(entityType) || kvp.Value.EntityType == entityType))
                .Select(kvp => kvp.Key)];
        }

        public static int GetTrackedCount(uint playerIndex) => GetPlayerEntities(playerIndex).Count;

        public static (int totalTracked, int ownerCount) GetStatistics()
        {
            return (trackedEntities.Count, trackedEntities.Values.Select(e => e.PlayerIndex).Distinct().Count());
        }


        public static CParticleSystem? CreateTrackedParticleSystem(uint playerIndex, string particleName, float? autoDestroySeconds = null)
        {
            try
            {
                if (OverBudget()) return null;
                var particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
                if (particle == null || !particle.IsValid) return null;

                particle.EffectName = particleName;
                particle.StartActive = true;
                particle.DispatchSpawn();

                RegisterEntity(particle.Index, playerIndex, "particle_system");
                ScheduleAutoDestroy(particle.Index, autoDestroySeconds);
                return particle;
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[EntityManager] CreateTrackedParticleSystem: {ex.Message}");
                return null;
            }
        }

        public static CDynamicProp? CreateTrackedDynamicProp(uint playerIndex, string designerName = "prop_dynamic")
        {
            try
            {
                if (OverBudget()) return null;
                var prop = Utilities.CreateEntityByName<CDynamicProp>(designerName);
                if (prop == null || !prop.IsValid) return null;

                RegisterEntity(prop.Index, playerIndex, designerName);
                return prop;
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[EntityManager] CreateTrackedDynamicProp: {ex.Message}");
                return null;
            }
        }

        public static CDynamicProp? CreateTrackedPropOverride(uint playerIndex)
        {
            return CreateTrackedDynamicProp(playerIndex, "prop_dynamic_override");
        }

        public static CEnvShake? CreateTrackedEnvShake(uint playerIndex)
        {
            try
            {
                if (OverBudget()) return null;
                var shake = Utilities.CreateEntityByName<CEnvShake>("env_shake");
                if (shake == null || !shake.IsValid) return null;

                shake.DispatchSpawn();
                RegisterEntity(shake.Index, playerIndex, "env_shake");
                return shake;
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[EntityManager] CreateTrackedEnvShake: {ex.Message}");
                return null;
            }
        }

        public static CChicken? CreateTrackedChicken(uint playerIndex)
        {
            try
            {
                if (OverBudget()) return null;
                var chicken = Utilities.CreateEntityByName<CChicken>("chicken");
                if (chicken == null || !chicken.IsValid) return null;

                chicken.DispatchSpawn();
                RegisterEntity(chicken.Index, playerIndex, "chicken");
                return chicken;
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[EntityManager] CreateTrackedChicken: {ex.Message}");
                return null;
            }
        }

        public static CPhysicsPropMultiplayer? CreateTrackedPhysicsProp(uint playerIndex)
        {
            try
            {
                if (OverBudget()) return null;
                var prop = Utilities.CreateEntityByName<CPhysicsPropMultiplayer>("prop_physics_multiplayer");
                if (prop == null || !prop.IsValid) return null;

                RegisterEntity(prop.Index, playerIndex, "prop_physics_multiplayer");
                return prop;
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[EntityManager] CreateTrackedPhysicsProp: {ex.Message}");
                return null;
            }
        }

        public static CTriggerMultiple? CreateTrackedTrigger(uint playerIndex, string name, float radius, Vector pos)
        {
            if (pos == null) return null;

            try
            {
                if (OverBudget()) return null;
                var trigger = Utilities.CreateEntityByName<CTriggerMultiple>("trigger_multiple");
                if (trigger == null || trigger.AbsOrigin == null) return null;

                trigger.Collision.SolidType = SolidType_t.SOLID_CAPSULE;
                trigger.Collision.SolidFlags = 0;
                trigger.Spawnflags = 1;
                trigger.Globalname = $"{name}_{trigger.Index}";
                trigger.Collision.SolidFlags = 1;

                trigger.AbsOrigin.X = pos.X;
                trigger.AbsOrigin.Y = pos.Y;
                trigger.AbsOrigin.Z = pos.Z;

                trigger.Collision.CapsuleRadius = radius;
                trigger.Collision.BoundingRadius = radius;
                trigger.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_TRIGGER;
                trigger.Collision.EnablePhysics = 1;
                trigger.Collision.TriggerBloat = 0;
                trigger.Collision.SurroundType = SurroundingBoundsType_t.USE_OBB_COLLISION_BOUNDS;
                trigger.Collision.CollisionAttribute.CollisionFunctionMask = 39;
                trigger.Collision.CollisionAttribute.CollisionGroup = 2;

                trigger.DispatchSpawn();
                RegisterEntity(trigger.Index, playerIndex, "trigger_multiple");
                return trigger;
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[EntityManager] CreateTrackedTrigger: {ex.Message}");
                return null;
            }
        }

        public static CBeam? CreateTrackedBeam(uint playerIndex, Vector start, Vector end, Color color)
        {
            try
            {
                var beam = Utilities.CreateEntityByName<CBeam>("beam");
                if (beam == null || !beam.IsValid) return null;

                beam.Render = color;
                beam.Width = 2.0f;
                beam.EndWidth = 2.0f;
                beam.Teleport(start);

                beam.EndPos.X = end.X;
                beam.EndPos.Y = end.Y;
                beam.EndPos.Z = end.Z;

                beam.DispatchSpawn();
                RegisterEntity(beam.Index, playerIndex, "beam");
                return beam;
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[EntityManager] CreateTrackedBeam: {ex.Message}");
                return null;
            }
        }

        // Dying entities stay out of transmit until the engine processes the kill (Event.CheckTransmit).
        private static readonly ConcurrentDictionary<uint, DateTime> recentlyDestroyed = new();

        public static List<uint> GetRecentlyDestroyedSnapshot()
        {
            if (recentlyDestroyed.IsEmpty) return [];

            var now = DateTime.UtcNow;
            var result = new List<uint>();
            foreach (var kvp in recentlyDestroyed)
            {
                if (now > kvp.Value) recentlyDestroyed.TryRemove(kvp.Key, out _);
                else result.Add(kvp.Key);
            }
            return result;
        }

        public static bool SuppressKills = false;

        public static bool DestroyEntity(uint entityIndex, float delay = 0.1f)
        {
            trackedEntities.TryRemove(entityIndex, out _);

            if (SuppressKills)
                return false;

            try
            {
                var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)entityIndex);
                if (entity != null && entity.IsValid)
                {
                    recentlyDestroyed[entityIndex] = DateTime.UtcNow.AddSeconds(delay + 2.0);
                    // Detach first so no follower is left on a freed parent.
                    entity.AcceptInput("ClearParent");
                    entity.AddEntityIOEvent("Kill", entity, delay: delay);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[EntityManager] DestroyEntity {entityIndex}: {ex.Message}");
            }

            return false;
        }

        public static bool DestroyEntity<T>(uint? entityIndex) where T : CBaseEntity
        {
            if (entityIndex == null) return false;
            return DestroyEntity(entityIndex.Value);
        }

        public static void DestroyPlayerEntities(uint playerIndex)
        {
            // Children die first (reverse creation order), same frame.
            var ordered = trackedEntities
                .Where(kvp => kvp.Value.PlayerIndex == playerIndex)
                .OrderByDescending(kvp => kvp.Value.CreatedAt)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var entityIndex in ordered)
                DestroyEntity(entityIndex);
        }

        public static void DestroyAllTracked()
        {
            // Stagger between owners; each owner's chain dies child-first in one frame.
            int group = 0;
            foreach (var owner in trackedEntities.Values.Select(e => e.PlayerIndex).Distinct().ToList())
            {
                float delay = 0.1f + (group++ % 16) * 0.03f;
                var ordered = trackedEntities
                    .Where(kvp => kvp.Value.PlayerIndex == owner)
                    .OrderByDescending(kvp => kvp.Value.CreatedAt)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var entityIndex in ordered)
                    DestroyEntity(entityIndex, delay);
            }

            trackedEntities.Clear();
        }

        public static void Clear()
        {
            DestroyAllTracked();
        }

        private static void ScheduleAutoDestroy(uint entityIndex, float? seconds)
        {
            if (seconds is not > 0) return;
            Instance?.AddTimer(seconds.Value, () => DestroyEntity(entityIndex), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }
    }
}
