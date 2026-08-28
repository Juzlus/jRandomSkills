using System.Runtime.InteropServices;
using System.Text;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;

namespace jRandomSkills;

internal unsafe sealed class CUtlString : IDisposable
{
    private nint _struct;
    private nint _data;

    public CUtlString(string value)
    {
        value ??= string.Empty;

        byte[] bytes = Encoding.UTF8.GetBytes(value + '\0');

        _data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, _data, bytes.Length);

        _struct = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(_struct, _data);
    }

    public nint Ptr => _struct;

    public void Dispose()
    {
        if (_struct != 0)
        {
            Marshal.FreeHGlobal(_struct);
            _struct = 0;
        }

        if (_data != 0)
        {
            Marshal.FreeHGlobal(_data);
            _data = 0;
        }
    }
}

internal unsafe sealed class CustomHudNatives
{
    private readonly MemoryFunctionVoid<nint, nint, nint, nint> _setDialogVariableString;
    private readonly MemoryFunctionVoid<nint, nint, nint, byte> _setHasClass;
    private readonly MemoryFunctionVoid<nint, uint, nint, nint, nint> _setDialogVariableStringForPlayer;
    private readonly MemoryFunctionVoid<nint, uint, nint, nint, byte> _setHasClassForPlayer;
    private readonly MemoryFunctionVoid<nint, uint, byte> _setInputCaptureEnabled;

    // Te dwie zwracają indeks przez wskaźnik (out ushort*), więc parametr
    // to nint (adres), a NIE ushort jako wartość — stąd brak problemu tutaj.
    private readonly MemoryFunctionWithReturn<nint, nint, nint, bool> _internPanelId;
    private readonly MemoryFunctionWithReturn<nint, nint, nint, bool> _internDialogVarName;
    private readonly MemoryFunctionVoid<nint, uint, uint, nint> _writeDialogVariableToState;

    public CustomHudNatives()
    {
        string setDialogVariableString = GameData.GetSignature("CCSCustomHudLayout::SetDialogVariableString");
        string setHasClass = GameData.GetSignature("CCSCustomHudLayout::SetHasClass");
        string setDialogVariableStringForPlayer = GameData.GetSignature("CCSCustomHudLayout::SetDialogVariableStringForPlayer");
        string setHasClassForPlayer = GameData.GetSignature("CCSCustomHudLayout::SetHasClassForPlayer");
        string setInputCaptureEnabled = GameData.GetSignature("CCSCustomHudLayout::SetInputCaptureEnabled");

        string internPanelId = GameData.GetSignature("CCSCustomHudLayout::InternPanelId");
        string internDialogVarName = GameData.GetSignature("CCSCustomHudLayout::InternDialogVarName");
        string writeDialogVariableToState = GameData.GetSignature("CCSCustomHudLayout::WriteDialogVariableToState");

        _setDialogVariableString = new MemoryFunctionVoid<nint, nint, nint, nint>(setDialogVariableString);
        _setHasClass = new MemoryFunctionVoid<nint, nint, nint, byte>(setHasClass);
        _setDialogVariableStringForPlayer = new MemoryFunctionVoid<nint, uint, nint, nint, nint>(setDialogVariableStringForPlayer);
        _setHasClassForPlayer = new MemoryFunctionVoid<nint, uint, nint, nint, byte>(setHasClassForPlayer);
        _setInputCaptureEnabled = new MemoryFunctionVoid<nint, uint, byte>(setInputCaptureEnabled);

        _internPanelId = new MemoryFunctionWithReturn<nint, nint, nint, bool>(internPanelId);
        _internDialogVarName = new MemoryFunctionWithReturn<nint, nint, nint, bool>(internDialogVarName);
        _writeDialogVariableToState = new MemoryFunctionVoid<nint, uint, uint, nint>(writeDialogVariableToState);
    }

    public void SetDialogVariableString(CBaseEntity entity, string panelId, string variableName, string value)
    {
        if (entity == null || !entity.IsValid)
            return;

        nint self = entity.Handle;
        if (self == IntPtr.Zero)
            return;

        using var pPanel = new CUtlString(panelId);
        using var pName = new CUtlString(variableName);
        using var pValue = new CUtlString(value);

        _setDialogVariableString.Invoke(self, pPanel.Ptr, pName.Ptr, pValue.Ptr);
    }

    public void SetHasClass(CBaseEntity entity, string panelId, string className, bool hasClass)
    {
        if (entity == null || !entity.IsValid)
            return;

        nint self = entity.Handle;
        if (self == IntPtr.Zero)
            return;

        using var pPanel = new CUtlString(panelId);
        using var pClass = new CUtlString(className);

        _setHasClass.Invoke(self, pPanel.Ptr, pClass.Ptr, (byte)(hasClass ? 1 : 0));
    }

    public void SetDialogVariableStringForPlayer(CBaseEntity entity, uint slot, string panelId, string variableName, string value)
    {
        if (entity == null || !entity.IsValid)
            return;

        if (slot >= 64)
            return;

        nint self = entity.Handle;
        if (self == IntPtr.Zero)
            return;

        using var pPanel = new CUtlString(panelId);
        using var pName = new CUtlString(variableName);
        using var pValue = new CUtlString(value);

        _setDialogVariableStringForPlayer.Invoke(self, slot, pPanel.Ptr, pName.Ptr, pValue.Ptr);
    }

    public void SetHasClassForPlayer(CBaseEntity entity, uint slot, string panelId, string className, bool hasClass)
    {
        if (entity == null || !entity.IsValid)
            return;

        if (slot >= 64)
            return;

        nint self = entity.Handle;
        if (self == IntPtr.Zero)
            return;

        using var pPanel = new CUtlString(panelId);
        using var pClass = new CUtlString(className);

        _setHasClassForPlayer.Invoke(self, slot, pPanel.Ptr, pClass.Ptr, (byte)(hasClass ? 1 : 0));
    }

    public void SetInputCaptureEnabled(CBaseEntity entity, uint slot, bool enabled)
    {
        if (entity == null || !entity.IsValid)
            return;

        if (slot >= 64)
            return;

        nint self = entity.Handle;
        if (self == IntPtr.Zero)
            return;

        _setInputCaptureEnabled.Invoke(self, slot, (byte)(enabled ? 1 : 0));
    }
}