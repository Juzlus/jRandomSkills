using CounterStrikeSharp.API.Core;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Enum;
using src.utils;
using static src.jRandomSkills;

namespace src.modules
{
    // The !guns menu drawn by CS2MenuManager (the shared library many servers already run for their
    // other plugins). It follows the server's CS2MenuManager config.toml colours and the menu style each
    // player picked with !mm. Kept in its own class so the plugin still loads, and GunsModule falls back
    // to the built-in WASD menu, when the library is not installed.
    internal static class GunsMenuCs2Mm
    {
        public static bool IsMenuOpen(CCSPlayerController player) => MenuManager.GetActiveMenu(player) != null;

        public static void Open(CCSPlayerController player)
        {
            var type = MenuTypeManager.GetPlayerMenuType(player) ?? MenuTypeManager.GetDefaultMenu();
            var main = MenuManager.MenuByType(type, player.GetTranslationWithoutIlliterate("guns_title"), Instance);

            foreach (var slot in GunsModule.Slots)
                AddSlot(main, type, player, slot);

            main.Display(player, 0);
        }

        private static void AddSlot(BaseMenu main, Type type, CCSPlayerController player, GunsModule.Slot slot)
        {
            string slotName = player.GetTranslationWithoutIlliterate(GunsModule.SlotKey(slot));
            string? current = GunsModule.CurrentChoice(player, slot);
            string currentName = string.IsNullOrEmpty(current) ? player.GetTranslationWithoutIlliterate("guns_default") : GunsModule.DisplayName(current);

            main.AddItem($"{slotName}: {currentName}", (p, option) =>
            {
                option.PostSelectAction = PostSelectAction.Nothing;

                var sub = MenuManager.MenuByType(type, slotName, Instance);
                sub.PrevMenu = main;

                sub.AddItem(p.GetTranslationWithoutIlliterate("guns_random"), (p2, option2) =>
                {
                    option2.PostSelectAction = PostSelectAction.Nothing;
                    GunsModule.SetChoice(p2, slot, GunsModule.Random);
                    Open(p2);
                });

                foreach (var weapon in GunsModule.WeaponsFor(slot))
                {
                    string chosen = weapon;
                    sub.AddItem(GunsModule.DisplayName(chosen), (p2, option2) =>
                    {
                        option2.PostSelectAction = PostSelectAction.Nothing;
                        GunsModule.SetChoice(p2, slot, chosen);
                        Open(p2);
                    });
                }

                sub.Display(p, 0);
            });
        }
    }
}
