using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class Thorns : ISkill
    {
        private const Skills skillName = Skills.Thorns;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = @event.Userid;
            if (victim == null || !victim.IsValid) return;

            var attacker = @event.Attacker;
            if (attacker == null || !attacker.IsValid) return;

            var attackerEvent = PlayerManager.GetPlayerEvent(attacker);
            var victimEvent = PlayerManager.GetPlayerEvent(victim);

            if (attackerEvent == null || !attackerEvent.IsValid) return;
            if (victimEvent == null || !victimEvent.IsValid ) return;

            var attackerPawn = attackerEvent.PlayerPawn.Value;
            if (attackerPawn == null || !attackerPawn.IsValid || attackerPawn.Health == 0) return;

            if (attackerEvent.Index == victimEvent.Index) return;

            var victimInfo = PlayerManager.GetPlayerByIndex(victimEvent!.Index);
            if (victimInfo?.Skill == skillName)
            {
                int damage = (int)(@event.DmgHealth * SkillsInfo.GetValue<float>(skillName, "healthTakenScale"));
                damage = Math.Min(damage, SkillsInfo.GetValue<int>(skillName, "maxTakenDamagePerShot"));

                SkillUtils.TakeHealth(attackerEvent.PlayerPawn.Value, damage);
                attackerEvent.EmitSound("Player.DamageBody.Onlooker");
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#962631", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float healthTakenScale = .3f, int maxTakenDamagePerShot = 37) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float HealthTakenScale { get; set; } = healthTakenScale;
            public int MaxTakenDamagePerShot { get; set; } = maxTakenDamagePerShot;
        }
    }
}