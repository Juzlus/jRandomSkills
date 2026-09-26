using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Pickpocket : ISkill
    {
        private const Skills skillName = Skills.Pickpocket;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        private static int GetMaxMoney() => SkillUtils.CvarValue("mp_maxmoney", 16000);

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);

            if (!Instance.IsPlayerValid(attacker) || victim == null || !victim.IsValid || attacker == victim) return;
            if (attacker!.Team == victim.Team) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(attacker.Index);
            if (playerInfo?.Skill != skillName) return;

            var attackerMoney = attacker.InGameMoneyServices;
            var victimMoney = victim.InGameMoneyServices;
            if (attackerMoney == null || victimMoney == null) return;

            int stolen = Math.Min(SkillsInfo.GetValue<int>(skillName, "moneyPerHit"), victimMoney.Account);
            stolen = Math.Min(stolen, GetMaxMoney() - attackerMoney.Account);
            if (stolen <= 0) return;

            victimMoney.Account -= stolen;
            Utilities.SetStateChanged(victim, "CCSPlayerController", "m_pInGameMoneyServices");

            attackerMoney.Account += stolen;
            Utilities.SetStateChanged(attacker, "CCSPlayerController", "m_pInGameMoneyServices");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#3fae4a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int moneyPerHit = 150) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int MoneyPerHit { get; set; } = moneyPerHit;
        }
    }
}
