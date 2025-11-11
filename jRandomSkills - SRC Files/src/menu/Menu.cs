using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Text.RegularExpressions;
using src.utils;

namespace src.menu
{
    public static class Menu
    {
        public static void DisplaySkillsList(CCSPlayerController player)
        {
            CenterHtmlMenu menu = new($"[ ★ {player.GetTranslation("skills_menu")} ★ ]", Instance);
            
            foreach (var skillInfo in SkillData.Skills)
            {
                string skillName = $"{player.GetSkillName(skillInfo.Skill)}";
                menu.AddMenuOption($" ★ {skillName}", (player, option) =>
                {
                    string selectedSkillName = option.Text;
                    string pattern = "\\[/?color\\b[^\\]]*\\]";
                    string cleanSkillName = Regex.Replace(selectedSkillName, pattern, "");
                    string skillName = cleanSkillName.Replace($" ★ ", "");
                    string skillDesc = player.GetSkillDescription(skillInfo.Skill);
                    SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{skillName}{ChatColors.Lime}: {skillDesc}", border: "");
                    MenuManager.CloseActiveMenu(player);
                });
            }

            MenuManager.OpenCenterHtmlMenu(Instance, player, menu);
        }
    }
}