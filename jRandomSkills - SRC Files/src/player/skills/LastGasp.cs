using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using static System.Net.Mime.MediaTypeNames;

namespace src.player.skills
{
    public class LastGasp : ISkill
    {
        private const Skills skillName = Skills.LastGasp;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);

            if (victim == null || !victim.IsValid || attacker == null || !attacker.IsValid)
                return;

            var victimPawn = victim.PlayerPawn?.Value;
            var attackerPawn = attacker.PlayerPawn?.Value;

            if (victimPawn == null || !victimPawn.IsValid || attackerPawn == null || !attackerPawn.IsValid)
                return;

            var victimInfo = PlayerManager.GetPlayerByIndex(victim!.Index);
            if (victimInfo?.Skill != skillName)
                return;

            int damageAfterDeath = SkillsInfo.GetValue<int>(skillName, "DamageAfterDeath");
            bool canKill = SkillsInfo.GetValue<bool>(skillName, "CanKill");

            if (!canKill)
            {
                int newHealth = (int)(attackerPawn.Health - damageAfterDeath);
                if (newHealth <= 0)
                    damageAfterDeath = damageAfterDeath - 1 + newHealth;

            }

            SkillUtils.TakeHealth(attackerPawn, damageAfterDeath, victim, KillfeedIcons.Fist);
            PlayerManager.GetPlayerFromEvent(attacker)?.ExecuteClientCommand($"play player/player_damagebody_0{jRandomSkills.Instance.Random.Next(4, 8)}");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#88bdba", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, int damageAfterDeath = 30, bool canKill = true) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int DamageAfterDeath { get; set; } = damageAfterDeath;
            public bool CanKill { get; set; } = canKill;

        }
    }
}