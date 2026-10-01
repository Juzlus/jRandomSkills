using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    // Double Trouble hands out extra random skills on top of whatever the round draw gave (see ComboManager
    // for the clash rules). The skill is solo, so a Double Trouble player only ever holds its extras.
    public class DoubleTrouble : ISkill
    {
        private const Skills skillName = Skills.DoubleTrouble;

        // Skills that pick a target from a menu, hand out or copy skills, or are combos themselves.
        private const string DefaultExcluded = "AreaReaper, Bankrupt, Bounty, CarefulBullets, Chameleon, Darkness, Deactivator, Deaf, DoubleTrouble, Duplicator, ExpensiveAmmo, Gambler, Giant, Glitch, Inheritance, Jammer, JetKick, JumpBan, JumpCurse, LifeSwap, Magnifier, Marked, MoneySwap, Mute, Nemesis, Nightmare, Poison, PrimaryBan, Rage, Thief, Voodoo, WildThrow";

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null || playerInfo.Skill != skillName) return;

            var excluded = new HashSet<string>((SkillsInfo.GetValue<string>(skillName, "excludedSkills") ?? "").Split(',').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            int count = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "extraSkills"));
            ComboManager.GrantExtras(player, playerInfo, count, excluded);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null || playerInfo.ExtraSkills.Length == 0) return;

            var extras = playerInfo.ExtraSkills;
            playerInfo.ExtraSkills = [];
            foreach (var extra in extras)
                jRandomSkills.Instance.SkillAction(extra.ToString(), "DisableSkill", [player]);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff7f50", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, int extraSkills = 2, string excludedSkills = DefaultExcluded) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int ExtraSkills { get; set; } = extraSkills;
            public string ExcludedSkills { get; set; } = excludedSkills;
        }
    }
}
