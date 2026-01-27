using CounterStrikeSharp.API.Core;
using WASDSharedAPI;

namespace WASDMenuAPI.Classes;

public class WasdManager : IWasdMenuManager
{
    public void OpenMainMenu(CCSPlayerController? player, IWasdMenu? menu)
    {
        if(!TryGetPlayer(player, out var menuPlayer)) return;
        menuPlayer?.OpenMainMenu((WasdMenu?)menu);
    }

    public void CloseMenu(CCSPlayerController? player)
    {
        if (!TryGetPlayer(player, out var menuPlayer)) return;
        menuPlayer?.OpenMainMenu(null);
    }

    public void CloseSubMenu(CCSPlayerController? player)
    {
        if (!TryGetPlayer(player, out var menuPlayer)) return;
        menuPlayer?.CloseSubMenu();
    }

    public void CloseAllSubMenus(CCSPlayerController? player)
    {
        if (!TryGetPlayer(player, out var menuPlayer)) return;
        menuPlayer?.CloseAllSubMenus();
    }

    public void OpenSubMenu(CCSPlayerController? player, IWasdMenu? menu)
    {
        if (!TryGetPlayer(player, out var menuPlayer)) return;
        menuPlayer?.OpenSubMenu(menu);
    }

    public IWasdMenu CreateMenu(string title, string itemText, string itemHoverText, string controlText)
    {
        WasdMenu menu = new()
        {
            Title = title,
            ItemText = itemText,
            ItemHoverText = itemHoverText,
            ControlText = controlText
        };
        return menu;
    }

    public bool HasMenu(CCSPlayerController? player)
    {
        if (TryGetPlayer(player, out var menuPlayer) && menuPlayer != null)
            return menuPlayer.MainMenu != null;
        return false;
    }

    public void UpdateActiveMenu(CCSPlayerController? player, Dictionary<string, Action<CCSPlayerController, IWasdMenuOption>> list)
    {
        if (!TryGetPlayer(player, out var menuPlayer)) return;
        menuPlayer?.UpdateActiveMenu(list);
    }

    private static bool TryGetPlayer(CCSPlayerController? player, out WasdMenuPlayer? menuPlayer)
    {
        menuPlayer = null!;
        if (player == null || !player.IsValid || player.IsBot || player.SteamID == 0) return false;
        return WASDMenuAPI.Players.TryGetValue(player.SteamID, out menuPlayer);
    }
}