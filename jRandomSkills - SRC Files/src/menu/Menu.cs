using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Runtime.CompilerServices;
using src.player;
using src.utils;
using WASDSharedAPI;
using static src.jRandomSkills;

namespace src.menu
{
    public static class Menu
    {
        public sealed record Label(string Kind, string Title, string Info, string Color);

        private const string SkillsKind = "skills";
        private const string LanguageKind = "language";
        private const string TitleColor = "#FFFFFF";
        private const string CurrentColor = "#00FF00";

        private static readonly ConditionalWeakTable<IWasdMenu, Label> labels = new();

        public static Label? LabelOf(IWasdMenu? menu) => menu != null && labels.TryGetValue(menu, out var label) ? label : null;

        private static bool ToggleOff(CCSPlayerController player, string kind)
        {
            if (!SkillUtils.HasMenu(player)) return false;

            if (WASDMenuAPI.WASDMenuAPI.Players.TryGetValue(player.SteamID, out var menuPlayer)
                && LabelOf(menuPlayer.MainMenu) is { } label)
            {
                SkillUtils.CloseMenu(player);
                return label.Kind == kind;
            }
            return true;
        }

        private static IWasdMenu? Open(CCSPlayerController player, string kind, string title, string info)
        {
            var menu = SkillUtils.CreateListMenu(player, title, TitleColor, info);
            if (menu != null)
                labels.AddOrUpdate(menu, new Label(kind, title, info, TitleColor));
            return menu;
        }

        public static void DisplaySkillsList(CCSPlayerController player)
        {
            if (ToggleOff(player, SkillsKind)) return;

            var menu = Open(player, SkillsKind, player.GetTranslationWithoutIlliterate("skills_menu"), "");
            if (menu == null) return;

            var skills = SkillData.Skills
                .Where(s => s.Skill != Skills.None)
                .Select(s => (Info: s, Name: player.GetSkillName(s.Skill)))
                .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase);

            foreach (var (skillInfo, name) in skills)
            {
                string color = skillInfo.Color is { Length: 7 } hex && hex[0] == '#' ? hex + "|" : "";
                menu.Add($"{color}‪{System.Net.WebUtility.HtmlEncode(name)}‬", (p, option) =>
                {
                    SkillUtils.CloseMenu(p);
                    SkillUtils.PrintToChat(p, $"{ChatColors.DarkRed}{p.GetSkillName(skillInfo.Skill)}{ChatColors.Lime}: {p.GetSkillDescription(skillInfo.Skill)}", border: "");
                });
            }

            SkillUtils.OpenMenu(player, menu);
        }

        public static void DisplayLanguageList(CCSPlayerController player)
        {
            if (ToggleOff(player, LanguageKind)) return;

            var menu = Open(player, LanguageKind, player.GetTranslationWithoutIlliterate("language_menu"), "");
            if (menu == null) return;

            string current = Localization.LangCodeOf(player);
            var languages = Localization.LoadedLanguages()
                .Select(code => (Code: code, Name: LanguageName(code)))
                .OrderBy(l => l.Code != current)
                .ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase);

            foreach (var (code, name) in languages)
            {
                string color = code == current ? CurrentColor + "|" : "";
                menu.Add($"{color}‪{System.Net.WebUtility.HtmlEncode(name)}‬", (p, option) =>
                {
                    SkillUtils.CloseMenu(p);
                    Localization.ChangePlayerLanguage(p, code);
                    SkillUtils.PrintToChat(p, $"{ChatColors.Lime}{p.GetTranslationWithoutIlliterate("language_changed", name)}", border: "");
                });
            }

            SkillUtils.OpenMenu(player, menu);
        }

        private static string LanguageName(string code)
        {
            string name = Localization.GetTranslation("language_name", code, null, false);
            return name == "language_name" ? code.ToUpperInvariant() : name;
        }
    }
}
