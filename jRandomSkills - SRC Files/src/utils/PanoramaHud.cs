using System.Net;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using src.player;
using static src.jRandomSkills;

namespace src.utils
{
    public static partial class PanoramaHud
    {
        private const string ScopeId = "js-scope";
        private const string SkillId = "js-skill";
        private const string MenuId = "js-menu";
        private const string OffClass = "js-off";
        private const int SegmentSlots = 4;
        private const int OneLineChars = 30;
        private const int CurseSlots = 5;
        private const int MenuSlots = 10;
        private const int HideAfterTicks = 12;
        private const int ParseCacheLimit = 1024;
        private static readonly string[] DescIds = [.. Enumerable.Range(0, SegmentSlots).Select(i => "js-sd" + i)];
        private static readonly string[] StatusIds = [.. Enumerable.Range(0, SegmentSlots).Select(i => "js-ss" + i)];
        private static readonly string[] CurseIds = [.. Enumerable.Range(0, CurseSlots).Select(i => "js-c" + i)];
        private static readonly string[] MenuRowIds = [.. Enumerable.Range(0, MenuSlots).Select(i => "js-m" + i)];
        private static readonly string[] FootIds = [.. Enumerable.Range(0, SegmentSlots).Select(i => "js-mf" + i)];
        private static readonly HashSet<string> OffByDefault = [SkillId, MenuId, .. DescIds, .. StatusIds, .. CurseIds, .. MenuRowIds, .. FootIds];
        private static readonly HashSet<string> FontNames = ["stratum", "light", "cond", "boldcond", "mono", "noto", "serif", "notomono"];
        private static readonly Dictionary<string, string> plainCache = [];
        private static readonly Dictionary<string, string> colorCache = [];
        private static readonly Dictionary<string, List<(string Text, string Color)>> segmentCache = [];
        private static readonly List<(string Text, string Color)> NoSegments = [];

        [GeneratedRegex("color=['\"]?(#[0-9a-fA-F]{6})", RegexOptions.CultureInvariant)]
        private static partial Regex ColorRegex();

        [GeneratedRegex(@"#FF0000['""]?>\s*(\d+)\s*<", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
        private static partial Regex CooldownRegex();

        [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
        private static partial Regex TagRegex();

        [GeneratedRegex(@"<\s*(/?)\s*([a-zA-Z]+)([^>]*)>", RegexOptions.CultureInvariant)]
        private static partial Regex RichTagRegex();

        private sealed class View
        {
            public readonly Dictionary<(string Panel, string Name), bool> Classes = [];
            public readonly Dictionary<(string Panel, string Name), string> Vars = [];
            public uint Key;
            public int LastSkillTick = int.MinValue;
            public bool SkillShown;
            public string SkillColor = "";
            public readonly string[] DescColors = new string[SegmentSlots];
            public readonly string[] StatusColors = new string[SegmentSlots];
            public string FontClass = "";
            public string SizeClass = "";
            public string CooldownClass = "";
            public int CooldownEndTick;
            public int CooldownTotalTicks;
            public readonly long[] CurseKeys = new long[CurseSlots];
            public readonly string[] CurseColors = new string[CurseSlots];
            public bool MenuShown;
            public int LastMenuTick;
            public int OtherHtmlUntil;
            public string MenuColor = "";
            public readonly string[] RowColors = new string[MenuSlots];
            public readonly string[] FootColors = new string[SegmentSlots];
        }

        private static CCSCustomHudLayout? layout;
        private static readonly Dictionary<int, View> views = [];

        public static bool Enabled => Config.LoadedConfig.PanoramaHud?.Enabled == true;

        public static bool Active(CCSPlayerController? player)
        {
            if (!Enabled || player == null || !player.IsValid || player.IsBot || player.IsHLTV) return false;
            if (player.Connected != PlayerConnectedState.Connected) return false;
            return EnsureLayout();
        }

        private static bool EnsureLayout()
        {
            if (layout is { IsValid: true }) return true;

            layout = null;
            foreach (var view in views.Values)
                ResetView(view);

            var created = Utilities.CreateEntityByName<CCSCustomHudLayout>("custom_hud_layout");
            if (created == null || created.Handle == IntPtr.Zero) return false;

            created.StrLayout = Config.LoadedConfig.PanoramaHud!.Layout;
            created.DispatchSpawn();
            if (!created.IsValid) return false;

            layout = created;
            return true;
        }

        private static void ResetView(View view)
        {
            view.Classes.Clear();
            view.Vars.Clear();
            view.SkillShown = false;
            view.SkillColor = "";
            Array.Clear(view.DescColors);
            Array.Clear(view.StatusColors);
            view.FontClass = "";
            view.SizeClass = "";
            view.CooldownClass = "";
            view.CooldownEndTick = 0;
            view.CooldownTotalTicks = 0;
            Array.Clear(view.CurseKeys);
            Array.Clear(view.CurseColors);
            view.MenuShown = false;
            view.MenuColor = "";
            Array.Clear(view.RowColors);
            Array.Clear(view.FootColors);
        }

        private static View GetView(CCSPlayerController player)
        {
            if (!views.TryGetValue(player.Slot, out var view) || view.Key != player.Key())
            {
                view = new View { Key = player.Key() };
                views[player.Slot] = view;
            }
            return view;
        }

        private static void SetClass(CCSPlayerController player, View view, string panelId, string className, bool on)
        {
            if (layout is not { IsValid: true }) return;

            var key = (panelId, className);
            if (!view.Classes.TryGetValue(key, out bool current))
                current = className == OffClass && OffByDefault.Contains(panelId);

            view.Classes[key] = on;
            if (current != on)
                layout.SetHasClassForPlayer(player, panelId, className, on);
        }

        private static void SetVar(CCSPlayerController player, View view, string panelId, string name, string value)
        {
            if (layout is not { IsValid: true }) return;

            var key = (panelId, name);
            if (view.Vars.TryGetValue(key, out var current) && current == value) return;

            view.Vars[key] = value;
            layout.SetDialogVariableStringForPlayer(player, panelId, name, value);
        }

        private static void SwapClass(CCSPlayerController player, View view, string panelId, ref string current, string next)
        {
            if (current == next) return;
            if (current != "") SetClass(player, view, panelId, current, false);
            if (next != "") SetClass(player, view, panelId, next, true);
            current = next;
        }

        private static string ColorClass(string? color)
        {
            if (string.IsNullOrEmpty(color)) return "";
            if (colorCache.TryGetValue(color, out var cached)) return cached;

            var match = ColorRegex().Match(color);
            string hex = match.Success ? match.Groups[1].Value : color.Trim();
            string result = hex.Length == 7 && hex[0] == '#' ? "sc-" + hex[1..].ToLowerInvariant() : "";
            return Remember(colorCache, color, result);
        }

        private static T Remember<T>(Dictionary<string, T> cache, string key, T value)
        {
            if (cache.Count >= ParseCacheLimit) cache.Clear();
            cache[key] = value;
            return value;
        }

        private static string Plain(string? html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            if (plainCache.TryGetValue(html, out var cached)) return cached;

            string text = TagRegex().Replace(html.Replace("<br>", " ").Replace("<br/>", " "), "");
            return Remember(plainCache, html, WebUtility.HtmlDecode(text).Trim().TrimEnd(':').Trim());
        }

        private static List<(string Text, string Color)> Segments(string? html)
        {
            if (string.IsNullOrEmpty(html)) return NoSegments;
            if (segmentCache.TryGetValue(html, out var cached)) return cached;
            return Remember(segmentCache, html, ParseSegments(html));
        }

        private static List<(string Text, string Color)> ParseSegments(string html)
        {
            var segments = new List<(string Text, string Color)>();

            var colors = new Stack<string>();
            string color = "";
            int position = 0;

            void Add(string raw)
            {
                string text = WebUtility.HtmlDecode(raw);
                if (text.Length == 0) return;
                if (segments.Count > 0 && segments[^1].Color == color)
                    segments[^1] = (segments[^1].Text + text, color);
                else
                    segments.Add((text, color));
            }

            foreach (Match match in RichTagRegex().Matches(html))
            {
                Add(html[position..match.Index]);
                position = match.Index + match.Length;

                bool closing = match.Groups[1].Value == "/";
                switch (match.Groups[2].Value.ToLowerInvariant())
                {
                    case "br":
                        Add(" ");
                        break;
                    case "font" when closing:
                        color = colors.Count > 0 ? colors.Pop() : "";
                        break;
                    case "font":
                        colors.Push(color);
                        var found = ColorRegex().Match(match.Groups[3].Value);
                        if (found.Success) color = ColorClass(found.Groups[1].Value);
                        break;
                }
            }
            Add(html[position..]);

            for (int i = segments.Count - 1; i >= 0; i--)
                if (string.IsNullOrWhiteSpace(segments[i].Text)) segments.RemoveAt(i);
            if (segments.Count > 0)
            {
                segments[0] = (segments[0].Text.TrimStart(), segments[0].Color);
                segments[^1] = (segments[^1].Text.TrimEnd(), segments[^1].Color);
            }

            if (segments.Count > 1)
            {
                int length = 0;
                foreach (var segment in segments) length += segment.Text.Length;
                if (length > OneLineChars)
                {
                    string joined = string.Concat(segments.Select(segment => segment.Text));
                    segments.Clear();
                    segments.Add((joined, ""));
                }
            }
            return segments;
        }

        private static void RenderSegments(CCSPlayerController player, View view, string[] ids, List<(string Text, string Color)> segments, string[] colors)
        {
            for (int i = 0; i < SegmentSlots; i++)
            {
                string id = ids[i];
                if (i >= segments.Count)
                {
                    SetClass(player, view, id, OffClass, true);
                    continue;
                }

                string text = segments[i].Text;
                if (i == SegmentSlots - 1)
                    for (int rest = i + 1; rest < segments.Count; rest++)
                        text += segments[rest].Text;

                SetVar(player, view, id, "text", text);
                SwapClass(player, view, id, ref colors[i], segments[i].Color);
                SetClass(player, view, id, OffClass, false);
            }
        }

        private static bool IsAlive(CCSPlayerController player) =>
            player.PawnIsAlive && player.PlayerPawn.Value?.LifeState == (byte)LifeState_t.LIFE_ALIVE;

        private static CCSPlayerController? Observed(CCSPlayerController player)
        {
            if (IsAlive(player)) return PlayerManager.GetPlayerEvent(player) ?? player;

            var target = player.Pawn.Value?.ObserverServices?.ObserverTarget?.Value;
            if (target == null || !target.IsValid) return null;

            var observed = PlayerManager.GetControllerByPawn(target.Handle);
            return observed == null ? null : PlayerManager.GetPlayerEvent(observed);
        }

        private static void ApplyLook(CCSPlayerController player, View view)
        {
            var settings = Config.LoadedConfig.PanoramaHud!;
            string font = settings.Font?.Trim().ToLowerInvariant() ?? "";
            int size = Math.Clamp((int)Math.Round(settings.FontSize / 5.0) * 5, 80, 130);

            SwapClass(player, view, ScopeId, ref view.FontClass, "ff-" + (FontNames.Contains(font) ? font : "stratum"));
            SwapClass(player, view, ScopeId, ref view.SizeClass, size == 100 ? "" : "fs-" + size);
        }

        public static void ShowSkill(CCSPlayerController player, jSkill_PlayerInfo? skillPlayer, string? header, string? center, string? extra, bool isDescription, string? description)
        {
            var view = GetView(player);
            int tick = Server.TickCount;

            bool drawing = skillPlayer?.IsDrawing == true && player.PawnIsAlive;
            string info = string.IsNullOrEmpty(Config.LoadedConfig.HtmlHudCustomisation.HeaderLineSize) ? "" : Plain(header);
            var owner = IsAlive(player) ? skillPlayer : PlayerManager.GetPlayer(Observed(player));
            var desc = Segments(isDescription ? extra : description);
            var status = isDescription ? NoSegments : Segments(extra);

            ApplyLook(player, view);
            SetVar(player, view, SkillId, "info", info);
            SetVar(player, view, SkillId, "name", Plain(center));
            RenderSegments(player, view, DescIds, desc, view.DescColors);
            RenderSegments(player, view, StatusIds, status, view.StatusColors);
            SetClass(player, view, SkillId, "no-info", info == "");
            SetClass(player, view, SkillId, "no-desc", desc.Count == 0);
            SetClass(player, view, SkillId, "no-status", status.Count == 0);
            SwapClass(player, view, SkillId, ref view.SkillColor, drawing ? "" : ColorClass(center));
            ApplyCooldown(player, view, isDescription ? null : extra, owner, tick);
            if (!view.MenuShown)
                SetClass(player, view, SkillId, OffClass, false);

            view.SkillShown = true;
            view.LastSkillTick = tick;
        }

        private static void ApplyCooldown(CCSPlayerController player, View view, string? extra, jSkill_PlayerInfo? owner, int tick)
        {
            int seconds = 0;
            if (!string.IsNullOrEmpty(extra))
            {
                var match = CooldownRegex().Match(extra);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int value)
                    && extra == player.GetTranslation("hud_info", $"<font color='#FF0000'>{value}</font>"))
                    seconds = value;
            }

            if (seconds <= 0)
            {
                StopCooldown(player, view);
                return;
            }

            int shown = (int)Math.Ceiling((view.CooldownEndTick - tick) / 64.0);
            if (view.CooldownEndTick == 0 || Math.Abs(shown - seconds) > 1)
            {
                float total = owner == null ? 0f : SkillsInfo.GetValue<float>(owner.Skill, "cooldown");
                view.CooldownTotalTicks = (int)(Math.Max(total, seconds) * 64);
                view.CooldownEndTick = tick + seconds * 64;
            }

            SetClass(player, view, SkillId, "has-cd", true);
            UpdateCooldownBar(player, view, tick);
        }

        private static void UpdateCooldownBar(CCSPlayerController player, View view, int tick)
        {
            int percent = view.CooldownTotalTicks <= 0 ? 0 : Math.Clamp((int)Math.Round(100.0 * (view.CooldownEndTick - tick) / view.CooldownTotalTicks), 0, 100);
            SwapClass(player, view, SkillId, ref view.CooldownClass, "cd-" + percent);
        }

        private static void StopCooldown(CCSPlayerController player, View view)
        {
            if (view.CooldownEndTick == 0) return;

            view.CooldownEndTick = 0;
            SetClass(player, view, SkillId, "has-cd", false);
        }

        private static string EnemyInfo(CCSPlayerController player, Skills skill, jSkill_PlayerInfo? curser)
        {
            string key = skill.ToString().ToLowerInvariant() + "_enemy_info";
            object[] args = skill switch
            {
                Skills.Bounty => [SkillsInfo.GetValue<int>(skill, "reward")],
                Skills.ExpensiveAmmo => [SkillsInfo.GetValue<int>(skill, "moneyPerShot")],
                Skills.Giant => [curser?.SkillChance ?? 1f],
                _ => []
            };
            string text = player.GetTranslationWithoutIlliterate(key, args);
            return text == key || text.Contains('{') ? "" : Plain(text);
        }

        private static void UpdateCurses(CCSPlayerController player, View view)
        {
            uint shown = Observed(player)?.Key() ?? 0;
            var cursers = shown == 0 ? [] : SkillUtils.GetCursersOf(shown);

            int slot = 0;
            foreach (uint curserKey in cursers)
            {
                if (slot >= CurseSlots) break;

                var curser = PlayerManager.GetPlayerByKey(curserKey);
                if (curser == null || !SkillUtils.IsCurseSkill(curser.Skill)) continue;

                string id = CurseIds[slot];
                long key = ((long)curserKey << 16) | (uint)curser.Skill;
                if (view.CurseKeys[slot] != key)
                {
                    view.CurseKeys[slot] = key;
                    string info = EnemyInfo(player, curser.Skill, curser);
                    SetVar(player, view, id, "name", Plain(player.GetSkillName(curser.Skill)));
                    SetVar(player, view, id, "info", info);
                    SetClass(player, view, id, "no-info", info == "");
                    SwapClass(player, view, id, ref view.CurseColors[slot], ColorClass(SkillData.GetInfo(curser.Skill)?.Color));
                }
                SetClass(player, view, id, OffClass, false);
                slot++;
            }

            for (; slot < CurseSlots; slot++)
            {
                view.CurseKeys[slot] = 0;
                SetClass(player, view, CurseIds[slot], OffClass, true);
            }
        }

        public static void OnTick()
        {
            if (!Enabled) return;

            int tick = Server.TickCount;
            if (tick % 4 != 0) return;
            bool slow = tick % 8 == 0;

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (!Active(player)) continue;

                var view = GetView(player);
                SetClass(player, view, ScopeId, "is-spec", !IsAlive(player));
                TickMenu(player, view, tick);

                if (view.SkillShown && tick - view.LastSkillTick > HideAfterTicks)
                {
                    view.SkillShown = false;
                    SetClass(player, view, SkillId, OffClass, true);
                }

                if (slow) UpdateCurses(player, view);

                if (view.CooldownEndTick == 0) continue;

                if (tick >= view.CooldownEndTick)
                    StopCooldown(player, view);
                else
                    UpdateCooldownBar(player, view, tick);
            }
        }

        public static void Forget(int slot)
        {
            views.Remove(slot);
        }

        public static void OnRoundStart()
        {
            if (!Enabled) return;
            Server.NextFrame(() =>
            {
                if (layout is { IsValid: true }) return;
                EnsureLayout();
            });
        }

        public static void Reset()
        {
            layout = null;
            views.Clear();
        }
    }
}
