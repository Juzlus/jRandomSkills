using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Dracula : ISkill
    {
        private const Skills skillName = Skills.Dracula;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim) || attacker == victim) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(attacker!.Index);

            if (playerInfo?.Skill == skillName && victim!.PawnIsAlive)
                HealAttacker(attacker!, @event.DmgHealth);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return;

            pawn.MaxHealth = 100;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            if (pawn.Health > 100)
            {
                pawn.Health = 100;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
            }
        }

        private static void HealAttacker(CCSPlayerController attacker, float damage)
        {
            var attackerPawn = attacker.PlayerPawn?.Value;
            if (attackerPawn == null || !attackerPawn.IsValid) return;

            if (attackerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || attackerPawn.Health <= 0) return;

            int extraHealth = (int)(damage * SkillsInfo.GetValue<float>(skillName, "healthRegainScale"));
            if (extraHealth <= 0) return;

            attackerPawn.Health += extraHealth;
            Utilities.SetStateChanged(attackerPawn, "CBaseEntity", "m_iHealth");

            if (attackerPawn.MaxHealth < attackerPawn.Health)
            {
                attackerPawn.MaxHealth = attackerPawn.Health;
                Utilities.SetStateChanged(attackerPawn, "CBaseEntity", "m_iMaxHealth");
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#FA050D", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float healthRegainScale = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float HealthRegainScale { get; set; } = healthRegainScale;
        }
    }
}