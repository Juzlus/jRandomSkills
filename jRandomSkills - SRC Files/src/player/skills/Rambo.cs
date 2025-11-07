using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Rambo : ISkill
    {
        private const Skills skillName = Skills.Rambo;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            int healthBonus = Instance.Random.Next(SkillsInfo.GetValue<int>(skillName, "minExtraHealth"), SkillsInfo.GetValue<int>(skillName, "maxExtraHealth"));
            AddHealth(player, healthBonus);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            ResetHealth(player);
        }

        public static void AddHealth(CCSPlayerController player, int health)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null) return;

            pawn.MaxHealth = Math.Min(pawn.Health + health, 1000);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            pawn.Health = pawn.MaxHealth;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        public static void ResetHealth(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null) return;

            pawn.MaxHealth = 100;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            pawn.Health = Math.Min(pawn.Health, 100);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#009905", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int minExtraHealth = 50, int maxExtraHealth = 501) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int MinExtraHealth { get; set; } = minExtraHealth;
            public int MaxExtraHealth { get; set; } = maxExtraHealth;
        }
    }
}