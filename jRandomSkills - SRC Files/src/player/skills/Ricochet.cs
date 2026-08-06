using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using RayTraceAPI;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class Ricochet : ISkill
    {
        private const Skills skillName = Skills.Ricochet;

        private static readonly ConcurrentDictionary<uint, ImpactBudget> impactBudgets = [];
        private static readonly HashSet<uint> ownedTracers = [];
        private static readonly List<uint> tracerPool = [];
        private static readonly List<Flight> flights = [];
        private static readonly object flightLock = new();
        private static readonly Color tracerColor = Color.FromArgb(170, 255, 226, 140);
        private static readonly ulong worldOnlyMask = (ulong)InteractionLayers.MASK_WORLD_ONLY;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            impactBudgets.Clear();
            ResetPool(false);
        }

        public static void RoundEnd()
        {
            ResetPool(true);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            impactBudgets.TryRemove(playerIndex, out _);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            impactBudgets.TryRemove(player.Index, out _);
        }

        public static void BulletImpact(EventBulletImpact @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            if (PlayerManager.GetPlayerByIndex(player.Index)?.Skill != skillName) return;

            if (!RayTrace.IsAvailable)
            {
                Trace("RayTrace module unavailable");
                return;
            }

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            {
                Trace("shooter pawn invalid or dead");
                return;
            }

            if (!TryConsumeBudget(player.Index)) return;

            Vector3 eyePos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            Vector3 impact = new(@event.X, @event.Y, @event.Z);

            Vector3 toImpact = impact - eyePos;
            if (toImpact.LengthSquared() < 1f)
            {
                Trace("impact too close to eye");
                return;
            }

            Vector3 direction = Vector3.Normalize(toImpact);

            var surface = RayTrace.TraceShape(player, ToVector(eyePos), ToVector(impact + direction * 8f), worldOnlyMask, 0);
            if (surface == null)
            {
                Trace("surface trace returned null");
                return;
            }

            if (!surface.Value.DidHit)
            {
                Trace($"surface trace missed (fraction={surface.Value.Fraction:0.###})");
                return;
            }

            if (!TryResolveNormal(player, surface.Value, direction, out Vector3 normal))
            {
                Trace("could not resolve surface normal");
                return;
            }

            Bounce(player, pawn, surface.Value.EndPos, direction, normal);
        }

        private static void Bounce(CCSPlayerController player, CCSPlayerPawn pawn, Vector3 point, Vector3 direction, Vector3 normal)
        {
            int bounces = Math.Max(SkillsInfo.GetValue<int>(skillName, "bounces"), 1);
            float segmentDistance = SkillsInfo.GetValue<float>(skillName, "segmentDistance");
            float damageFalloff = SkillsInfo.GetValue<float>(skillName, "damageFalloff");

            var vdata = GetWeaponVData(pawn);
            float damage = GetBaseDamage(vdata);

            KillfeedIcons? killfeedIcon = KillfeedIconsExtensions.FromWeaponName(SkillUtils.GetDesignerName(pawn.WeaponServices?.ActiveWeapon?.Value));

            List<Vector3> path = [];

            for (int i = 0; i < bounces; i++)
            {
                direction = Vector3.Reflect(direction, normal);

                Vector3 start = point + normal * 2f;
                if (path.Count == 0) path.Add(start);

                Vector3 end = start + direction * segmentDistance;

                var hit = RayTrace.TraceShape(player, ToVector(start), ToVector(end));
                if (hit == null)
                {
                    Trace($"bounce {i + 1}: segment trace returned null");
                    break;
                }

                Vector3 stop = hit.Value.DidHit ? hit.Value.EndPos : end;
                path.Add(stop);

                if (!hit.Value.DidHit)
                {
                    Trace($"bounce {i + 1}: no surface, len={Vector3.Distance(start, stop):0.#}");
                    break;
                }

                if (hit.Value.HitPlayer(out var victim))
                {
                    DealDamage(player, victim, damage, vdata, killfeedIcon);
                    break;
                }

                if (!TryResolveNormal(player, hit.Value, direction, out normal))
                {
                    Trace($"bounce {i + 1}: could not resolve normal");
                    break;
                }

                point = hit.Value.EndPos;
                damage *= damageFalloff;
            }

            StartFlight(player, path);
        }

        public static void OnTick()
        {
            lock (flightLock)
            {
                if (flights.Count == 0) return;

                float speed = MathF.Max(SkillsInfo.GetValue<float>(skillName, "tracerSpeed"), 100f);
                float trail = MathF.Max(SkillsInfo.GetValue<float>(skillName, "tracerLength"), 16f);
                float step = speed / 64f;
                int tick = Server.TickCount;

                for (int i = flights.Count - 1; i >= 0; i--)
                {
                    var flight = flights[i];
                    flight.Travelled += step;

                    if (flight.Travelled - trail >= flight.TotalLength || tick >= flight.DeadlineTick)
                    {
                        ReleaseTracer(flight);
                        flights.RemoveAt(i);
                        continue;
                    }

                    float head = MathF.Min(flight.Travelled, flight.TotalLength);
                    int segment = SegmentAt(flight, head);
                    float tail = MathF.Max(head - trail, flight.Cumulative[segment]);

                    UpdateTracer(flight, PointAt(flight, tail), PointAt(flight, head));
                }
            }
        }

        private static void StartFlight(CCSPlayerController player, List<Vector3> path)
        {
            if (path.Count < 2) return;
            if (!SkillsInfo.GetValue<bool>(skillName, "showTracer")) return;
            if (EntityManager.OverBudget()) return;

            var points = path.ToArray();
            var cumulative = new float[points.Length];

            for (int i = 1; i < points.Length; i++)
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);

            float total = cumulative[^1];
            if (total < 1f) return;

            float speed = MathF.Max(SkillsInfo.GetValue<float>(skillName, "tracerSpeed"), 100f);

            lock (flightLock)
            {
                if (flights.Count >= Math.Max(SkillsInfo.GetValue<int>(skillName, "maxActiveTracers"), 1)) return;

                flights.Add(new Flight
                {
                    OwnerIndex = player.Index,
                    Points = points,
                    Cumulative = cumulative,
                    TotalLength = total,
                    DeadlineTick = Server.TickCount + (int)(total / speed * 64f) + 128,
                });
            }
        }

        private static int SegmentAt(Flight flight, float distance)
        {
            for (int i = flight.Points.Length - 2; i >= 0; i--)
                if (distance >= flight.Cumulative[i]) return i;

            return 0;
        }

        private static Vector3 PointAt(Flight flight, float distance)
        {
            int i = SegmentAt(flight, distance);

            float segmentStart = flight.Cumulative[i];
            float segmentLength = flight.Cumulative[i + 1] - segmentStart;
            if (segmentLength <= .0001f) return flight.Points[i];

            float t = Math.Clamp((distance - segmentStart) / segmentLength, 0f, 1f);
            return Vector3.Lerp(flight.Points[i], flight.Points[i + 1], t);
        }

        private static void UpdateTracer(Flight flight, Vector3 tail, Vector3 head)
        {
            CBeam? beam = null;

            if (flight.BeamIndex != null)
            {
                beam = ResolveTracer(flight.BeamIndex.Value);
                if (beam == null)
                {
                    ownedTracers.Remove(flight.BeamIndex.Value);
                    flight.BeamIndex = null;
                }
            }

            beam ??= AcquireTracer(flight);
            if (beam == null) return;

            beam.Teleport(ToVector(tail));

            beam.EndPos.X = head.X;
            beam.EndPos.Y = head.Y;
            beam.EndPos.Z = head.Z;

            Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
        }

        private static CBeam? AcquireTracer(Flight flight)
        {
            float width = SkillsInfo.GetValue<float>(skillName, "tracerWidth");

            while (tracerPool.Count > 0)
            {
                uint pooledIndex = tracerPool[^1];
                tracerPool.RemoveAt(tracerPool.Count - 1);

                var pooled = ResolveTracer(pooledIndex);
                if (pooled == null)
                {
                    ownedTracers.Remove(pooledIndex);
                    continue;
                }

                pooled.Width = width;
                pooled.EndWidth = width;
                pooled.Render = tracerColor;

                Utilities.SetStateChanged(pooled, "CBeam", "m_fWidth");
                Utilities.SetStateChanged(pooled, "CBeam", "m_fEndWidth");
                Utilities.SetStateChanged(pooled, "CBaseModelEntity", "m_clrRender");

                flight.BeamIndex = pooledIndex;
                return pooled;
            }

            if (ownedTracers.Count >= Math.Max(SkillsInfo.GetValue<int>(skillName, "maxActiveTracers"), 1)) return null;
            if (EntityManager.OverBudget()) return null;

            var beam = CreateTracerBeam(flight.Points[0], width);
            if (beam == null) return null;

            ownedTracers.Add(beam.Index);
            flight.BeamIndex = beam.Index;
            return beam;
        }

        private static CBeam? CreateTracerBeam(Vector3 at, float width)
        {
            try
            {
                var beam = Utilities.CreateEntityByName<CBeam>("beam");
                if (beam == null || !beam.IsValid) return null;

                beam.Render = tracerColor;
                beam.Width = width;
                beam.EndWidth = width;

                beam.Teleport(ToVector(at));

                beam.EndPos.X = at.X;
                beam.EndPos.Y = at.Y;
                beam.EndPos.Z = at.Z;

                beam.DispatchSpawn();
                return beam;
            }
            catch (Exception ex)
            {
                Trace($"beam create failed: {ex.Message}");
                return null;
            }
        }

        private static void ReleaseTracer(Flight flight)
        {
            if (flight.BeamIndex == null) return;

            uint beamIndex = flight.BeamIndex.Value;
            flight.BeamIndex = null;

            if (!ownedTracers.Contains(beamIndex)) return;

            var beam = ResolveTracer(beamIndex);
            if (beam == null)
            {
                ownedTracers.Remove(beamIndex);
                Trace($"tracer {beamIndex} vanished before release");
                return;
            }

            HideTracer(beam);
            tracerPool.Add(beamIndex);
        }

        private static void HideTracer(CBeam beam)
        {
            beam.Width = 0;
            beam.EndWidth = 0;
            beam.Render = Color.FromArgb(0, 0, 0, 0);

            Utilities.SetStateChanged(beam, "CBeam", "m_fWidth");
            Utilities.SetStateChanged(beam, "CBeam", "m_fEndWidth");
            Utilities.SetStateChanged(beam, "CBaseModelEntity", "m_clrRender");

            if (beam.AbsOrigin == null) return;

            beam.EndPos.X = beam.AbsOrigin.X;
            beam.EndPos.Y = beam.AbsOrigin.Y;
            beam.EndPos.Z = beam.AbsOrigin.Z;

            Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
        }

        private static void ResetPool(bool hideEntities)
        {
            lock (flightLock)
            {
                foreach (var flight in flights)
                    flight.BeamIndex = null;

                flights.Clear();

                if (hideEntities)
                    foreach (uint beamIndex in ownedTracers)
                    {
                        var beam = ResolveTracer(beamIndex);
                        if (beam != null) HideTracer(beam);
                    }

                if (ownedTracers.Count > 0)
                    Trace($"pool reset: {ownedTracers.Count} tracer(s) released");

                tracerPool.Clear();
                ownedTracers.Clear();
            }
        }

        private static CBeam? ResolveTracer(uint beamIndex)
        {
            var beam = Utilities.GetEntityFromIndex<CBeam>((int)beamIndex);
            if (beam == null || !beam.IsValid) return null;

            return beam.DesignerName == "beam" ? beam : null;
        }

        private static CCSWeaponBaseVData? GetWeaponVData(CCSPlayerPawn pawn)
        {
            var activeWeapon = pawn.WeaponServices?.ActiveWeapon?.Value;
            if (activeWeapon == null || !activeWeapon.IsValid) return null;

            try
            {
                return activeWeapon.As<CCSWeaponBase>().GetVData<CCSWeaponBaseVData>();
            }
            catch
            {
                return null;
            }
        }

        private static float GetBaseDamage(CCSWeaponBaseVData? vdata)
        {
            float fallback = SkillsInfo.GetValue<float>(skillName, "fallbackDamage");
            if (vdata == null) return fallback;

            float weaponDamage = vdata.Damage;
            if (weaponDamage <= 0) return fallback;

            return weaponDamage * SkillsInfo.GetValue<float>(skillName, "damageMultiplier");
        }

        private static void DealDamage(CCSPlayerController attacker, CCSPlayerController? victim, float damage, CCSWeaponBaseVData? vdata, KillfeedIcons? killfeedIcon)
        {
            if (victim == null || !victim.IsValid) return;

            var victimEvent = PlayerManager.GetPlayerEvent(victim);
            if (victimEvent == null || !victimEvent.IsValid) return;
            if (victimEvent.Index == attacker.Index || victimEvent.Team == attacker.Team) return;

            var victimPawn = victimEvent.PlayerPawn?.Value;
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            if (vdata != null && victimEvent.PawnArmor > 0 && SkillsInfo.GetValue<bool>(skillName, "respectArmor"))
                damage *= vdata.ArmorRatio * .5f;

            int finalDamage = (int)MathF.Round(damage);
            if (finalDamage <= 0) return;

            Trace($"hit {victimEvent.PlayerName} for {finalDamage}");
            SkillUtils.TakeHealth(victimPawn, finalDamage, attacker, killfeedIcon ?? KillfeedIcons.Leg);
        }

        private static bool TryResolveNormal(CCSPlayerController player, CustomTraceResult result, Vector3 direction, out Vector3 normal)
        {
            normal = Vector3.Zero;

            if (!TryRoughNormal(player, result, direction, out Vector3 rough)) return false;

            if (TryRefineNormal(player, result.EndPos, direction, rough, out normal)) return true;

            Trace("surface refine failed, using rough normal");
            normal = rough;
            return true;
        }

        private static bool TryRoughNormal(CCSPlayerController player, CustomTraceResult result, Vector3 direction, out Vector3 rough)
        {
            rough = result.Normal;
            if (rough.LengthSquared() > .0001f)
            {
                rough = Vector3.Normalize(rough);
                if (Vector3.Dot(direction, rough) < -.05f) return true;
            }

            return TryPlaneFitNormal(player, result.EndPos, direction, out rough);
        }

        private static bool TryRefineNormal(CCSPlayerController player, Vector3 baseHit, Vector3 direction, Vector3 rough, out Vector3 normal)
        {
            normal = Vector3.Zero;

            Vector3 axis = MathF.Abs(rough.Z) > .9f ? Vector3.UnitX : Vector3.UnitZ;
            Vector3 first = Vector3.Cross(rough, axis);
            if (first.LengthSquared() < .0001f) return false;
            first = Vector3.Normalize(first);

            Vector3 second = Vector3.Normalize(Vector3.Cross(rough, first));

            float offset = MathF.Max(SkillsInfo.GetValue<float>(skillName, "normalProbeOffset"), 1f);
            float tolerance = offset * 2.5f;

            Span<Vector3> ring = stackalloc Vector3[4];
            int count = 0;

            if (SampleSurface(player, baseHit, rough, first * offset, tolerance, out Vector3 firstPlus)) ring[count++] = firstPlus;
            if (SampleSurface(player, baseHit, rough, second * offset, tolerance, out Vector3 secondPlus)) ring[count++] = secondPlus;
            if (SampleSurface(player, baseHit, rough, first * -offset, tolerance, out Vector3 firstMinus)) ring[count++] = firstMinus;
            if (SampleSurface(player, baseHit, rough, second * -offset, tolerance, out Vector3 secondMinus)) ring[count++] = secondMinus;

            if (count == 2 && SampleSurface(player, baseHit, rough, Vector3.Zero, tolerance, out Vector3 center))
                ring[count++] = center;

            if (count < 3) return false;

            normal = NewellNormal(ring[..count]);
            if (normal.LengthSquared() < .0001f) return false;

            normal = Vector3.Normalize(normal);
            float dot = Vector3.Dot(direction, normal);

            if (MathF.Abs(dot) < .05f) return false;
            if (dot > 0f) normal = -normal;

            return true;
        }

        private static bool SampleSurface(CCSPlayerController player, Vector3 baseHit, Vector3 rough, Vector3 tangentOffset, float tolerance, out Vector3 point)
        {
            point = Vector3.Zero;

            Vector3 anchor = baseHit + tangentOffset;
            Vector3 start = anchor + rough * 16f;
            Vector3 end = anchor - rough * 48f;

            var sample = RayTrace.TraceShape(player, ToVector(start), ToVector(end), worldOnlyMask, 0);
            if (sample == null || !sample.Value.DidHit) return false;

            point = sample.Value.EndPos;
            return MathF.Abs(Vector3.Dot(point - baseHit, rough)) <= tolerance;
        }

        private static Vector3 NewellNormal(ReadOnlySpan<Vector3> points)
        {
            Vector3 normal = Vector3.Zero;

            for (int i = 0; i < points.Length; i++)
            {
                Vector3 current = points[i];
                Vector3 next = points[(i + 1) % points.Length];

                normal.X += (current.Y - next.Y) * (current.Z + next.Z);
                normal.Y += (current.Z - next.Z) * (current.X + next.X);
                normal.Z += (current.X - next.X) * (current.Y + next.Y);
            }

            return normal;
        }

        private static bool TryPlaneFitNormal(CCSPlayerController player, Vector3 baseHit, Vector3 direction, out Vector3 normal)
        {
            normal = Vector3.Zero;

            Vector3 axis = MathF.Abs(direction.Z) > .9f ? Vector3.UnitX : Vector3.UnitZ;
            Vector3 right = Vector3.Cross(direction, axis);
            if (right.LengthSquared() < .0001f) return false;
            right = Vector3.Normalize(right);

            Vector3 up = Vector3.Normalize(Vector3.Cross(right, direction));

            float offset = MathF.Max(SkillsInfo.GetValue<float>(skillName, "normalProbeOffset"), 1f);
            float tolerance = offset * 6f;
            Vector3 origin = baseHit - direction * 24f;

            if (!Probe(player, origin, direction, right * offset, baseHit, tolerance, out Vector3 rightHit)) return false;
            if (!Probe(player, origin, direction, right * -offset, baseHit, tolerance, out Vector3 leftHit)) return false;
            if (!Probe(player, origin, direction, up * offset, baseHit, tolerance, out Vector3 upHit)) return false;
            if (!Probe(player, origin, direction, up * -offset, baseHit, tolerance, out Vector3 downHit)) return false;

            normal = Vector3.Cross(rightHit - leftHit, upHit - downHit);
            if (normal.LengthSquared() < .0001f) return false;

            normal = Vector3.Normalize(normal);
            float dot = Vector3.Dot(direction, normal);

            if (MathF.Abs(dot) < .05f) return false;
            if (dot > 0f) normal = -normal;

            return true;
        }

        private static bool Probe(CCSPlayerController player, Vector3 origin, Vector3 direction, Vector3 offset, Vector3 baseHit, float tolerance, out Vector3 point)
        {
            point = Vector3.Zero;

            Vector3 start = origin + offset;
            Vector3 end = start + direction * 96f;

            var probe = RayTrace.TraceShape(player, ToVector(start), ToVector(end), worldOnlyMask, 0);
            if (probe == null || !probe.Value.DidHit) return false;

            point = probe.Value.EndPos;
            return Vector3.Distance(point, baseHit) <= tolerance;
        }

        private static void Trace(string message)
        {
            Debug.WriteToDebug($"[Ricochet] {message}");
        }

        private static bool TryConsumeBudget(uint playerIndex)
        {
            int tick = Server.TickCount;
            var budget = impactBudgets.GetOrAdd(playerIndex, _ => new ImpactBudget());

            if (budget.Tick != tick)
            {
                budget.Tick = tick;
                budget.Count = 0;
            }

            if (budget.Count >= Math.Max(SkillsInfo.GetValue<int>(skillName, "maxImpactsPerTick"), 1)) return false;

            budget.Count++;
            return true;
        }

        private static Vector ToVector(Vector3 vector) => new(vector.X, vector.Y, vector.Z);

        private class ImpactBudget
        {
            public int Tick { get; set; } = int.MinValue;
            public int Count { get; set; }
        }

        private class Flight
        {
            public required uint OwnerIndex { get; set; }
            public required Vector3[] Points { get; set; }
            public required float[] Cumulative { get; set; }
            public required float TotalLength { get; set; }
            public required int DeadlineTick { get; set; }
            public float Travelled { get; set; }
            public uint? BeamIndex { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffd75e", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Rare, int bounces = 3, float segmentDistance = 1200f, float damageMultiplier = .5f, float fallbackDamage = 25f, bool respectArmor = true, float damageFalloff = .75f, int maxImpactsPerTick = 2, bool showTracer = true, float tracerWidth = 0.5f, float tracerSpeed = 2600f, float tracerLength = 220f, int maxActiveTracers = 12, float normalProbeOffset = 6f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int Bounces { get; set; } = bounces;
            public float SegmentDistance { get; set; } = segmentDistance;
            public float DamageMultiplier { get; set; } = damageMultiplier;
            public float FallbackDamage { get; set; } = fallbackDamage;
            public bool RespectArmor { get; set; } = respectArmor;
            public float DamageFalloff { get; set; } = damageFalloff;
            public int MaxImpactsPerTick { get; set; } = maxImpactsPerTick;
            public bool ShowTracer { get; set; } = showTracer;
            public float TracerWidth { get; set; } = tracerWidth;
            public float TracerSpeed { get; set; } = tracerSpeed;
            public float TracerLength { get; set; } = tracerLength;
            public int MaxActiveTracers { get; set; } = maxActiveTracers;
            public float NormalProbeOffset { get; set; } = normalProbeOffset;
        }
    }
}
