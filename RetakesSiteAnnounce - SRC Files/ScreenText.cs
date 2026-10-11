using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace RetakesSiteAnnounce;

// Text drawn at a fixed spot of one player's screen without any HUD message: a point_worldtext entity kept in
// front of the eyes, the way CS2-GameHUD (InfoTop and similar plugins) does it. Only the owner receives the entity
// (CheckTransmit), so nobody else sees it floating around.
//
// Lifecycle follows GameHUD: ONE text entity per player, created on first use and kept for the whole map.
// Showing sets its message, hiding blanks it; the entity is only removed when the player leaves. Killing and
// re-creating parented entities mid-round is exactly the kind of thing that can crash the engine.
public sealed class ScreenText
{
    public enum Method { Pawn, Orient }

    public readonly record struct Style(
        float X, float Y, float Z, float FontSize, float UnitsPerPx, string? FontName, float BackgroundBorder);

    private sealed class Entry
    {
        public uint PlayerIndex;
        public uint TextIndex;
        public uint OrientIndex;
        public uint ParentHandle;
        public bool Visible;
        public Method Method;
        public Style Style;
        public Color Color;
    }

    private readonly Dictionary<uint, Entry> _entries = [];

    public bool Any => _entries.Count > 0;
    public bool AnyVisible => _entries.Values.Any(e => e.Visible);

    public static Method ParseMethod(string? name) =>
        string.Equals(name?.Trim(), "orient", StringComparison.OrdinalIgnoreCase) ? Method.Orient : Method.Pawn;

    // X: right, Y: up, Z: distance in front of the eyes (world units at that distance, GameHUD convention).
    public bool Show(CCSPlayerController? player, string text, Color color, Style style, Method method)
    {
        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) return false;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return false;

        if (!_entries.TryGetValue(player.Index, out var entry))
        {
            entry = new Entry { PlayerIndex = player.Index };
            _entries[player.Index] = entry;
        }

        // A different method or a dead text entity: start over for this player.
        var ent = Utilities.GetEntityFromIndex<CPointWorldText>((int)entry.TextIndex);
        if (ent == null || !ent.IsValid || entry.Method != method)
        {
            RemoveEntities(entry);
            entry.Method = method;

            ent = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
            if (ent == null || !ent.IsValid) return false;

            ent.MessageText = "";
            ent.Enabled = true;
            ent.Fullbright = true;
            ent.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
            ent.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_TOP;
            ent.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
            ApplyStyle(ent, style, color);
            ent.DispatchSpawn();

            entry.TextIndex = ent.Index;
            entry.ParentHandle = 0;
        }
        else if (!entry.Style.Equals(style) || entry.Color != color)
        {
            ApplyStyle(ent, style, color);
        }
        entry.Style = style;
        entry.Color = color;

        // Parent: the pawn itself, or a point_orient riding on the pawn. Re-parent when the pawn changed.
        CBaseEntity? parent = pawn;
        if (method == Method.Orient)
        {
            var orient = Utilities.GetEntityFromIndex<CPointOrient>((int)entry.OrientIndex);
            if (orient == null || !orient.IsValid)
            {
                orient = CreateOrient(pawn);
                if (orient == null) return false;
                entry.OrientIndex = orient.Index;
                entry.ParentHandle = 0;
            }
            parent = orient;
        }

        if (parent == null || !parent.IsValid) return false;
        if (entry.ParentHandle != parent.EntityHandle.Raw)
        {
            ent.AcceptInput("SetParent", parent, null, "!activator");
            entry.ParentHandle = parent.EntityHandle.Raw;
        }

        ent.AcceptInput("SetMessage", null, null, text);
        entry.Visible = true;
        Place(pawn, ent, entry, parent);
        return true;
    }

    private static void ApplyStyle(CPointWorldText ent, Style style, Color color)
    {
        ent.FontSize = style.FontSize;
        if (!string.IsNullOrWhiteSpace(style.FontName)) ent.FontName = style.FontName;
        ent.Color = color;
        ent.WorldUnitsPerPx = style.UnitsPerPx;
        bool background = style.BackgroundBorder > 0f;
        ent.DrawBackground = background;
        if (background)
        {
            ent.BackgroundBorderHeight = style.BackgroundBorder;
            ent.BackgroundBorderWidth = style.BackgroundBorder;
        }
    }

    private static void Place(CCSPlayerPawn pawn, CPointWorldText ent, Entry entry, CBaseEntity? parent)
    {
        QAngle angles;
        Vector origin;

        if (entry.Method == Method.Orient && parent != null && parent.IsValid && parent.AbsRotation != null && parent.AbsOrigin != null)
        {
            angles = parent.AbsRotation;
            origin = new Vector(parent.AbsOrigin.X, parent.AbsOrigin.Y, parent.AbsOrigin.Z);
        }
        else
        {
            if (pawn.AbsOrigin == null) return;
            angles = pawn.V_angle;
            origin = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
        }

        AngleVectors(angles, out var forward, out var right, out var up);
        var s = entry.Style;
        Vector pos = origin + forward * s.Z + right * s.X + up * s.Y;
        QAngle textAngles = new(0, angles.Y + 270f, 90f - angles.X);
        ent.Teleport(pos, textAngles);
    }

    private static void AngleVectors(QAngle angles, out Vector forward, out Vector right, out Vector up)
    {
        (float sy, float cy) = MathF.SinCos(angles.Y * MathF.PI / 180f);
        (float sp, float cp) = MathF.SinCos(angles.X * MathF.PI / 180f);
        forward = new Vector(cp * cy, cp * sy, -sp);
        right = new Vector(sy, -cy, 0);
        up = new Vector(sp * cy, sp * sy, cp);
    }

    // A point_orient that turns with the player's eyes on the client; the text rides on it.
    private static CPointOrient? CreateOrient(CCSPlayerPawn pawn)
    {
        if (pawn.AbsOrigin == null) return null;

        var orient = Utilities.CreateEntityByName<CPointOrient>("point_orient");
        if (orient == null || !orient.IsValid) return null;

        orient.Active = true;
        orient.GoalDirection = PointOrientGoalDirectionType_t.eEyesForward;
        orient.DispatchSpawn();
        orient.Teleport(new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z));
        orient.AcceptInput("SetParent", pawn, null, "!activator");
        orient.AcceptInput("SetTarget", pawn, null, "!activator");
        return orient;
    }

    public bool IsVisible(uint playerIndex) => _entries.TryGetValue(playerIndex, out var entry) && entry.Visible;

    // Blank the text; the entity stays for the next call.
    public void Hide(uint playerIndex)
    {
        if (!_entries.TryGetValue(playerIndex, out var entry) || !entry.Visible) return;

        entry.Visible = false;
        var ent = Utilities.GetEntityFromIndex<CPointWorldText>((int)entry.TextIndex);
        if (ent != null && ent.IsValid)
            ent.AcceptInput("SetMessage", null, null, "");
    }

    public void HideAll()
    {
        foreach (var index in _entries.Keys.ToArray())
            Hide(index);
    }

    // The player left: drop their entities.
    public void Remove(uint playerIndex)
    {
        if (_entries.Remove(playerIndex, out var entry))
            RemoveEntities(entry);
    }

    public void RemoveAll()
    {
        foreach (var entry in _entries.Values)
            RemoveEntities(entry);
        _entries.Clear();
    }

    // Map change: the engine has already freed everything.
    public void Clear() => _entries.Clear();

    private static void RemoveEntities(Entry entry)
    {
        try
        {
            var ent = Utilities.GetEntityFromIndex<CPointWorldText>((int)entry.TextIndex);
            if (ent != null && ent.IsValid) ent.Remove();
        }
        catch { }
        try
        {
            var orient = Utilities.GetEntityFromIndex<CPointOrient>((int)entry.OrientIndex);
            if (orient != null && orient.IsValid) orient.Remove();
        }
        catch { }
        entry.TextIndex = 0;
        entry.OrientIndex = 0;
        entry.ParentHandle = 0;
        entry.Visible = false;
    }

    // Pawn method: re-aim every visible text from its owner's current view angles. Dead owners lose their text.
    public void OnTick()
    {
        if (_entries.Count == 0) return;

        foreach (var entry in _entries.Values)
        {
            if (!entry.Visible) continue;

            var player = Utilities.GetPlayerFromIndex((int)entry.PlayerIndex);
            var pawn = player?.PlayerPawn.Value;
            var ent = Utilities.GetEntityFromIndex<CPointWorldText>((int)entry.TextIndex);

            if (player == null || !player.IsValid || pawn == null || !pawn.IsValid || ent == null || !ent.IsValid
                || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            {
                Hide(entry.PlayerIndex);
                continue;
            }

            if (entry.Method == Method.Pawn)
                Place(pawn, ent, entry, pawn);
        }
    }

    // Only the owner may receive their text (and orient helper).
    public void FilterTransmit(CCheckTransmitInfoList infoList)
    {
        if (_entries.Count == 0) return;

        foreach (var (info, player) in infoList)
        {
            if (player == null || !player.IsValid) continue;
            foreach (var entry in _entries.Values)
            {
                if (entry.PlayerIndex == player.Index) continue;
                if (entry.TextIndex != 0 && info.TransmitEntities.Contains((int)entry.TextIndex))
                    info.TransmitEntities.Remove((int)entry.TextIndex);
                if (entry.OrientIndex != 0 && info.TransmitEntities.Contains((int)entry.OrientIndex))
                    info.TransmitEntities.Remove((int)entry.OrientIndex);
            }
        }
    }
}
