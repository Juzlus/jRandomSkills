using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using WASDMenuAPI.Classes;
using WASDSharedAPI;

namespace WASDMenuAPI;

public class WasdMenuPlayer
{
    public required CCSPlayerController Player { get; set; }
    public WasdMenu? MainMenu = null;
    public LinkedListNode<IWasdMenuOption>? CurrentChoice = null;
    public LinkedListNode<IWasdMenuOption>? MenuStart = null;
    public string CenterHtml = "";
    public int VisibleOptions = 3;
    public PlayerButtons Buttons { get; set; }

    public int scrollIndex = 0;
    public int maxLength = 21;
    public int scrollJump = 3;

    public void OpenMainMenu(WasdMenu? menu)
    {
        if (menu == null)
        {
            MainMenu = null;
            CurrentChoice = null;
            CenterHtml = "";
            return;
        }
        MainMenu = menu;
        CurrentChoice = MainMenu.Options?.First;
        MenuStart = CurrentChoice;
        UpdateCenterHtml();
    }

    public void OpenSubMenu(IWasdMenu? menu)
    {
        if (menu == null)
        {
            CurrentChoice = MainMenu?.Options?.First;
            MenuStart = CurrentChoice;
            UpdateCenterHtml();
            return;
        }

        CurrentChoice = menu.Options?.First;
        MenuStart = CurrentChoice;
        UpdateCenterHtml();
    }
    public void GoBackToPrev(LinkedListNode<IWasdMenuOption>? menu)
    {
        if (menu == null)
        {
            CurrentChoice = MainMenu?.Options?.First;
            MenuStart = CurrentChoice;
            UpdateCenterHtml();
            return;
        }

        CurrentChoice = menu;
        if (CurrentChoice.Value.Index >= 5 )
        {
            MenuStart = CurrentChoice;
            for (int i = 0; i < VisibleOptions; i++)
            {
                MenuStart = MenuStart?.Previous;
            }
        }
        else
            MenuStart = CurrentChoice.List?.First;
        UpdateCenterHtml();
    }

    public void CloseSubMenu()
    {
        if (CurrentChoice?.Value.Parent?.Prev == null)
            return;
        GoBackToPrev(CurrentChoice?.Value.Parent.Prev);
    }

    public void CloseAllSubMenus()
    {
        OpenSubMenu(null);
    }
    
    public void Choose()
    {
        CurrentChoice?.Value.OnChoose?.Invoke(Player, CurrentChoice.Value);
    }

    public void ScrollDown()
    {
        if(CurrentChoice == null || MainMenu == null)
            return;
        scrollIndex = 0;
        CurrentChoice = CurrentChoice.Next ?? CurrentChoice.List?.First;
        MenuStart = CurrentChoice!.Value.Index >= VisibleOptions ? MenuStart!.Next : CurrentChoice.List?.First;
        UpdateCenterHtml();
    }
    
    public void ScrollUp()
    {
        if(CurrentChoice == null || MainMenu == null)
            return;
        scrollIndex = 0;
        CurrentChoice = CurrentChoice.Previous ?? CurrentChoice.List?.Last;
        if (CurrentChoice == CurrentChoice?.List?.Last && CurrentChoice?.Value.Index >= VisibleOptions)
        {
            MenuStart = CurrentChoice;
            for (int i = 0; i < VisibleOptions - 1; i++)
                MenuStart = MenuStart?.Previous;
        }
        else
            MenuStart = CurrentChoice!.Value.Index >= VisibleOptions ? MenuStart!.Previous : CurrentChoice.List?.First;
        UpdateCenterHtml();
    }

    private void UpdateCenterHtml()
    {
        if (CurrentChoice == null || MainMenu == null || MenuStart == null)
            return;

        StringBuilder builder = new();
        string itemText = "";
        string itemHoverText = "";
        string endControl = "";
        LinkedListNode<IWasdMenuOption>? option = MenuStart;

        if (!string.IsNullOrEmpty(option?.Value?.Parent?.Title))
            builder.AppendLine(option.Value.Parent?.Title ?? "");

        if (!string.IsNullOrEmpty(option?.Value?.Parent?.ItemText))
            itemText = option?.Value?.Parent?.ItemText ?? "";

        if (!string.IsNullOrEmpty(option?.Value?.Parent?.ItemHoverText))
            itemHoverText = option?.Value?.Parent?.ItemHoverText ?? "";

        if (!string.IsNullOrEmpty(option?.Value?.Parent?.ControlText))
            endControl = option?.Value?.Parent?.ControlText ?? "";

        int shown = 0;
        int maxOptions = VisibleOptions;
        
        while (shown < maxOptions && option != null && option.Value != null)
        {
            /*            if (shown == maxOptions - 1 && option.Next != null)
                        {
                            builder.AppendLine(
                                $"<img src='https://raw.githubusercontent.com/oqyh/cs2-Kill-Sound-GoldKingZ/main/Resources/arrow.gif' class=''> <img src='https://raw.githubusercontent.com/oqyh/cs2-Kill-Sound-GoldKingZ/main/Resources/arrow.gif' class=''> <img src='https://raw.githubusercontent.com/oqyh/cs2-Kill-Sound-GoldKingZ/main/Resources/arrow.gif' class=''> <br>");
                            break;
                        }*/

            string input = option.Value.OptionDisplay ?? string.Empty;
            string color = string.Empty;
            string text = input;

            if (input.Contains('|'))
            {
                string[] parts = input.Split("|", 2);
                color = parts[0].StartsWith("#") && parts[0].Length == 7 ? parts[0] : string.Empty;
                text = parts.Length > 1 ? parts[1] : "";
            }

            string cleanText = text.Replace("\u200B", "")
                                   .Replace("\u200C", "")
                                   .Replace("\u202A", "")
                                   .Replace("\u202C", "");

            int sepIndex = cleanText.IndexOf(" : ");
            string namePart = sepIndex != -1 ? cleanText.Substring(0, sepIndex) : cleanText;
            string statsPart = sepIndex != -1 ? cleanText.Substring(sepIndex) : "";

            int start = 0;
            int length = cleanText.Length;

            if (option == CurrentChoice)
            {
                if (cleanText.Length > maxLength)
                {
                    int remaining = cleanText.Length - scrollIndex;
                    if (remaining <= maxLength)
                    {
                        start = scrollIndex;
                        length = remaining;
                        if (Server.TickCount % 32 == 0) scrollIndex = 0;
                    }
                    else
                    {
                        start = scrollIndex;
                        length = maxLength;
                        if (Server.TickCount % 32 == 0) scrollIndex += scrollJump;
                    }
                }
                else
                {
                    start = 0;
                    length = Math.Min(cleanText.Length, maxLength);
                }
            }
            else
            {
                start = 0;
                length = Math.Min(cleanText.Length, maxLength);
            }

            int nameLength = namePart.Length;

            int nameSliceStart = Math.Max(start, 0);
            int nameSliceEnd = Math.Min(start + length, nameLength);
            int nameSliceLength = Math.Max(0, nameSliceEnd - nameSliceStart);
            string visibleName = nameSliceLength > 0 ? cleanText.Substring(nameSliceStart, nameSliceLength) : "";

            int statsSliceStart = Math.Max(start, nameLength);
            int statsSliceEnd = Math.Min(start + length, cleanText.Length);
            int statsSliceLength = Math.Max(0, statsSliceEnd - statsSliceStart);

            string visibleStats = statsSliceLength > 0 ? cleanText.Substring(statsSliceStart, statsSliceLength) : "";
            string finalOptionText = (visibleName.Length > 0 ? $"\u202A{visibleName}\u202C" : "") + visibleStats;

            if (option == CurrentChoice)
                builder.AppendLine(string.Format(itemHoverText, finalOptionText));
            else
                builder.AppendLine(string.Format(itemText, $"<font {(string.IsNullOrEmpty(color) ? "" : $"color='{color}'")}'>{finalOptionText}</font>"));

            option = option.Next;
            shown++;
        }

        if (!string.IsNullOrEmpty(endControl))
            builder.AppendLine(endControl);

        CenterHtml = builder.ToString();
    }

    public void UpdateActiveMenu(Dictionary<string, Action<CCSPlayerController, IWasdMenuOption>> list)
    {
        if (list.Count == 0)
        {
            OpenMainMenu(null);
            return;
        }

        if (MainMenu == null || MainMenu.Options == null)
            return;

        MainMenu.Options.Clear();
        bool choiceExists = false;

        foreach (var item in list)
        {
            WasdMenuOption newOption = new()
            {
                OptionDisplay = item.Key,
                OnChoose = item.Value,
                Index = MainMenu.Options.Count,
                Parent = MainMenu
            };
            MainMenu.Options.AddLast(newOption);

            if (CurrentChoice?.Value?.OptionDisplay == item.Key)
            {
                CurrentChoice = MainMenu.Options?.Last;
                choiceExists = true;
            }
        }
        if (!choiceExists)
            CurrentChoice = MainMenu.Options?.First;

        if (CurrentChoice != null)
        {
            if (CurrentChoice.Value.Index >= VisibleOptions)
            {
                MenuStart = CurrentChoice;
                for (int i = 0; i < VisibleOptions - 1; i++)
                    MenuStart = MenuStart?.Previous;
            }
            else
                MenuStart = MainMenu.Options?.First;
        }

        UpdateCenterHtml();
    }

    private static string SafeSubstring(string text, int start, int length)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (start < 0) start = 0;
        if (start >= text.Length) return "";
        if (start + length > text.Length)
            length = text.Length - start;
        return text.Substring(start, length);
    }
}