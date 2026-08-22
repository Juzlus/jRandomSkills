using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using src.player;
using System.Collections.Concurrent;
using System.Drawing;
using System.Globalization;

namespace src.utils
{
    public static class DecoyRing
    {
        private const string discModel = "models/dev/grenade_trajectory/grenade_target.vmdl";

        private const float discBaseRadius = 13.462f;
        private const int discAlpha = 178;

        private const int outerSegments = 24;
        private const int innerSegments = 18;
        private const float ringHeight = 3f;

        private const float maxLifetime = 25f;

        private const int spawnFrames = 4;

        private static readonly ConcurrentDictionary<string, Ring> rings = [];

        private sealed class Ring
        {
            public uint? Disc { get; set; }
            public List<uint> Beams { get; } = [];
            public bool Cancelled { get; set; }
        }

        public static void PreloadAssets()
        {
            jRandomSkills.Instance.AddToManifest(discModel);
        }

        public static void Show(Skills skill, uint id, Vector center, float radius)
        {
            if (radius <= 0 || !Config.LoadedConfig.ShowDecoyRing) return;

            string key = MakeKey(skill, id);
            var ring = new Ring();
            if (!rings.TryAdd(key, ring)) return;

            SpawnDisc(center, radius, ring);

            if (!EntityManager.OverBudget())
            {
                var segments = new List<(Vector From, Vector To)>(outerSegments + innerSegments);
                CollectRing(center, radius, outerSegments, segments);
                CollectRing(center, radius * .5f, innerSegments, segments);

                SpawnBeamChunk(ring, segments, 0, GetSkillColor(skill));
            }

            jRandomSkills.Instance.AddTimer(maxLifetime, () => Hide(skill, id), TimerFlags.STOP_ON_MAPCHANGE);
        }

        public static void Hide(Skills skill, uint id)
        {
            if (!rings.TryRemove(MakeKey(skill, id), out var ring)) return;
            Destroy(ring);
        }

        public static void ClearAll(Skills skill)
        {
            string prefix = skill + "|";

            foreach (var key in rings.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!rings.TryRemove(key, out var ring)) continue;

                Destroy(ring);
            }
        }

        private static void Destroy(Ring ring)
        {
            ring.Cancelled = true;

            foreach (uint beamIndex in ring.Beams)
                EntityManager.DestroyBeam(beamIndex);

            ring.Beams.Clear();

            if (ring.Disc == null) return;

            uint discIndex = ring.Disc.Value;
            ring.Disc = null;

            EntityManager.DestroyEntity(discIndex, 0f);
        }

        private static string MakeKey(Skills skill, uint id)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{skill}|{id}");
        }

        private static Color GetSkillColor(Skills skill)
        {
            try
            {
                string hex = SkillsInfo.GetValue<string>(skill, "color");
                if (!string.IsNullOrEmpty(hex))
                    return ColorTranslator.FromHtml(hex);
            }
            catch { }

            return Color.White;
        }

        private static void SpawnDisc(Vector center, float radius, Ring ring)
        {
            var disc = EntityManager.CreateTrackedPropOverride(EntityManager.SystemOwnerIndex);
            if (disc == null || !disc.IsValid) return;

            uint discIndex = disc.Index;
            ring.Disc = discIndex;

            float scale = radius / discBaseRadius;
            Vector pos = new(center.X, center.Y, center.Z + 1f);

            Server.NextFrame(() =>
            {
                var prop = Utilities.GetEntityFromIndex<CDynamicProp>((int)discIndex);
                if (prop == null || !prop.IsValid) return;

                var node = prop.CBodyComponent?.SceneNode?.Owner?.Entity;
                if (node != null)
                    node.Flags = (uint)(node.Flags & ~(1 << 2));

                MakeNonSolid(prop);

                prop.SetModel(discModel);
                prop.Teleport(pos, new QAngle(0, 0, 0));
                prop.DispatchSpawn();

                prop.Render = Color.FromArgb(discAlpha, 255, 255, 255);
                Utilities.SetStateChanged(prop, "CBaseModelEntity", "m_clrRender");

                var skeleton = prop.CBodyComponent?.SceneNode?.GetSkeletonInstance();
                if (skeleton != null)
                {
                    skeleton.Scale = scale;
                    prop.AcceptInput("SetScale", null, null, scale.ToString(CultureInfo.InvariantCulture));
                }

                Utilities.SetStateChanged(prop, "CBaseEntity", "m_CBodyComponent");
            });
        }

        private static void MakeNonSolid(CDynamicProp prop)
        {
            var collision = prop.Collision;
            if (collision == null) return;

            collision.SolidType = SolidType_t.SOLID_NONE;
            collision.SolidFlags = 12;

            var attribute = collision.CollisionAttribute;
            if (attribute == null) return;

            attribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_NEVER;
            attribute.CollisionFunctionMask = 0;
            attribute.InteractsAs = 0;
            attribute.InteractsWith = 0;
            attribute.InteractsExclude = 0;
        }

        private static void CollectRing(Vector center, float radius, int segments, List<(Vector From, Vector To)> output)
        {
            if (segments < 3 || radius <= 0) return;

            float step = MathF.Tau / segments;
            Vector previous = PointOn(center, radius, 0);

            for (int i = 1; i <= segments; i++)
            {
                Vector next = PointOn(center, radius, step * i);
                output.Add((previous, next));
                previous = next;
            }
        }

        private static void SpawnBeamChunk(Ring ring, List<(Vector From, Vector To)> segments, int start, Color color)
        {
            if (ring.Cancelled || start >= segments.Count) return;

            int perFrame = (segments.Count + spawnFrames - 1) / spawnFrames;
            int end = Math.Min(start + perFrame, segments.Count);

            for (int i = start; i < end; i++)
            {
                var beam = EntityManager.CreateTrackedBeam(EntityManager.SystemOwnerIndex, segments[i].From, segments[i].To, color);
                if (beam == null || !beam.IsValid) continue;

                beam.Width = 1.2f;
                beam.EndWidth = 1.2f;

                Utilities.SetStateChanged(beam, "CBeam", "m_fWidth");
                Utilities.SetStateChanged(beam, "CBeam", "m_fEndWidth");

                ring.Beams.Add(beam.Index);
            }

            if (end >= segments.Count) return;

            Server.NextFrame(() => SpawnBeamChunk(ring, segments, end, color));
        }

        private static Vector PointOn(Vector center, float radius, float angle)
        {
            return new Vector(
                center.X + MathF.Cos(angle) * radius,
                center.Y + MathF.Sin(angle) * radius,
                center.Z + ringHeight);
        }
    }
}
