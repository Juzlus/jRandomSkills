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
        private static int rainTick = -1;
        private static CCSPlayerPawn? rainThrower;
        private static byte rainTeam = (byte)CsTeam.None;
        private static readonly ConcurrentDictionary<uint, (uint ThrowerRaw, byte Team)> rainMolotovs = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            KillAllDecoys();
            rainMolotovs.Clear();
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
            const float maxSpawnHeight = 400.0f;
            const float clusterRadius = 110.0f;
            const float dropSpeed = 250.0f;

            rainTick = Server.TickCount;
            rainThrower = pawn;
            rainTeam = player.TeamNum;

            Vector decoyGround = new(targetPos.X, targetPos.Y, targetPos.Z + 8f);
            Vector dropVelocity = new(0, 0, -dropSpeed);

            for (int i = 0; i < grenadeCount; i++)
            {
                float offsetAngle = Random.Shared.NextSingle() * MathF.Tau;
                float offsetDist = MathF.Sqrt(Random.Shared.NextSingle()) * clusterRadius;

                Vector ground = new(
                    decoyGround.X + MathF.Cos(offsetAngle) * offsetDist,
                    decoyGround.Y + MathF.Sin(offsetAngle) * offsetDist,
                    decoyGround.Z
                );

                var reach = RayTrace.TraceShape(player, decoyGround, ground, null, 0);
                if (reach.HasValue && reach.Value.DidHit)
                    ground = decoyGround;

                float height = maxSpawnHeight;
                var ceiling = RayTrace.TraceShape(player, ground, new Vector(ground.X, ground.Y, ground.Z + maxSpawnHeight), null, 0);
                if (ceiling.HasValue && ceiling.Value.DidHit)
                    height = Math.Clamp(maxSpawnHeight * ceiling.Value.Fraction - 24f, 24f, maxSpawnHeight);

                Vector spawnPos = new(ground.X, ground.Y, ground.Z + height);

                SkillUtils.CreateMolotovProjectile(spawnPos, angle, dropVelocity, player.TeamNum);
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