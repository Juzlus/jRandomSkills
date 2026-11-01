using System.Net;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using WASDMenuAPI;

namespace src.utils
{
    public static partial class PanoramaHud
    {
        private const int MenuHideTicks = 4;

        [GeneratedRegex("^#[0-9a-fA-F]{6}\\|", RegexOptions.CultureInvariant)]
        private static partial Regex OptionColorRegex();

        public static bool RenderMenu(WasdMenuPlayer menuPlayer)
        {
            var player = menuPlayer.Player;
            if (menuPlayer.MainMenu == null || !Active(player)) return false;

            var view = GetView(player);
            string title = "", select = "", color = "";
            var label = menu.Menu.LabelOf(menuPlayer.MainMenu);
            if (label != null)
            {
                title = label.Title;
                select = label.Info;
            }
            else if (PlayerManager.GetPlayer(player) is { } info)
            {
                title = Plain(player.GetSkillName(info.Skill));
                string key = $"{info.Skill.ToString().ToLowerInvariant()}_select_info";
                string text = player.GetTranslation(key);
                select = text == key ? "" : Plain(text);
                color = ColorClass(SkillData.GetInfo(info.Skill)?.Color);
            }

            ApplyLook(player, view);
            SetVar(player, view, MenuId, "title", title);
            SetVar(player, view, MenuId, "info", select);
            SetClass(player, view, MenuId, "no-info", select == "");
            SwapClass(player, view, MenuId, ref view.MenuColor, color);

            int position = (menuPlayer.CurrentChoice?.Value.Index ?? 0) + 1;
            int total = menuPlayer.MainMenu.Options?.Count ?? 0;
            RenderSegments(player, view, FootIds,
            [
                (Plain(player.GetTranslationWithoutIlliterate("menu_controlls_scroll")), ""),
                ("·", ""),
                (Plain(player.GetTranslationWithoutIlliterate("menu_controlls_select")), ""),
                ($"·  {position}/{total}", "")
            ], view.FootColors);

            var node = menuPlayer.MenuStart;
            int visible = Math.Min(menuPlayer.VisibleOptions, MenuSlots);
            for (int i = 0; i < MenuSlots; i++)
            {
                string id = MenuRowIds[i];
                if (i >= visible || node == null)
                {
                    SetClass(player, view, id, OffClass, true);
                    continue;
                }

                string raw = (node.Value.OptionDisplay ?? "")
                    .Replace("​", "").Replace("‌", "").Replace("‪", "").Replace("‬", "");
                string rowColor = "";
                if (OptionColorRegex().IsMatch(raw))
                {
                    rowColor = ColorClass(raw[..7]);
                    raw = raw[8..];
                }

                SetVar(player, view, id, "text", WebUtility.HtmlDecode(raw));
                SetClass(player, view, id, "is-sel", node == menuPlayer.CurrentChoice);
                SwapClass(player, view, id, ref view.RowColors[i], rowColor);
                SetClass(player, view, id, OffClass, false);
                node = node.Next;
            }

            if (!view.MenuShown)
            {
                view.MenuShown = true;
                SetClass(player, view, SkillId, OffClass, true);
                SetClass(player, view, MenuId, OffClass, false);
            }
            view.LastMenuTick = Server.TickCount;
            return true;
        }

        public static void NoteOtherHtml(CCSPlayerController player) =>
            GetView(player).OtherHtmlUntil = Server.TickCount + 15;

        public static bool OtherHtmlActive(CCSPlayerController player) =>
            Enabled && views.TryGetValue(player.Slot, out var view) && view.Key == player.Key() && view.OtherHtmlUntil > Server.TickCount;

        private static void TickMenu(CCSPlayerController player, View view, int tick)
        {
            SetClass(player, view, ScopeId, "is-other", view.OtherHtmlUntil > tick);
            if (!view.MenuShown || tick - view.LastMenuTick <= MenuHideTicks) return;

            view.MenuShown = false;
            SetClass(player, view, MenuId, OffClass, true);
            if (view.SkillShown)
                SetClass(player, view, SkillId, OffClass, false);
        }
    }
}
