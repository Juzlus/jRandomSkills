using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using RayTraceAPI;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class Tripwire : ISkill
    {
        private const Skills skillName = Skills.Tripwire;

        private static readonly ConcurrentDictionary<uint, WireInfo> wires = [];
        private static readonly ConcurrentDictionary<uint, int> wireCount = [];
        private static readonly ConcurrentDictionary<(uint Owner, uint Target), (int Slot, int ExpiryTick)> revealed = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var wire in wires.Values)
                    DestroyWire(wire.BeamIndex);

                wires.Clear();
                wireCount.Clear();
                revealed.Clear();
            }
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            RemovePlayerWires(playerIndex);

            foreach (var key in revealed.Keys)
                if (key.Owner == playerIndex || key.Target == playerIndex)
                    revealed.TryRemove(key, out _);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            RemovePlayerWires(player.Index);

            foreach (var key in revealed.Keys)
                if (key.Owner == player.Index)
                    revealed.TryRemove(key, out _);
        }

        private static void RemovePlayerWires(uint ownerIndex)
        {
            foreach (var kvp in wires)
            {
                if (kvp.Value.OwnerIndex != ownerIndex) continue;

                DestroyWire(kvp.Key);
                wires.TryRemove(kvp.Key, out _);
            }

            wireCount.TryRemove(ownerIndex, out _);
        }

        private static void DestroyWire(uint beamIndex)
        {
            var beam = Utilities.GetEntityFromIndex<CBeam>((int)beamIndex);
            if (beam != null && beam.IsValid)
            {
                beam.Width = 0;
                beam.EndWidth = 0;
                beam.Render = Color.FromArgb(0, 0, 0, 0);

                Utilities.SetStateChanged(beam, "CBeam", "m_fWidth");
                Utilities.SetStateChanged(beam, "CBeam", "m_fEndWidth");
                Utilities.SetStateChanged(beam, "CBaseModelEntity", "m_clrRender");
            }

            EntityManager.DestroyEntity(beamIndex);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            int maxWires = SkillsInfo.GetValue<int>(skillName, "maxWires");
            if (wireCount.TryGetValue(player.Index, out int placed) && placed >= maxWires)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("tripwire_limit_info", maxWires));
                return;
            }

            if (!TryPlaceWire(player))
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("tripwire_no_wall_info"));
                return;
            }

            wireCount.AddOrUpdate(player.Index, 1, (_, v) => v + 1);
            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("tripwire_placed_info"));
        }

        private static bool TryPlaceWire(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return false;
            if (!RayTrace.IsAvailable) return false;

            float height = SkillsInfo.GetValue<float>(skillName, "wireHeight");
            float maxDistance = SkillsInfo.GetValue<float>(skillName, "maxWallDistance");

            Vector origin = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + height);
            float yaw = pawn.EyeAngles.Y;

            Vector rightDir = SkillUtils.GetForwardVector(new QAngle(0, yaw - 90, 0));
            Vector leftDir = SkillUtils.GetForwardVector(new QAngle(0, yaw + 90, 0));

            ulong wallMask = (ulong)InteractionLayers.MASK_WORLD_ONLY;

            var rightHit = RayTrace.TraceShape(player, origin, origin + rightDir * maxDistance, wallMask, 0);
            var leftHit = RayTrace.TraceShape(player, origin, origin + leftDir * maxDistance, wallMask, 0);

            if (rightHit == null || leftHit == null) return false;
            if (!rightHit.Value.DidHit || !leftHit.Value.DidHit) return false;
            if (rightHit.Value.HitPlayer(out _) || leftHit.Value.HitPlayer(out _)) return false;

            Vector start = new(rightHit.Value.EndPos.X, rightHit.Value.EndPos.Y, rightHit.Value.EndPos.Z);
            Vector end = new(leftHit.Value.EndPos.X, leftHit.Value.EndPos.Y, leftHit.Value.EndPos.Z);

            if (EntityManager.OverBudget()) return false;

            var beam = EntityManager.CreateTrackedBeam(player.Index, start, end, Color.FromArgb(255, 255, 40, 40));
            if (beam == null || !beam.IsValid) return false;

            beam.Width = SkillsInfo.GetValue<float>(skillName, "wireWidth");

            wires[beam.Index] = new WireInfo
            {
                OwnerIndex = player.Index,
                BeamIndex = beam.Index,
                Start = start,
                End = end,
            };

            return true;
        }

        public static void OnTick()
        {
            if (revealed.IsEmpty && wires.IsEmpty) return;

            int tick = Server.TickCount;

            foreach (var kvp in revealed)
            {
                if (kvp.Value.ExpiryTick <= tick)
                {
                    revealed.TryRemove(kvp.Key, out _);
                    continue;
                }

                RevealOnRadar(kvp.Key.Target, kvp.Value.Slot);
            }

            if (wires.IsEmpty || tick % 4 != 0) return;

            float radius = SkillsInfo.GetValue<float>(skillName, "triggerRadius");
            int revealTicks = (int)(SkillsInfo.GetValue<float>(skillName, "radarDuration") * 64);
            float bodyHeight = SkillsInfo.GetValue<float>(skillName, "wireHeight");

            foreach (var wire in wires.Values)
            {
                var owner = Utilities.GetPlayerFromIndex((int)wire.OwnerIndex);
                if (owner == null || !owner.IsValid) continue;

                foreach (var enemy in PlayerManager.GetTickPlayers())
                {
                    if (enemy == null || !enemy.IsValid || enemy.Team == owner.Team) continue;

                    var enemyEvent = PlayerManager.GetPlayerEvent(enemy);
                    if (enemyEvent == null || !enemyEvent.IsValid) continue;

                    var enemyPawn = enemyEvent.PlayerPawn?.Value;
                    if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.Health <= 0 || enemyPawn.AbsOrigin == null) continue;

                    var key = (wire.OwnerIndex, enemyEvent.Index);
                    if (revealed.ContainsKey(key)) continue;

                    Vector body = new(enemyPawn.AbsOrigin.X, enemyPawn.AbsOrigin.Y, enemyPawn.AbsOrigin.Z + bodyHeight);
                    if (DistanceToSegment(body, wire.Start, wire.End) > radius) continue;

                    revealed[key] = (owner.Slot, tick + revealTicks);

                    var ownerEvent = PlayerManager.GetPlayerFromEvent(owner);
                    if (ownerEvent != null && ownerEvent.IsValid)
                    {
                        ownerEvent.ExecuteClientCommand("play sounds/weapons/taser/taser_charge_ready");
                        ownerEvent.PrintToChat($" {ChatColors.Red}" + ownerEvent.GetTranslation("tripwire_triggered_info", enemyEvent.PlayerName));
                    }
                }
            }
        }

        private static void RevealOnRadar(uint targetIndex, int ownerSlot)
        {
            var target = Utilities.GetPlayerFromIndex((int)targetIndex);
            if (target == null || !target.IsValid) return;

            var targetPawn = target.PlayerPawn?.Value;
            if (targetPawn == null || !targetPawn.IsValid || targetPawn.Health <= 0) return;

            targetPawn.EntitySpottedState.SpottedByMask[0] |= (1u << (ownerSlot % 32));
        }

        private static float DistanceToSegment(Vector point, Vector start, Vector end)
        {
            Vector line = new(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
            float lengthSquared = line.X * line.X + line.Y * line.Y + line.Z * line.Z;
            if (lengthSquared <= .0001f) return (float)SkillUtils.GetDistance(point, start);

            Vector toPoint = new(point.X - start.X, point.Y - start.Y, point.Z - start.Z);
            float t = (toPoint.X * line.X + toPoint.Y * line.Y + toPoint.Z * line.Z) / lengthSquared;
            t = Math.Clamp(t, 0f, 1f);

            Vector closest = new(start.X + line.X * t, start.Y + line.Y * t, start.Z + line.Z * t);
            return (float)SkillUtils.GetDistance(point, closest);
        }

        private class WireInfo
        {
            public required uint OwnerIndex { get; set; }
            public required uint BeamIndex { get; set; }
            public required Vector Start { get; set; }
            public required Vector End { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff3b3b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, float radarDuration = 5f, float triggerRadius = 24f, float wireHeight = 30f, float wireWidth = 1.5f, float maxWallDistance = 400f, int maxWires = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float RadarDuration { get; set; } = radarDuration;
            public float TriggerRadius { get; set; } = triggerRadius;
            public float WireHeight { get; set; } = wireHeight;
            public float WireWidth { get; set; } = wireWidth;
            public float MaxWallDistance { get; set; } = maxWallDistance;
            public int MaxWires { get; set; } = maxWires;
        }
    }
}
