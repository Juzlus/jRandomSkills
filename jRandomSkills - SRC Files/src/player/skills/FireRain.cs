using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class FireRain : ISkill
    {
        private const Skills skillName = Skills.FireRain;
        private static readonly ConcurrentDictionary<uint, byte> decoys = [];
        private static int rainTick = -1;
        private static CCSPlayerPawn? rainThrower;
        private static byte rainTeam = (byte)CsTeam.None;
        private static readonly ConcurrentDictionary<uint, (uint ThrowerRaw, byte Team)> rainMolotovs = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            DecoyRing.PreloadAssets();
        }

        public static void NewRound()
        {
            KillAllDecoys();
            rainMolotovs.Clear();
            DecoyRing.ClearAll(skillName);
        }

        private static void KillAllDecoys()
        {
            foreach (var decoyIndex in decoys.Keys.ToArray())
            {
                var decoy = Utilities.GetEntityFromIndex<CDecoyProjectile>((int)decoyIndex);
                if (decoy != null && decoy.IsValid && decoy.DesignerName == "decoy_projectile")
                    decoy.AddEntityIOEvent("Kill", decoy, delay: 0.1f);
            }

            decoys.Clear();
        }

        private static void SpawnRain(CCSPlayerController player, Vector targetPos)
        {
            var pawn = player.PlayerPawn.Value;

            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
                return;

            const int grenadeCount = 5;
            const int grenadeGroundCount = 2;

            const float spawnHeight = 1500.0f;
            const float approachDistance = 600.0f;

            rainTick = Server.TickCount;
            rainThrower = pawn;
            rainTeam = player.TeamNum;

            float startAngle = Random.Shared.NextSingle() * MathF.Tau;

            Vector? skyCenter = null;
            bool foundPosition = false;

            for (int i = 0; i < 8; i++)
            {
                float approachAngle = startAngle + i * (MathF.Tau / 8);

                float dirX = MathF.Cos(approachAngle);
                float dirY = MathF.Sin(approachAngle);

                Vector testCenter = new(
                    targetPos.X + dirX * approachDistance,
                    targetPos.Y + dirY * approachDistance,
                    targetPos.Z + spawnHeight
                );

                var trace = RayTrace.TraceShape(player, targetPos, testCenter, null, 0);

                if (!trace.HasValue || !trace.Value.DidHit)
                {
                    skyCenter = testCenter;
                    foundPosition = true;
                    break;
                }
            }

            if (!foundPosition || skyCenter == null)
            {
                CreateMolotovSplash(targetPos, grenadeGroundCount, player.TeamNum);
                return;
            }

            CreateMolotovRaid(targetPos, skyCenter, grenadeCount, player.TeamNum);
        }

        private static void CreateMolotovSplash(Vector targetPos, int grenadeCount, byte teamNum)
        {
            const float spreadSpeed = 100.0f;
            const float upwardVelocity = 100.0f;

            for (int i = 0; i < grenadeCount; i++)
            {
                float angle = Random.Shared.NextSingle() * MathF.Tau;
                float speed = spreadSpeed * (0.7f + Random.Shared.NextSingle() * 0.6f);

                float vx = MathF.Cos(angle) * speed;
                float vy = MathF.Sin(angle) * speed;
                float vz = upwardVelocity + Random.Shared.NextSingle() * 150.0f;

                Vector velocity = new(vx, vy, vz);

                Vector spawnPos = new(
                    targetPos.X,
                    targetPos.Y,
                    targetPos.Z + 10.0f
                );

                float yaw = MathF.Atan2(vy, vx) * (180.0f / MathF.PI);
                float horizontalSpeed = MathF.Sqrt(vx * vx + vy * vy);
                float pitch = -MathF.Atan2(vz, horizontalSpeed) * (180.0f / MathF.PI);

                QAngle launchAngle = new(pitch, yaw, 0.0f);
                SkillUtils.CreateMolotovProjectile(spawnPos, launchAngle, velocity, teamNum);
            }
        }

        private static void CreateMolotovRaid(Vector targetPos, Vector? skyCenter, int grenadeCount, byte teamNum)
        {
            if (skyCenter == null)
                return;

            const float clusterRadius = 130.0f;
            const float flightTime = 1.2f;
            const float gravity = 800.0f;
            const float maxHorizontalVelocity = 700.0f;

            Vector adjustedTarget = targetPos;
            Vector direction = new(
                skyCenter.X - targetPos.X,
                skyCenter.Y - targetPos.Y,
                0
            );

            float length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);

            if (length > 0)
            {
                direction.X /= length;
                direction.Y /= length;

                const float aimOffset = 150.0f;

                adjustedTarget = new Vector(
                    targetPos.X + direction.X * aimOffset,
                    targetPos.Y + direction.Y * aimOffset,
                    targetPos.Z
                );
            }

            float dx = adjustedTarget.X - skyCenter.X;
            float dy = adjustedTarget.Y - skyCenter.Y;
            float dz = adjustedTarget.Z - skyCenter.Z;

            float vx = dx / flightTime;
            float vy = dy / flightTime;
            float vz = (dz + 0.5f * gravity * flightTime * flightTime) / flightTime;

            float horizontalSpeed = MathF.Sqrt(vx * vx + vy * vy);

            if (horizontalSpeed > maxHorizontalVelocity)
            {
                float scale = maxHorizontalVelocity / horizontalSpeed;
                vx *= scale;
                vy *= scale;
            }

            Vector velocity = new(vx, vy, vz);

            float yaw = MathF.Atan2(vy, vx) * (180.0f / MathF.PI);
            float finalHorizontalSpeed = MathF.Sqrt(vx * vx + vy * vy);
            float pitch = -MathF.Atan2(vz, finalHorizontalSpeed) * (180.0f / MathF.PI);

            QAngle launchAngle = new(pitch, yaw, 0.0f);

            for (int i = 0; i < grenadeCount; i++)
            {
                float offsetAngle = Random.Shared.NextSingle() * MathF.Tau;
                float offsetDist = MathF.Sqrt(Random.Shared.NextSingle()) * clusterRadius;

                float offsetX = MathF.Cos(offsetAngle) * offsetDist;
                float offsetY = MathF.Sin(offsetAngle) * offsetDist;

                Vector spawnPos = new(
                    skyCenter.X + offsetX,
                    skyCenter.Y + offsetY,
                    skyCenter.Z
                );

                SkillUtils.CreateMolotovProjectile(spawnPos, launchAngle, velocity, teamNum);
            }
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            var name = entity.DesignerName;

            if (name == "molotov_projectile")
            {
                if (Server.TickCount != rainTick) return;

                var thrower = rainThrower;
                if (thrower == null || !thrower.IsValid) return;

                var molotov = entity.As<CMolotovProjectile>();
                if (molotov == null || !molotov.IsValid) return;

                molotov.TeamNum = rainTeam;
                molotov.Thrower.Raw = thrower.EntityHandle.Raw;
                molotov.OwnerEntity.Raw = thrower.EntityHandle.Raw;
                rainMolotovs[molotov.Index] = (thrower.EntityHandle.Raw, rainTeam);

                Server.NextWorldUpdate(() =>
                {
                    if (molotov == null || !molotov.IsValid) return;
                    molotov.DetonateTime += 30f;
                    Utilities.SetStateChanged(molotov, "CBaseGrenade", "m_flDetonateTime");
                });

                return;
            }

            if (name == "inferno")
            {
                var inferno = entity.As<CInferno>();
                if (inferno == null || !inferno.IsValid) return;

                var ownerHandle = inferno.OwnerEntity;
                var source = ownerHandle?.Value;
                if (ownerHandle == null || source == null || !source.IsValid) return;
                if (!rainMolotovs.TryGetValue(source.Index, out var origin)) return;

                inferno.TeamNum = origin.Team;
                ownerHandle.Raw = origin.ThrowerRaw;
                return;
            }

            if (name != "decoy_projectile")
                return;

            var decoy = entity.As<CDecoyProjectile>();
            if (decoy == null || !decoy.IsValid || decoy.OwnerEntity == null || decoy.OwnerEntity.Value == null || !decoy.OwnerEntity.Value.IsValid) return;

            var pawn = decoy.OwnerEntity.Value.As<CCSPlayerPawn>();
            if (pawn == null || !pawn.IsValid || pawn.Controller == null || pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;

            var player = pawn.Controller.Value.As<CCSPlayerController>();
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));
            if (playerInfo?.Skill != skillName) return;
            decoys.TryAdd(decoy.Index, 0);
        }

        public static void DecoyStarted(EventDecoyStarted @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            uint key = (uint)@event.Entityid;
            if (decoys.ContainsKey(key))
            {
                var decoy = Utilities.GetEntityFromIndex<CDecoyProjectile>(@event.Entityid);
                if (decoy != null && decoy.IsValid)
                    decoy.AddEntityIOEvent("Kill", decoy, delay: 0.1f);
                decoys.TryRemove(key, out _);

                Vector impact = new(@event.X, @event.Y, @event.Z);
                SpawnRain(player, impact);

                DecoyRing.Show(skillName, key, impact, 130f);
                jRandomSkills.Instance.AddTimer(SkillsInfo.GetValue<float>(skillName, "markerSeconds"),
                    () => DecoyRing.Hide(skillName, key),
                    CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillUtils.TryGiveWeapon(player, CsItem.DecoyGrenade);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffbf47", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Epic, float markerSeconds = 4f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float MarkerSeconds { get; set; } = markerSeconds;
        }
    }
}