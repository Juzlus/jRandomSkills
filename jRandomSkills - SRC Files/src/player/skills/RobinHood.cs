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

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = @event.Userid;
            var attacker = @event.Attacker;
            var damage = @event.DmgHealth;
            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim) || attacker == victim) return;

            var attackerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);
            if (attackerInfo?.Skill != skillName) return;

            int moneyToSteal = damage * SkillsInfo.GetValue<int>(skillName, "moneyMultiplier");
            StealMoney(victim!, attacker!, moneyToSteal);
        }

        private static void StealMoney(CCSPlayerController victim, CCSPlayerController attacker, int money)
        {
            var victimMoneyServices = victim?.InGameMoneyServices;
            var attackerMoneyServices = attacker?.InGameMoneyServices;
            if (victimMoneyServices == null || attackerMoneyServices == null) return;

            var moneyToAdd = victimMoneyServices.Account < money ? victimMoneyServices.Account : money;
            victimMoneyServices.Account = Math.Max(victimMoneyServices.Account - money, 0);
            Utilities.SetStateChanged(victim!, "CCSPlayerController", "m_pInGameMoneyServices");

            attackerMoneyServices.Account = Math.Min(attackerMoneyServices.Account + moneyToAdd, 16000);
            Utilities.SetStateChanged(attacker!, "CCSPlayerController", "m_pInGameMoneyServices");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#119125", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int moneyMultiplier = 35) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int MoneyMultiplier { get; set; } = moneyMultiplier;
        }
    }
}