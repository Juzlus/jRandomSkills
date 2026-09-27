using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using src.utils;
using System.Collections.Concurrent;
using WASDSharedAPI;
using static src.jRandomSkills;

namespace src.modules
{
    // !guns: pick the rifle and pistol you get each retakes round instead of buying. Choices are kept per
    // SteamID in configs/guns.json and read by the retakes weapon allocation.
    public static class GunsModule
    {
        public const string Random = "random";

        private class Preference
        {
            public string? PrimaryT { get; set; }
            public string? PrimaryCT { get; set; }
            public string? Secondary { get; set; }
        }

        private static readonly ConcurrentDictionary<ulong, Preference> preferences = new();
        private static readonly object fileLock = new();
        private static string PreferencesPath => Path.Combine(Instance.ModuleDirectory, "configs", "guns.json");

        private static readonly Dictionary<string, string> displayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["weapon_ak47"] = "AK-47", ["weapon_m4a1"] = "M4A4", ["weapon_m4a1_silencer"] = "M4A1-S",
            ["weapon_famas"] = "FAMAS", ["weapon_galilar"] = "Galil AR", ["weapon_aug"] = "AUG", ["weapon_sg556"] = "SG 553",
            ["weapon_awp"] = "AWP", ["weapon_ssg08"] = "SSG 08", ["weapon_scar20"] = "SCAR-20", ["weapon_g3sg1"] = "G3SG1",
            ["weapon_mp9"] = "MP9", ["weapon_mac10"] = "MAC-10", ["weapon_mp7"] = "MP7", ["weapon_mp5sd"] = "MP5-SD",
            ["weapon_ump45"] = "UMP-45", ["weapon_p90"] = "P90", ["weapon_bizon"] = "PP-Bizon",
            ["weapon_nova"] = "Nova", ["weapon_xm1014"] = "XM1014", ["weapon_mag7"] = "MAG-7", ["weapon_sawedoff"] = "Sawed-Off",
            ["weapon_m249"] = "M249", ["weapon_negev"] = "Negev",
            ["weapon_deagle"] = "Desert Eagle", ["weapon_usp_silencer"] = "USP-S", ["weapon_hkp2000"] = "P2000", ["weapon_glock"] = "Glock-18",
            ["weapon_p250"] = "P250", ["weapon_fiveseven"] = "Five-SeveN", ["weapon_tec9"] = "Tec-9", ["weapon_cz75a"] = "CZ75-Auto",
            ["weapon_revolver"] = "R8 Revolver", ["weapon_elite"] = "Dual Berettas",
        };

        // What players type after !guns.
        private static readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ak"] = "weapon_ak47", ["ak47"] = "weapon_ak47", ["m4a4"] = "weapon_m4a1", ["m4"] = "weapon_m4a1",
            ["m4a1s"] = "weapon_m4a1_silencer", ["m4a1-s"] = "weapon_m4a1_silencer", ["m4a1_silencer"] = "weapon_m4a1_silencer", ["m4s"] = "weapon_m4a1_silencer",
            ["famas"] = "weapon_famas", ["galil"] = "weapon_galilar", ["galilar"] = "weapon_galilar", ["aug"] = "weapon_aug",
            ["sg553"] = "weapon_sg556", ["sg556"] = "weapon_sg556", ["krieg"] = "weapon_sg556", ["awp"] = "weapon_awp",
            ["scout"] = "weapon_ssg08", ["ssg08"] = "weapon_ssg08", ["ssg"] = "weapon_ssg08", ["scar20"] = "weapon_scar20", ["scar"] = "weapon_scar20",
            ["g3sg1"] = "weapon_g3sg1", ["autosniper"] = "weapon_g3sg1",
            ["mp9"] = "weapon_mp9", ["mac10"] = "weapon_mac10", ["mac"] = "weapon_mac10", ["mp7"] = "weapon_mp7", ["mp5"] = "weapon_mp5sd", ["mp5sd"] = "weapon_mp5sd",
            ["ump"] = "weapon_ump45", ["ump45"] = "weapon_ump45", ["p90"] = "weapon_p90", ["bizon"] = "weapon_bizon", ["pp-bizon"] = "weapon_bizon",
            ["nova"] = "weapon_nova", ["xm1014"] = "weapon_xm1014", ["xm"] = "weapon_xm1014", ["mag7"] = "weapon_mag7", ["sawedoff"] = "weapon_sawedoff", ["sawed-off"] = "weapon_sawedoff",
            ["m249"] = "weapon_m249", ["negev"] = "weapon_negev",
            ["deagle"] = "weapon_deagle", ["usp"] = "weapon_usp_silencer", ["usps"] = "weapon_usp_silencer", ["usp-s"] = "weapon_usp_silencer", ["usp_silencer"] = "weapon_usp_silencer",
            ["p2000"] = "weapon_hkp2000", ["hkp2000"] = "weapon_hkp2000", ["glock"] = "weapon_glock", ["glock18"] = "weapon_glock", ["p250"] = "weapon_p250",
            ["fiveseven"] = "weapon_fiveseven", ["five-seven"] = "weapon_fiveseven", ["57"] = "weapon_fiveseven", ["tec9"] = "weapon_tec9", ["tec-9"] = "weapon_tec9",
            ["cz"] = "weapon_cz75a", ["cz75"] = "weapon_cz75a", ["cz75a"] = "weapon_cz75a", ["revolver"] = "weapon_revolver", ["r8"] = "weapon_revolver",
            ["elite"] = "weapon_elite", ["dualies"] = "weapon_elite", ["duals"] = "weapon_elite", ["berettas"] = "weapon_elite",
            ["random"] = Random, ["rand"] = Random, ["any"] = Random,
        };

        private static Config.GunsModuleSettings Settings => Config.LoadedConfig.Modules.Guns;

        public static void Load()
        {
            LoadPreferences();

            foreach (var alias in Settings.Alias.Split(',').Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)))
                Instance.AddCommand($"css_{alias}", "Choose the weapons you get in retakes", Command_Guns);
        }

        private static void LoadPreferences()
        {
            lock (fileLock)
            {
                try
                {
                    if (!File.Exists(PreferencesPath)) return;

                    var loaded = JsonConvert.DeserializeObject<Dictionary<ulong, Preference>>(File.ReadAllText(PreferencesPath));
                    if (loaded == null) return;

                    foreach (var (steamId, preference) in loaded)
                        preferences[steamId] = preference;
                }
                catch (Exception ex)
                {
                    Instance.Logger.LogError("[jRandomSkills] Could not read guns.json: {Message}", ex.Message);
                }
            }
        }

        private static void SavePreferences()
        {
            lock (fileLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
                    string json = JsonConvert.SerializeObject(preferences.ToDictionary(p => p.Key, p => p.Value), Formatting.Indented);
                    string tempPath = $"{PreferencesPath}.temp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, PreferencesPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    Instance.Logger.LogError("[jRandomSkills] Could not save guns.json: {Message}", ex.Message);
                }
            }
        }

        public static string DisplayName(string weapon)
        {
            if (weapon == Random) return "Random";
            return displayNames.TryGetValue(weapon, out var name) ? name : weapon.Replace("weapon_", "");
        }

        // Turns "ak47", "AK-47" or "weapon_ak47" into "weapon_ak47"; null when it isn't a known weapon.
        private static string? Normalize(string input)
        {
            string key = input.Trim().ToLowerInvariant();
            if (aliases.TryGetValue(key, out var alias)) return alias;
            if (key.StartsWith("weapon_") && displayNames.ContainsKey(key)) return key;
            if (displayNames.ContainsKey($"weapon_{key}")) return $"weapon_{key}";

            var byDisplay = displayNames.FirstOrDefault(kv => kv.Value.Replace("-", "").Replace(" ", "").Equals(key.Replace("-", "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
            return byDisplay.Key;
        }

        private static List<string> PrimaryList(CsTeam team) => team == CsTeam.Terrorist ? Settings.PrimaryT : Settings.PrimaryCT;

        // Weapon the retakes allocation should hand out, or null to use its default.
        public static string? ResolvePrimary(CCSPlayerController player, System.Random random)
        {
            if (!Settings.Enabled || player == null || !player.IsValid) return null;
            if (!preferences.TryGetValue(player.SteamID, out var preference)) return null;

            string? choice = player.Team == CsTeam.Terrorist ? preference.PrimaryT : preference.PrimaryCT;
            return Pick(choice, PrimaryList(player.Team), random);
        }

        public static string? ResolveSecondary(CCSPlayerController player, System.Random random)
        {
            if (!Settings.Enabled || player == null || !player.IsValid) return null;
            if (!preferences.TryGetValue(player.SteamID, out var preference)) return null;

            return Pick(preference.Secondary, Settings.Secondary, random);
        }

        private static string? Pick(string? choice, List<string> allowed, System.Random random)
        {
            if (string.IsNullOrEmpty(choice) || allowed.Count == 0) return null;
            if (choice == Random) return allowed[random.Next(allowed.Count)];
            return allowed.Contains(choice, StringComparer.OrdinalIgnoreCase) ? choice : null;
        }

        private static Preference GetOrCreate(CCSPlayerController player) => preferences.GetOrAdd(player.SteamID, _ => new Preference());

        private enum Slot { PrimaryT, PrimaryCT, Secondary }

        private static void SetChoice(CCSPlayerController player, Slot slot, string weapon)
        {
            var preference = GetOrCreate(player);
            switch (slot)
            {
                case Slot.PrimaryT: preference.PrimaryT = weapon; break;
                case Slot.PrimaryCT: preference.PrimaryCT = weapon; break;
                case Slot.Secondary: preference.Secondary = weapon; break;
            }
            SavePreferences();

            player.PrintToChat($" {ChatColors.Lime}{player.GetTranslationWithoutIlliterate("guns_saved", player.GetTranslationWithoutIlliterate(SlotKey(slot)), DisplayName(weapon))}");
        }

        private static string SlotKey(Slot slot) => slot switch
        {
            Slot.PrimaryT => "guns_rifle_t",
            Slot.PrimaryCT => "guns_rifle_ct",
            _ => "guns_pistol",
        };

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
        private static void Command_Guns(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid || player.IsBot) return;
            if (!Settings.Enabled) return;

            string[] args = command.ArgString.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 0)
            {
                OpenMenu(player);
                return;
            }

            // !guns <rifle> [pistol]: the rifle goes to the slot of the team the player is on now
            // (both slots when they are not on a team yet).
            string? primary = Normalize(args[0]);
            if (primary == null || (primary != Random && !PrimaryList(CsTeam.Terrorist).Contains(primary, StringComparer.OrdinalIgnoreCase) && !PrimaryList(CsTeam.CounterTerrorist).Contains(primary, StringComparer.OrdinalIgnoreCase)))
            {
                if (primary != null && Settings.Secondary.Contains(primary, StringComparer.OrdinalIgnoreCase))
                {
                    SetChoice(player, Slot.Secondary, primary);
                    return;
                }

                player.PrintToChat($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate("guns_unknown", args[0])}");
                player.PrintToChat($" {ChatColors.Grey}{player.GetTranslationWithoutIlliterate("guns_usage")}");
                return;
            }

            bool tOk = primary == Random || PrimaryList(CsTeam.Terrorist).Contains(primary, StringComparer.OrdinalIgnoreCase);
            bool ctOk = primary == Random || PrimaryList(CsTeam.CounterTerrorist).Contains(primary, StringComparer.OrdinalIgnoreCase);

            if (player.Team == CsTeam.Terrorist && tOk) SetChoice(player, Slot.PrimaryT, primary);
            else if (player.Team == CsTeam.CounterTerrorist && ctOk) SetChoice(player, Slot.PrimaryCT, primary);
            else
            {
                if (tOk) SetChoice(player, Slot.PrimaryT, primary);
                if (ctOk) SetChoice(player, Slot.PrimaryCT, primary);
            }

            if (args.Length > 1)
            {
                string? secondary = Normalize(args[1]);
                if (secondary != null && (secondary == Random || Settings.Secondary.Contains(secondary, StringComparer.OrdinalIgnoreCase)))
                    SetChoice(player, Slot.Secondary, secondary);
                else
                    player.PrintToChat($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate("guns_unknown", args[1])}");
            }
        }

        private static void OpenMenu(CCSPlayerController player)
        {
            if (SkillUtils.HasMenu(player))
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslationWithoutIlliterate("guns_menu_busy")}");
                return;
            }

            var manager = SkillUtils.MenuManager();
            if (manager == null) return;

            var preference = GetOrCreate(player);
            var menu = CreateStyledMenu(manager, player, player.GetTranslationWithoutIlliterate("guns_select_info"));

            AddSlotOption(menu, manager, player, Slot.PrimaryT, preference.PrimaryT, PrimaryList(CsTeam.Terrorist));
            AddSlotOption(menu, manager, player, Slot.PrimaryCT, preference.PrimaryCT, PrimaryList(CsTeam.CounterTerrorist));
            AddSlotOption(menu, manager, player, Slot.Secondary, preference.Secondary, Settings.Secondary);

            manager.OpenMainMenu(player, menu);
        }

        private static void AddSlotOption(IWasdMenu menu, IWasdMenuManager manager, CCSPlayerController player, Slot slot, string? current, List<string> weapons)
        {
            string slotName = player.GetTranslationWithoutIlliterate(SlotKey(slot));
            string currentName = string.IsNullOrEmpty(current) ? player.GetTranslationWithoutIlliterate("guns_default") : DisplayName(current);

            menu.Add($"{System.Net.WebUtility.HtmlEncode(slotName)}: {System.Net.WebUtility.HtmlEncode(currentName)}", (p, _) =>
            {
                var subMenu = CreateStyledMenu(manager, p, slotName);

                subMenu.Add(p.GetTranslationWithoutIlliterate("guns_random"), (p2, _) =>
                {
                    SetChoice(p2, slot, Random);
                    manager.CloseMenu(p2);
                });

                foreach (var weapon in weapons)
                {
                    string chosen = weapon;
                    subMenu.Add(System.Net.WebUtility.HtmlEncode(DisplayName(chosen)), (p2, _) =>
                    {
                        SetChoice(p2, slot, chosen);
                        manager.CloseMenu(p2);
                    });
                }

                manager.OpenSubMenu(p, subMenu);
            });
        }

        // Same HUD styling as the skill selection menus.
        private static IWasdMenu CreateStyledMenu(IWasdMenuManager manager, CCSPlayerController player, string subtitle)
        {
            var config = Config.LoadedConfig.HtmlHudCustomisation;
            string title = player.GetTranslationWithoutIlliterate("guns_title");

            string header = $"<font class='fontWeight-Bold fontSize-{config.SkillLineSize}' color='{config.HeaderLineColor}'>‪{System.Net.WebUtility.HtmlEncode(title)}‬</font><br>";
            string info = string.IsNullOrEmpty(config.WSADMenuSelectInfoLineSize) ? "" :
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
