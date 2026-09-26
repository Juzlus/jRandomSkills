using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Headhunter : ISkill
    {
        private const Skills skillName = Skills.Headhunter;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            if (!@event.Headshot) return;

            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(attacker) || victim == null || !victim.IsValid || attacker == victim) return;
            if (attacker!.Team == victim.Team) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(attacker.Index);
            if (playerInfo?.Skill != skillName) return;

            var pawn = attacker.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            SkillUtils.SetHealth(pawn, pawn.MaxHealth);

            int armor = SkillsInfo.GetValue<int>(skillName, "armorOnHeadshot");
            if (armor > 0 && pawn.ArmorValue < armor)
            {
                pawn.ArmorValue = armor;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
            }

            SkillUtils.ApplyScreenColor(attacker, 60, 255, 60, 60, 200, 100);
            attacker.PrintToChat($" {ChatColors.Lime}{attacker.GetTranslation("headhunter_heal_info")}");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d9a13b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int armorOnHeadshot = 100) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int ArmorOnHeadshot { get; set; } = armorOnHeadshot;
        }
    }
}
