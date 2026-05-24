using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Disarmament : ISkill
    {
        private const Skills skillName = Skills.Disarmament;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim) || attacker == victim) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(attacker!.Index);

            if (playerInfo?.Skill == skillName && victim!.PawnIsAlive)
            {
                if (Instance.Random.NextDouble() <= playerInfo?.SkillChance)
                {
                    var weaponServices = victim.PlayerPawn?.Value?.WeaponServices;
                    if (weaponServices?.ActiveWeapon == null) return;

                    var weaponName = weaponServices?.ActiveWeapon?.Value?.DesignerName;
                    if (weaponName != null && !weaponName.Contains("weapon_knife") && !weaponName.Contains("weapon_bayonet") && !weaponName.Contains("weapon_c4"))
                        victim.DropActiveWeapon();
                }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            float newChance = (float)Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "chanceTo") - SkillsInfo.GetValue<float>(skillName, "chanceFrom")) + SkillsInfo.GetValue<float>(skillName, "chanceFrom");
            playerInfo.SkillChance = newChance;
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, newChance)}",
                border: !Utilities.GetPlayers().Any(p => p.Team == player.Team && p != player) ? "tb" : "t");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#FF4500", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float chanceFrom = .2f, float chanceTo = .35f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
        }
    }
}