using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class ExplosiveShot : ISkill
    {
        private const Skills skillName = Skills.ExplosiveShot;

        private static readonly QAngle angle = new(5, 10, -4);
        private static int lastTick = 0;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            
            float newChance = (float)Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "ChanceTo") - SkillsInfo.GetValue<float>(skillName, "ChanceFrom")) + SkillsInfo.GetValue<float>(skillName, "ChanceFrom");
            playerInfo.SkillChance = newChance;
            
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, newChance)}",
                border: !Utilities.GetPlayers().Any(p => p.Team == player.Team && p != player) ? "tb" : "t");
        }

        private static void SpawnExplosion(Vector vector)
        {
            lastTick = Server.TickCount;
            SkillUtils.CreateHEGrenadeProjectile(vector, angle, new Vector(0, 0, 0), 0);
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            if (entity.DesignerName != "hegrenade_projectile") return;

            var heProjectile = entity.As<CBaseCSGrenadeProjectile>();
            if (heProjectile == null || !heProjectile.IsValid || heProjectile.AbsRotation == null) return;

            Server.NextFrame(() =>
            {
                if (heProjectile == null || !heProjectile.IsValid) return;
                if (!(NearlyEquals(angle.X, heProjectile.AbsRotation.X) && NearlyEquals(angle.Y, heProjectile.AbsRotation.Y) && NearlyEquals(angle.Z, heProjectile.AbsRotation.Z)))
                    return;

                heProjectile.TicksAtZeroVelocity = 100;
                heProjectile.TeamNum = (byte)CsTeam.None;
                heProjectile.Damage = SkillsInfo.GetValue<float>(skillName, "damage");
                heProjectile.DmgRadius = SkillsInfo.GetValue<float>(skillName, "damageRadius");
                heProjectile.DetonateTime = 0;
            });
        }

        private static bool NearlyEquals(float a, float b, float epsilon = 0.001f) => Math.Abs(a -b) < epsilon;

        public static void BulletImpact(EventBulletImpact @event)
        {
            if (lastTick == Server.TickCount) return;

            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var pos = new Vector(@event.X, @event.Y, @event.Z);

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null || playerInfo.Skill != skillName) return;

            if (Instance.Random.NextDouble() <= playerInfo.SkillChance)
                SpawnExplosion(pos);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#9c0000", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float damage = 25f, float damageRadius = 210f, float chanceFrom = .15f, float chanceTo = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float Damage { get; set; } = damage;
            public float DamageRadius { get; set; } = damageRadius;
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
        }
    }
}