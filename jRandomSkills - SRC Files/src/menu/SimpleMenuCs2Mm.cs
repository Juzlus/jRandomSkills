using CounterStrikeSharp.API.Core;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Enum;
using static src.jRandomSkills;

namespace src.menu
{
    // CS2MenuManager drawing for SimpleMenu, kept in its own class so the plugin still loads (and SimpleMenu
    // falls back to the WASD menu) when the library is not installed.
    internal static class SimpleMenuCs2Mm
    {
        public static bool IsMenuOpen(CCSPlayerController player) => MenuManager.GetActiveMenu(player) != null;

        public static void Close(CCSPlayerController player) => MenuManager.CloseActiveMenu(player);

        public static void Open(CCSPlayerController player, string title, IReadOnlyList<SimpleMenu.Item> items)
        {
            var type = MenuTypeManager.GetPlayerMenuType(player) ?? MenuTypeManager.GetDefaultMenu();
            var menu = MenuManager.MenuByType(type, title, Instance);

            foreach (var item in items)
            {
                var pick = item.OnPick;
                menu.AddItem(item.Label, (p, option) =>
                {
                    option.PostSelectAction = PostSelectAction.Close;
                    pick(p);
                });
            }

            menu.Display(player, 0);
        }
    }
}
