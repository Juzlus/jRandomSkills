using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class Scavenger : ISkill
    {
        private const Skills skillName = Skills.Scavenger;
        private const int fallbackMaxMoney = 16000;

        public static void LoadSkill()
        {
            Localization.RegisterDescriptionArgs(skillName, () =>
            [
                SkillsInfo.GetValue<int>(skillName, "healthPerKill"),
                SkillsInfo.GetValue<int>(skillName, "moneyPerKill")
            ]);
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            if (victim == null || !victim.IsValid || attacker == null || !attacker.IsValid) return;
            if (victim.Key() == attacker.Key() || victim.Team == attacker.Team) return;

            if (PlayerManager.GetPlayer(attacker)?.Skill != skillName) return;

            var attackerPawn = attacker.PlayerPawn?.Value;
            if (attackerPawn == null || !attackerPawn.IsValid || attackerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            int health = SkillsInfo.GetValue<int>(skillName, "healthPerKill");
            if (health > 0)
                SkillUtils.AddHealth(attackerPawn, health);

            int money = SkillsInfo.GetValue<int>(skillName, "moneyPerKill");
            var moneyServices = attacker.InGameMoneyServices;
            if (money > 0 && moneyServices != null)
            {
                moneyServices.Account = Math.Min(moneyServices.Account + money, SkillUtils.CvarValue("mp_maxmoney", fallbackMaxMoney));
                NetStateFast.Changed(attacker, "CCSPlayerController", "m_pInGameMoneyServices");
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#c9a227", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int healthPerKill = 25, int moneyPerKill = 300) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int HealthPerKill { get; set; } = healthPerKill;
            public int MoneyPerKill { get; set; } = moneyPerKill;
        }
    }
}
