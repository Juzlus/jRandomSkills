using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
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
            bool alone = PlayerManager.GetTickPlayers().Count(p => p.IsValid && p.Team == player.Team) <= 1;

            var candidates = SkillData.Skills.Where(s =>
            {
                if (s == null || s.Skill == Skills.None || s.Skill == skillName) return false;
                string name = SkillNames.Get(s.Skill);
                if (excluded.Contains(name) || IsSkillBlockedByMode(s.Skill)) return false;

                var def = SkillsInfo.GetSkillConfig(s.Skill);
                if (def == null || !def.Active) return false;
                if (def.OnlyTeam != (int)CsTeam.None && def.OnlyTeam != (int)player.Team) return false;
                if (def.NeedsTeammates && alone) return false;
                if (!string.IsNullOrEmpty(def.RequiredPermission) && !player.IsBot && !CounterStrikeSharp.API.Modules.Admin.AdminManager.PlayerHasPermissions(player, def.RequiredPermission)) return false;
                return true;
            }).ToList();

            if (candidates.Count == 0) return;

            int count = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "extraSkills"));
            var picked = new List<Skills>();
            while (picked.Count < count && candidates.Count > 0)
            {
                var choice = candidates[Instance.Random.Next(candidates.Count)];
                candidates.Remove(choice);
                picked.Add(choice.Skill);
            }

            playerInfo.ExtraSkills = [.. picked];
            foreach (var extra in picked)
            {
                Instance.SkillAction(extra.ToString(), "EnableSkill", [player]);
                player.PrintToChat($" {ChatColors.Gold}{player.GetTranslation("doubletrouble_extra_info", $"{ChatColors.Lime}{player.GetSkillName(extra)}{ChatColors.Grey} - {player.GetSkillDescription(extra)}")}");
            }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null || playerInfo.ExtraSkills.Length == 0) return;

            var extras = playerInfo.ExtraSkills;
            playerInfo.ExtraSkills = [];
            foreach (var extra in extras)
                Instance.SkillAction(extra.ToString(), "DisableSkill", [player]);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff7f50", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, int extraSkills = 1, string excludedSkills = DefaultExcluded) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int ExtraSkills { get; set; } = extraSkills;
            public string ExcludedSkills { get; set; } = excludedSkills;
        }
    }
}
