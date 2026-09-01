using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class RobinHood : ISkill
    {
        private const Skills skillName = Skills.RobinHood;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        private static int GetMaxMoney() => SkillUtils.CvarValue("mp_maxmoney", 16000);

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            if (!Instance.IsPlayerValid(attacker) || victim == null || !victim.IsValid || attacker == victim) return;

            var attackerInfo = PlayerManager.GetPlayerByIndex(attacker!.Index);
            if (attackerInfo?.Skill != skillName) return;

            int damage = SkillUtils.CapToVictimHealth(victim, @event.DmgHealth);
            int moneyToSteal = damage * SkillsInfo.GetValue<int>(skillName, "moneyMultiplier");
            StealMoney(victim!, attacker!, moneyToSteal);
        }

        private static void StealMoney(CCSPlayerController victim, CCSPlayerController attacker, int money)
        {
            var victimMoneyServices = victim?.InGameMoneyServices;
            var attackerMoneyServices = attacker?.InGameMoneyServices;
            if (victimMoneyServices == null || attackerMoneyServices == null) return;

            int maxMoney = GetMaxMoney();
            int headroom = Math.Max(maxMoney - attackerMoneyServices.Account, 0);

            int transfer = Math.Min(money, victimMoneyServices.Account);
            transfer = Math.Min(transfer, headroom);
            if (transfer <= 0) return;

            victimMoneyServices.Account -= transfer;
            Utilities.SetStateChanged(victim!, "CCSPlayerController", "m_pInGameMoneyServices");

            attackerMoneyServices.Account += transfer;
            Utilities.SetStateChanged(attacker!, "CCSPlayerController", "m_pInGameMoneyServices");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#119125", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int moneyMultiplier = 35) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int MoneyMultiplier { get; set; } = moneyMultiplier;
        }
    }
}