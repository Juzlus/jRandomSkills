using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using src.player;
using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using static src.jRandomSkills;

namespace src.utils
{
    public static class EntityManager
    {
        public const uint SystemOwnerIndex = uint.MaxValue;
        private static readonly ConcurrentDictionary<uint, EntityData> trackedEntities = [];
        private static readonly ConcurrentDictionary<uint, uint> knownHandles = [];
        private static readonly ConcurrentDictionary<uint, List<uint>> byOwner = [];

        private static void AddOwned(uint ownerKey, uint entityIndex)
        {
            var list = byOwner.GetOrAdd(ownerKey, _ => []);
            if (!list.Contains(entityIndex)) list.Add(entityIndex);
        }

        private static void RemoveOwned(uint ownerKey, uint entityIndex)
        {
            if (!byOwner.TryGetValue(ownerKey, out var list)) return;
            list.Remove(entityIndex);
            if (list.Count == 0) byOwner.TryRemove(ownerKey, out _);
        }

        private struct EntityData
        {
            public uint EntityIndex;
            public uint OwnerKey;
            public uint HandleRaw;
            public string EntityType;
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

        public static void RegisterEntity(uint entityIndex, uint ownerKey, string entityType)
        {
            if (entityIndex == 0) return;

            bool alreadyTracked = trackedEntities.TryGetValue(entityIndex, out var previous);

            uint handleRaw = 0;
            try { handleRaw = Utilities.GetEntityFromIndex<CBaseEntity>((int)entityIndex)?.EntityHandle.Raw ?? 0; }
            catch { }
            if (handleRaw != 0) knownHandles[entityIndex] = handleRaw;

            if (alreadyTracked && previous.OwnerKey != ownerKey)
                RemoveOwned(previous.OwnerKey, entityIndex);
            AddOwned(ownerKey, entityIndex);

            trackedEntities[entityIndex] = new EntityData
            {
                EntityIndex = entityIndex,
                OwnerKey = ownerKey,
                HandleRaw = handleRaw,
                EntityType = entityType
            };

            if (!Config.DebugEnabled(DebugCategory.Entity)) return;

            if (!alreadyTracked)
                Debug.WriteToDebug($"[Entity] + {entityType} #{entityIndex} owner={DescribeOwner(ownerKey)} tracked={trackedEntities.Count}", DebugCategory.Entity);
            else if (previous.EntityType != entityType)
                Debug.WriteToDebug($"[Entity] ~ {previous.EntityType} -> {entityType} #{entityIndex} owner={DescribeOwner(ownerKey)} tracked={trackedEntities.Count}", DebugCategory.Entity);
        }

        private static string DescribeOwner(uint ownerKey)
        {
            return ownerKey == SystemOwnerIndex ? "system" : ownerKey.ToString();
        }

        private static void LogEntityError(string message)
        {
            Server.PrintToConsole($"[EntityManager] {message}");
            Debug.WriteToDebug($"[Entity] ! {message}", DebugCategory.Entity);
        }

        public static void RegisterExisting(CBaseEntity? entity, uint ownerKey, string entityType)
        {
            if (entity == null || !entity.IsValid) return;
            RegisterEntity(entity.Index, ownerKey, entityType);
        }

        public static List<uint> GetPlayerEntities(uint ownerKey, string? entityType = null)
        {
            if (!byOwner.TryGetValue(ownerKey, out var owned)) return [];

            List<uint> result = new(owned.Count);
            foreach (var entityIndex in owned)
                if (string.IsNullOrEmpty(entityType) || (trackedEntities.TryGetValue(entityIndex, out var data) && data.EntityType == entityType))
                    result.Add(entityIndex);
            return result;
        }

        public static int GetTrackedCount(uint ownerKey) => GetPlayerEntities(ownerKey).Count;

        public static uint? GetEntityOwner(uint entityIndex)
        {
            if (!trackedEntities.TryGetValue(entityIndex, out var data)) return null;
            return data.OwnerKey == SystemOwnerIndex ? null : data.OwnerKey;
        }

        public static (int totalTracked, int ownerCount) GetStatistics()
        {
            return (trackedEntities.Count, trackedEntities.Values.Select(e => e.OwnerKey).Distinct().Count());
        }

        public static string DescribeTracked(int topTypes = 4)
        {
            var byType = new Dictionary<string, int>();
            foreach (var data in trackedEntities.Values)
            {
                string type = string.IsNullOrEmpty(data.EntityType) ? "?" : data.EntityType;
                byType[type] = byType.TryGetValue(type, out int count) ? count + 1 : 1;
            }

            if (byType.Count == 0) return string.Empty;

            var parts = byType.OrderByDescending(kvp => kvp.Value).Take(topTypes).Select(kvp => $"{kvp.Key}:{kvp.Value}");
            return " types=" + string.Join(",", parts);
        }


        public static CParticleSystem? CreateTrackedParticleSystem(uint ownerKey, string particleName, float? autoDestroySeconds = null)
        {
            try
            {
                if (OverBudget()) return null;
                var particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
                if (particle == null || !particle.IsValid) return null;

                particle.EffectName = particleName;
                particle.StartActive = true;
                particle.DispatchSpawn();

                RegisterEntity(particle.Index, ownerKey, "particle_system");
                ScheduleAutoDestroy(particle.Index, autoDestroySeconds);
                return particle;
            }
            catch (Exception ex)
            {
                LogEntityError($"CreateTrackedParticleSystem: {ex.Message}");
                return null;
            }
        }

        public static CParticleSystem? CreateTracer(uint ownerKey, string particleName, Vector start, Vector end, float lifetime = 1f)
        {
            try
            {
                if (OverBudget()) return null;
                var particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
                if (particle == null || !particle.IsValid) return null;

                particle.EffectName = particleName;
                particle.StartActive = true;
                particle.Teleport(start);

                var controlPoint = new Vector(particle.Handle + Schema.GetSchemaOffset("CParticleSystem", "m_vServerControlPoints"));
                controlPoint.X = end.X;
                controlPoint.Y = end.Y;
                controlPoint.Z = end.Z;
                particle.ServerControlPointAssignments[0] = 1;

                particle.DispatchSpawn();

                RegisterEntity(particle.Index, ownerKey, "tracer");
                ScheduleAutoDestroy(particle.Index, lifetime);
                return particle;
            }
            catch (Exception ex)
            {
                LogEntityError($"CreateTracer: {ex.Message}");
                return null;
            }
        }

        public static CParticleSystem? CreateEntityParticle(uint ownerKey, string particleName, CBaseEntity target, float lifetime = 5f)
        {
            try
            {
                if (OverBudget() || target.AbsOrigin == null) return null;
                var particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
                if (particle == null || !particle.IsValid) return null;

                particle.EffectName = particleName;
                particle.StartActive = true;
                particle.Teleport(target.AbsOrigin);

                nint controlPointEnts = particle.Handle + Schema.GetSchemaOffset("CParticleSystem", "m_hControlPointEnts");
                Marshal.WriteInt32(controlPointEnts, (int)target.EntityHandle.Raw);
                Marshal.WriteInt32(controlPointEnts + 4, (int)target.EntityHandle.Raw);

                particle.DispatchSpawn();
                particle.AcceptInput("SetParent", target, particle, "!activator");

                RegisterEntity(particle.Index, ownerKey, "particle_system");
                ScheduleAutoDestroy(particle.Index, lifetime);
                return particle;
            }
            catch (Exception ex)
            {
                LogEntityError($"CreateEntityParticle: {ex.Message}");
                return null;
            }
        }

        public static CDynamicProp? CreateTrackedDynamicProp(uint ownerKey, string designerName = "prop_dynamic", string? trackAs = null)
        {
            try
            {
                if (OverBudget()) return null;
                var prop = Utilities.CreateEntityByName<CDynamicProp>(designerName);
                if (prop == null || !prop.IsValid) return null;

                RegisterEntity(prop.Index, ownerKey, trackAs ?? designerName);
                return prop;
            }
            catch (Exception ex)
            {
                LogEntityError($"CreateTrackedDynamicProp: {ex.Message}");
                return null;
            }
        }

        public static CDynamicProp? CreateTrackedPropOverride(uint ownerKey)
        {
            return CreateTrackedDynamicProp(ownerKey, "prop_dynamic_override");
        }

        public static CChicken? CreateTrackedChicken(uint ownerKey)
        {
            try
            {
                if (OverBudget()) return null;
                var chicken = Utilities.CreateEntityByName<CChicken>("chicken");
                if (chicken == null || !chicken.IsValid) return null;

                chicken.DispatchSpawn();
                RegisterEntity(chicken.Index, ownerKey, "chicken");
                return chicken;
            }
            catch (Exception ex)
            {
                LogEntityError($"CreateTrackedChicken: {ex.Message}");
                return null;
            }
        }

        public static CPhysicsPropMultiplayer? CreateTrackedPhysicsProp(uint ownerKey)
        {
            try
            {
                if (OverBudget()) return null;
                var prop = Utilities.CreateEntityByName<CPhysicsPropMultiplayer>("prop_physics_multiplayer");
                if (prop == null || !prop.IsValid) return null;

                RegisterEntity(prop.Index, ownerKey, "prop_physics_multiplayer");
                return prop;
            }
            catch (Exception ex)
            {
                LogEntityError($"CreateTrackedPhysicsProp: {ex.Message}");
                return null;
            }
        }

        public static CTriggerMultiple? CreateTrackedTrigger(uint ownerKey, string name, float radius, Vector pos)
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
                trigger.Entity!.Name = $"{name}_{trigger.Index}";
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
                RegisterEntity(trigger.Index, ownerKey, "trigger_multiple");
                return trigger;
            }
            catch (Exception ex)
            {
                LogEntityError($"CreateTrackedTrigger: {ex.Message}");
                return null;
            }
        }

        public static CBeam? CreateTrackedBeam(uint ownerKey, Vector start, Vector end, Color color)
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
                RegisterEntity(beam.Index, ownerKey, "beam");
                return beam;
            }
            catch (Exception ex)
            {
                LogEntityError($"CreateTrackedBeam: {ex.Message}");
                return null;
            }
        }

        // Dying entities stay out of transmit until the engine processes the kill (Event.CheckTransmit).
        private readonly record struct DyingEntity(DateTime Expires, uint HandleRaw);
        private static readonly ConcurrentDictionary<uint, DyingEntity> recentlyDestroyed = new();

        private static readonly List<uint> noDyingEntities = [];

        public static List<uint> GetRecentlyDestroyedSnapshot()
        {
            if (recentlyDestroyed.IsEmpty) return noDyingEntities;

            var now = DateTime.UtcNow;
            var result = new List<uint>();
            foreach (var kvp in recentlyDestroyed)
            {
                if (now > kvp.Value.Expires)
                {
                    recentlyDestroyed.TryRemove(kvp.Key, out _);
                    continue;
                }

                var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)kvp.Key);
                if (entity == null || !entity.IsValid || entity.EntityHandle.Raw != kvp.Value.HandleRaw)
                {
                    recentlyDestroyed.TryRemove(kvp.Key, out _);
                    continue;
                }

                result.Add(kvp.Key);
            }
            return result;
        }

        public static bool SuppressKills = false;

        public static bool DestroyEntity(uint entityIndex, float delay = 0.1f, bool hideFromTransmit = true)
        {
            bool wasTracked = trackedEntities.TryRemove(entityIndex, out var removed);
            if (wasTracked) RemoveOwned(removed.OwnerKey, entityIndex);

            if (wasTracked && Config.DebugEnabled(DebugCategory.Entity))
                Debug.WriteToDebug($"[Entity] - {removed.EntityType} #{entityIndex} owner={DescribeOwner(removed.OwnerKey)} tracked={trackedEntities.Count}", DebugCategory.Entity);

            if (SuppressKills)
                return false;

            try
            {
                var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)entityIndex);
                if (entity != null && entity.IsValid)
                {
                    if (knownHandles.TryRemove(entityIndex, out var knownRaw) && knownRaw != entity.EntityHandle.Raw)
                    {
                        if (Config.DebugEnabled(DebugCategory.Entity))
                            Debug.WriteToDebug($"[Entity] skip kill #{entityIndex}: index now holds {entity.DesignerName}", DebugCategory.Entity);
                        return false;
                    }

                    if (hideFromTransmit)
                        recentlyDestroyed[entityIndex] = new DyingEntity(DateTime.UtcNow.AddSeconds(delay + 0.5), entity.EntityHandle.Raw);
                    // Detach first so no follower is left on a freed parent.
                    entity.AcceptInput("ClearParent");
                    entity.AddEntityIOEvent("Kill", entity, delay: delay);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogEntityError($"DestroyEntity {entityIndex}: {ex.Message}");
            }

            return false;
        }

        public static bool DestroyBeam(uint entityIndex)
        {
            try
            {
                var beam = Utilities.GetEntityFromIndex<CBeam>((int)entityIndex);
                if (beam != null && beam.IsValid)
                {
                    beam.Width = 0;
                    beam.EndWidth = 0;
                    beam.Render = Color.FromArgb(0, 0, 0, 0);

                    Utilities.SetStateChanged(beam, "CBeam", "m_fWidth");
                    Utilities.SetStateChanged(beam, "CBeam", "m_fEndWidth");
                    Utilities.SetStateChanged(beam, "CBaseModelEntity", "m_clrRender");
                }
            }
            catch (Exception ex)
            {
                LogEntityError($"DestroyBeam {entityIndex}: {ex.Message}");
            }

            uint beamRaw = 0;
            try { beamRaw = Utilities.GetEntityFromIndex<CBeam>((int)entityIndex)?.EntityHandle.Raw ?? 0; }
            catch { }

            bool killed = DestroyEntity(entityIndex, hideFromTransmit: false);

            if (killed && !SuppressKills)
                Server.NextFrame(() =>
                {
                    var leftover = Utilities.GetEntityFromIndex<CBeam>((int)entityIndex);
                    if (leftover != null && leftover.IsValid && leftover.EntityHandle.Raw == beamRaw)
                        leftover.Remove();
                });

            return killed;
        }

        public static bool DestroyEntity<T>(uint? entityIndex) where T : CBaseEntity
        {
            if (entityIndex == null) return false;
            return DestroyEntity(entityIndex.Value);
        }

        public static void DestroyPlayerEntities(uint ownerKey)
        {
            // Children die first (reverse creation order), same frame.
            if (!byOwner.TryGetValue(ownerKey, out var owned)) return;

            for (int i = owned.Count - 1; i >= 0; i--)
                if (i < owned.Count)
                    DestroyEntity(owned[i]);
        }

        public static void DestroyAllTracked()
        {
            // Stagger between owners; each owner's chain dies child-first in one frame.
            int group = 0;
            foreach (var owner in byOwner.Keys.ToList())
            {
                float delay = 0.1f + (group++ % 16) * 0.03f;
                if (!byOwner.TryGetValue(owner, out var owned)) continue;

                for (int i = owned.Count - 1; i >= 0; i--)
                    if (i < owned.Count)
                        DestroyEntity(owned[i], delay);
            }

            trackedEntities.Clear();
            byOwner.Clear();
        }

        public static void Clear()
        {
            DestroyAllTracked();
            knownHandles.Clear();
        }

        public static void ScheduleAutoDestroy(uint entityIndex, float? seconds)
        {
            if (seconds is not > 0) return;
            Instance?.AddTimer(seconds.Value, () => DestroyEntity(entityIndex), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }
    }
}
