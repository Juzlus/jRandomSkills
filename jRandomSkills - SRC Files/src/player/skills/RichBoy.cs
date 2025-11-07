using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class RichBoy : ISkill
    {
        private const Skills skillName = Skills.RichBoy;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            int moneyBonus = Instance.Random.Next(SkillsInfo.GetValue<int>(skillName, "minMoney"), SkillsInfo.GetValue<int>(skillName, "maxMoney"));
            playerInfo.SkillChance = moneyBonus;
            AddMoney(player, moneyBonus);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            AddMoney(player, -(int)(playerInfo.SkillChance ?? 0));
        }

        private static void AddMoney(CCSPlayerController player, int money)
        {
            if (player == null || !player.IsValid) return;
            var moneyServices = player.InGameMoneyServices;
            if (moneyServices == null) return;

            moneyServices.Account = Math.Min(Math.Max(moneyServices.Account + money, 0), 16000);
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#D4AF37", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int minMoney = 5000, int maxMoney = 15000) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int MinMoney { get; set; } = minMoney;
            public int MaxMoney { get; set; } = maxMoney;
        }
    }
}