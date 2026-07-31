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
        private static readonly QAngle angle = new(10, -4, 13);
        private static readonly ConcurrentDictionary<uint, byte> decoys = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            KillAllDecoys();
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
            const float spawnHeight = 800.0f;
            const float approachDistance = 600.0f;
            const float clusterRadius = 130.0f;
            const float flightTime = 0.65f;
            const float gravity = 800.0f;

            float approachAngle = Random.Shared.NextSingle() * MathF.Tau;
            float dirX = MathF.Cos(approachAngle);
            float dirY = MathF.Sin(approachAngle);

            Vector skyCenter = new(
                targetPos.X + dirX * approachDistance,
                targetPos.Y + dirY * approachDistance,
                targetPos.Z + spawnHeight
            );

            float dx = targetPos.X - skyCenter.X;
            float dy = targetPos.Y - skyCenter.Y;
            float dz = targetPos.Z - skyCenter.Z;

            float vx = dx / flightTime;
            float vy = dy / flightTime;
            float vz = (dz + 0.5f * gravity * flightTime * flightTime) / flightTime;

            Vector velocity = new(vx, vy, vz);

            float yaw = MathF.Atan2(vy, vx) * (180.0f / MathF.PI);
            float horizontalSpeed = MathF.Sqrt(vx * vx + vy * vy);
            float pitch = -MathF.Atan2(vz, horizontalSpeed) * (180.0f / MathF.PI);
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

                SkillUtils.CreateMolotovProjectile(spawnPos, launchAngle, velocity, player.TeamNum);
            }
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            var name = entity.DesignerName;
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
                SpawnRain(player, new Vector(@event.X, @event.Y, @event.Z));
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillUtils.TryGiveWeapon(player, CsItem.DecoyGrenade);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffbf47", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Epic) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}