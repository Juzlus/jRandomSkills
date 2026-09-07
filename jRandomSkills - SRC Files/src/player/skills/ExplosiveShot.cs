using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class ExplosiveShot : ISkill
    {
        private const Skills skillName = Skills.ExplosiveShot;

        private static readonly QAngle angle = new(5, 10, -4);
        private static readonly ConcurrentDictionary<uint, int> lastTickByPlayer = [];
        private static readonly ConcurrentDictionary<int, ConcurrentQueue<(byte Team, uint Owner)>> nades = [];

        public static void NewRound()
        {
            nades.Clear();
            lastTickByPlayer.Clear();
        }

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
                border: !PlayerManager.GetTickPlayers().Any(p => p.Team == player.Team && p != player) ? "tb" : "t");
        }

        private static void SpawnExplosion(Vector vector, CCSPlayerController player)
        {
            lastTickByPlayer[player.Index] = Server.TickCount;
            nades.GetOrAdd(Server.TickCount, static _ => new ConcurrentQueue<(byte, uint)>()).Enqueue((player.TeamNum, player.Index));
            SkillUtils.CreateHEGrenadeProjectile(vector, angle, new Vector(0, 0, 0), player.TeamNum);
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            if (entity.DesignerName != "hegrenade_projectile") return;

            var heProjectile = entity.As<CBaseCSGrenadeProjectile>();
            if (heProjectile == null || !heProjectile.IsValid || heProjectile.AbsRotation == null) return;

            int spawnTick = Server.TickCount;

            Server.NextFrame(() =>
            {
                if (heProjectile == null || !heProjectile.IsValid) return;
                if (!(NearlyEquals(angle.X, heProjectile.AbsRotation.X) && NearlyEquals(angle.Y, heProjectile.AbsRotation.Y) && NearlyEquals(angle.Z, heProjectile.AbsRotation.Z)))
                    return;

                if (!nades.TryGetValue(spawnTick, out var queue) || !queue.TryDequeue(out var source)) return;
                if (queue.IsEmpty) nades.TryRemove(spawnTick, out _);

                heProjectile.TicksAtZeroVelocity = 100;
                heProjectile.TeamNum = source.Team;
                heProjectile.Damage = SkillsInfo.GetValue<float>(skillName, "damage");
                heProjectile.DmgRadius = SkillsInfo.GetValue<float>(skillName, "damageRadius");
                heProjectile.DetonateTime = 0;

                heProjectile.Globalname = $"explosiveshot_team_{source.Team}_{source.Owner}_{heProjectile.Index}";
            });
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null) return;

            var nade = damageInfo.Attacker?.Value;
            if (nade == null || !nade.IsValid) return;

            if (nade.DesignerName != "hegrenade_projectile") return;
            if (string.IsNullOrEmpty(nade.Globalname) || !nade.Globalname.StartsWith("explosiveshot_team_")) return;

            var parts = nade.Globalname.Split('_');
            if (parts.Length < 4) return;
            if (!int.TryParse(parts[2], out int nadeTeam)) return;
            if (!uint.TryParse(parts[3], out uint ownerIndex)) return;

            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);

            if (victimPawn.DesignerName != "player") return;
            if (victimPawn.Controller?.Value == null) return;

            var victim = victimPawn.Controller.Value.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid || victim.Index == ownerIndex) return;

            if (victimPawn.TeamNum == nadeTeam)
            {
                if (!SkillsInfo.GetValue<bool>(skillName, "friendlyFire"))
                {
                    damageInfo.Damage = 0;
                    return;
                }

                damageInfo.Damage *= SkillUtils.GetTeamDamageMultiplier(skillName);
            }

            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex);
            if (owner != null && !owner.IsValid) owner = null;

            if (owner != null && SkillUtils.IsPredictedLethal(damageInfo, victimPawn))
                SkillUtils.RegisterKillCredit(victim.Index, owner.Index, KillfeedIcons.Explosion);
        }

        private static bool NearlyEquals(float a, float b, float epsilon = 0.001f) => Math.Abs(a - b) < epsilon;

        public static void BulletImpact(EventBulletImpact @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            // One explosion per player per tick: a shotgun reports every pellet as its own impact.
            if (lastTickByPlayer.TryGetValue(player.Index, out int last) && last == Server.TickCount) return;

            var pos = new Vector(@event.X, @event.Y, @event.Z);

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null || playerInfo.Skill != skillName) return;

            if (Instance.Random.NextDouble() <= playerInfo.SkillChance)
                SpawnExplosion(pos, player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#9c0000", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float damage = 25f, float damageRadius = 210f, float chanceFrom = .15f, float chanceTo = .3f, float dmgReductionForTeamates = 0.5f, bool friendlyFire = true) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Damage { get; set; } = damage;
            public float DamageRadius { get; set; } = damageRadius;
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
            public float DmgReductionForTeamates { get; set; } = dmgReductionForTeamates;
        public bool FriendlyFire { get; set; } = friendlyFire;
        }
    }
}
