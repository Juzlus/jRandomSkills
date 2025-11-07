using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Pyro : ISkill
    {
        private const Skills skillName = Skills.Pyro;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = @event.Userid;
            int damage = @event.DmgHealth;
            string weapon = @event.Weapon;

            if (weapon != "inferno" || !Instance.IsPlayerValid(victim)) return;
            var victimInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == victim?.SteamID);
            if (victimInfo == null || victimInfo.Skill != skillName) return ;

            RestoreHealth(victim!, damage * SkillsInfo.GetValue<float>(skillName, "regenerationMultiplier"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, player.Team == CsTeam.CounterTerrorist ? CsItem.IncendiaryGrenade : CsItem.Molotov);
        }

        private static void RestoreHealth(CCSPlayerController victim, float damage)
        {
            var playerPawn = victim.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;
            var newHealth = playerPawn.Health + damage;

            if (newHealth > 100)
                newHealth = 100;

            playerPawn.Health = (int)newHealth;
            Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#3c47de", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float regenerationMultiplier = 1.5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float RegenerationMultiplier { get; set; } = regenerationMultiplier;
        }
    }
}