using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using src.utils;
using WASDSharedAPI;
using static src.jRandomSkills;

namespace src.menu
{
    // One-level pick menu used by the weapon-round and map votes. Drawn by CS2MenuManager when the server has
    // it (same Modules.Guns.MenuStyle switch as !guns), otherwise by the plugin's own WASD menu.
    public static class SimpleMenu
    {
        public sealed record Item(string Label, Action<CCSPlayerController> OnPick);

        private static bool cs2MenuManagerBroken;

        private static bool UsesCs2MenuManager =>
            Config.LoadedConfig.Modules.Guns.MenuStyle.Trim().Equals("CS2MenuManager", StringComparison.OrdinalIgnoreCase);

        // True while the player has a CS2MenuManager menu open (E selects there, so it must not fire a skill).
        public static bool IsExternalMenuOpen(CCSPlayerController player)
        {
            if (cs2MenuManagerBroken || !UsesCs2MenuManager) return false;
            try { return SimpleMenuCs2Mm.IsMenuOpen(player); }
            catch { cs2MenuManagerBroken = true; return false; }
        }

        public static void Open(CCSPlayerController player, string title, string subtitle, IReadOnlyList<Item> items)
        {
            if (player == null || !player.IsValid || player.IsBot || items.Count == 0) return;

            if (UsesCs2MenuManager && !cs2MenuManagerBroken)
            {
                try
                {
                    SimpleMenuCs2Mm.Open(player, title, items);
                    return;
                }
                catch (Exception ex)
                {
                    cs2MenuManagerBroken = true;
                    Instance.Logger.LogWarning("[TiredPowers] CS2MenuManager is not available ({Message}); vote menus use the built-in WASD menu instead.", (ex.InnerException ?? ex).Message);
                }
            }

            var manager = SkillUtils.MenuManager();
            if (manager == null) return;

            // A skill target menu may be open; replace it rather than stacking.
            if (manager.HasMenu(player)) manager.CloseMenu(player);

            var menu = CreateStyledMenu(manager, player, title, subtitle);
            foreach (var item in items)
            {
                var pick = item.OnPick;
                menu.Add(System.Net.WebUtility.HtmlEncode(item.Label), (p, _) =>
                {
                    manager.CloseMenu(p);
                    pick(p);
                });
            }

            manager.OpenMainMenu(player, menu);
        }

        public static void Close(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.IsBot) return;

            if (UsesCs2MenuManager && !cs2MenuManagerBroken)
            {
                try { SimpleMenuCs2Mm.Close(player); } catch { cs2MenuManagerBroken = true; }
            }

            var manager = SkillUtils.MenuManager();
            if (manager != null && manager.HasMenu(player)) manager.CloseMenu(player);
        }

        // Same HUD styling as the skill selection menus.
        private static IWasdMenu CreateStyledMenu(IWasdMenuManager manager, CCSPlayerController player, string title, string subtitle)
        {
            var config = Config.LoadedConfig.HtmlHudCustomisation;

            string header = $"<font class='fontWeight-Bold fontSize-{config.SkillLineSize}' color='{config.HeaderLineColor}'>‪{System.Net.WebUtility.HtmlEncode(title)}‬</font><br>";
            string info = string.IsNullOrEmpty(config.WSADMenuSelectInfoLineSize) || string.IsNullOrEmpty(subtitle) ? "" :
                $"<font class='fontSize-{config.WSADMenuSelectInfoLineSize}' color='{config.WSADMenuSelectInfoLineColor}'>{System.Net.WebUtility.HtmlEncode(subtitle)}</font><br>";

            string emptySymbol = "<font class='fontSize-ml'> </font>";
            string controls = string.IsNullOrEmpty(config.WSADMenuControllsLineSize) ? "" :
                $"{emptySymbol}<font class='fontSize-{config.WSADMenuControllsLineSize}' color='{config.WSADMenuControllsLineColor1}'>{player.GetTranslationWithoutIlliterate("menu_controlls_scroll")}</font>"
                + $"<font class='fontSize-{config.WSADMenuControllsLineSize}' color='{config.WSADMenuControllsLineColor2}'>{player.GetTranslationWithoutIlliterate("menu_controlls_padding")}</font>"
                + $"<font class='fontSize-{config.WSADMenuControllsLineSize}' color='{config.WSADMenuControllsLineColor3}'>{player.GetTranslationWithoutIlliterate("menu_controlls_select")}</font>{emptySymbol}<br>";

            string itemText = $"<font class='fontSize-{config.WSADMenuItemLineSize}' color='{config.WSADMenuItemLineColor}'>{{0}}</font><br>";
            string itemHoverText = $"<font class='fontSize-{config.WSADMenuItemLineSize}'><font color='purple'>[ </font><font color='{config.WSADMenuItemHoverLineColor}'>{{0}}</font><font color='purple'> ]</font></font><br>";

            return manager.CreateMenu(header + info, itemText, itemHoverText, controls);
        }
    }
}
