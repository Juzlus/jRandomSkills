using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using src.player;
using System.Collections.Concurrent;
using System.Reflection;
using static src.jRandomSkills;

namespace src.utils
{
    // Skill combos: a player can hold several skills at once (jSkill_PlayerInfo.ExtraSkills) as long as
    // they don't clash. Clashes come from Config.Combos.ClashGroups plus two built-in rules: only one skill
    // with a target menu (TypeSkill) and, unless allowed, only one skill fired by the use key (UseSkill).
    public static class ComboManager
    {
        private static Config.CombosSettings Settings => Config.LoadedConfig.Combos;

        private static readonly ConcurrentDictionary<(Skills, string), bool> hookCache = new();

        // True when the skill's class defines the given ISkill hook itself.
        public static bool HasHook(Skills skill, string method)
        {
            return hookCache.GetOrAdd((skill, method), key =>
            {
                var type = Assembly.GetExecutingAssembly().GetType($"src.player.skills.{key.Item1}");
                return type?.GetMethod(key.Item2, BindingFlags.Static | BindingFlags.Public) != null;
            });
        }

        public static bool IsSolo(Skills skill)
        {
            string name = SkillNames.Get(skill);
            return skill == Skills.None || Settings.SoloSkills.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static bool Clash(Skills a, Skills b)
        {
            if (a == b) return true;
            if (IsSolo(a) || IsSolo(b)) return true;
            if (HasHook(a, "TypeSkill") && HasHook(b, "TypeSkill")) return true;
            if (!Settings.AllowMultipleUseKeySkills && HasHook(a, "UseSkill") && HasHook(b, "UseSkill")) return true;

            string nameA = SkillNames.Get(a), nameB = SkillNames.Get(b);
            foreach (var group in Settings.ClashGroups)
                if (group.Contains(nameA, StringComparer.OrdinalIgnoreCase) && group.Contains(nameB, StringComparer.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        // The held skill whose target menu is open (only one is allowed), or the main skill.
        public static Skills MenuSkillOf(jSkill_PlayerInfo info)
        {
            foreach (var skill in info.AllSkills())
                if (HasHook(skill, "TypeSkill")) return skill;
            return info.Skill;
        }

        // Skills that can be added to what the player already holds.
        public static List<Skills> Candidates(CCSPlayerController player, jSkill_PlayerInfo info, ISet<string>? excluded = null)
        {
            var held = info.AllSkills().ToList();
            bool alone = PlayerManager.GetTickPlayers().Count(p => p.IsValid && p.Team == player.Team) <= 1;
            var result = new List<Skills>();

            foreach (var s in SkillData.GetSnapshot())
            {
                if (s == null || s.Skill == Skills.None || IsSolo(s.Skill)) continue;
                string name = SkillNames.Get(s.Skill);
                if (excluded != null && excluded.Contains(name)) continue;
                if (IsSkillBlockedByMode(s.Skill)) continue;

                var def = SkillsInfo.GetSkillConfig(s.Skill);
                if (def == null || !def.Active) continue;
                if (def.OnlyTeam != (int)CsTeam.None && def.OnlyTeam != (int)player.Team) continue;
                if (def.NeedsTeammates && alone) continue;
                if (!string.IsNullOrEmpty(def.RequiredPermission) && !player.IsBot && !AdminManager.PlayerHasPermissions(player, def.RequiredPermission)) continue;
                if (def.MaxPerServer >= 0 && PlayerManager.GetPlayerCountBySkill(s.Skill) >= def.MaxPerServer) continue;
                if (held.Any(h => Clash(h, s.Skill))) continue;

                result.Add(s.Skill);
            }

            return result;
        }

        // Adds up to `count` random non-clashing skills to the player and enables them.
        public static void GrantExtras(CCSPlayerController player, jSkill_PlayerInfo info, int count, ISet<string>? excluded = null)
        {
            if (count <= 0 || player == null || !player.IsValid) return;

            var picked = new List<Skills>();
            for (int i = 0; i < count; i++)
            {
                var candidates = Candidates(player, info, excluded);
                if (candidates.Count == 0) break;

                var choice = candidates[Instance.Random.Next(candidates.Count)];
                picked.Add(choice);
                info.ExtraSkills = [.. info.ExtraSkills, choice];
            }

            foreach (var extra in picked)
            {
                Instance.SkillAction(extra.ToString(), "EnableSkill", [player]);
                if (Config.LoadedConfig.YourSkillChatInfo)
                    player.PrintToChat($" {ChatColors.Gold}{player.GetTranslation("combo_extra_info", $"{ChatColors.Lime}{player.GetSkillName(extra)}{ChatColors.Grey} - {player.GetSkillDescription(extra)}")}");
            }
        }

        // Round draw: each extra slot (up to Combos.SkillsPerPlayer in total) is won with Combos.ExtraSkillChance.
        public static void GrantRoundExtras(CCSPlayerController player, jSkill_PlayerInfo info)
        {
            int slots = Settings.SkillsPerPlayer - 1;
            if (slots <= 0 || info.Skill == Skills.None || IsSolo(info.Skill)) return;

            float chance = Math.Clamp(Settings.ExtraSkillChance, 0f, 1f);
            int won = 0;
            for (int i = 0; i < slots; i++)
            {
                if (Instance.Random.NextDouble() >= chance) break;
                won++;
            }

            if (won > 0)
                GrantExtras(player, info, won);
        }
    }
}
