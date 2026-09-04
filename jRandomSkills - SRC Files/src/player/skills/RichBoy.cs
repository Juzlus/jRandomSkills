using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class RichBoy : ISkill
    {
        private const Skills skillName = Skills.RichBoy;

        private static readonly ConcurrentDictionary<uint, int> accountBeforeBonus = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        private static int GetMaxMoney() => SkillUtils.CvarValue("mp_maxmoney", 16000);

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var moneyServices = player.InGameMoneyServices;
            if (moneyServices == null) return;

            int moneyBonus = Instance.Random.Next(SkillsInfo.GetValue<int>(skillName, "minMoney"), SkillsInfo.GetValue<int>(skillName, "maxMoney"));
            moneyBonus = Math.Min(moneyBonus, GetMaxMoney() - moneyServices.Account);
            if (moneyBonus <= 0) return;

            accountBeforeBonus[player.Index] = moneyServices.Account;

            moneyServices.Account += moneyBonus;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (!accountBeforeBonus.TryRemove(player.Index, out int accountBefore)) return;

            var moneyServices = player.InGameMoneyServices;
            if (moneyServices == null) return;

            if (moneyServices.Account <= accountBefore) return;

            moneyServices.Account = accountBefore;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            accountBeforeBonus.TryRemove(playerIndex, out _);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#D4AF37", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int minMoney = 5000, int maxMoney = 15000) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int MinMoney { get; set; } = minMoney;
            public int MaxMoney { get; set; } = maxMoney;
        }
    }
}