using CounterStrikeSharp.API.Core;
using System.Diagnostics.CodeAnalysis;
using WASDSharedAPI;

namespace WASDMenuAPI.Classes;

public class WasdMenu : IWasdMenu
{
    [AllowNull] public string Title { get; set; } = "";
    [AllowNull] public string ItemText { get; set; } = "";
    [AllowNull] public string ItemHoverText { get; set; } = "";
    [AllowNull] public string ControlText { get; set; } = "";
    public LinkedList<IWasdMenuOption>? Options { get; set; } = new();
    public LinkedListNode<IWasdMenuOption>? Prev { get; set; } = null;
    public LinkedListNode<IWasdMenuOption> Add(string display, Action<CCSPlayerController, IWasdMenuOption> onChoice)
    {
        Options ??= new();
        WasdMenuOption newOption = new()
        {
            OptionDisplay = display,
            OnChoose = onChoice,
            Index = Options.Count,
            Parent = this
        };
        return Options.AddLast(newOption);
    }
}