using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using MaxMind.Db;
using Newtonsoft.Json;
using System.Net;
using System.Collections.Concurrent;
using src.player;
using src.player.skills;

namespace src.utils
{
    public static class Localization
    {
        private static readonly string languagesFolderPath = Path.Combine(jRandomSkills.Instance.ModuleDirectory, "languages");
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _translations = [];

        private static readonly string playersLanguageFileName = "playersLanguage.json";
        private static readonly string configsFolderPath = Path.Combine(jRandomSkills.Instance.ModuleDirectory, "configs");
        private static ConcurrentDictionary<ulong, string> _playersLanguage = [];
        private static readonly string geoliteFilePath = Path.Combine(jRandomSkills.Instance.ModuleDirectory, "packages", "GeoLite2-Country.mmdb");

        private static string defaultLangCode = "en";

        public static void Load()
        {
            _translations.Clear();
            _playersLanguage.Clear();
            SetLangCode();
            LoadAllLanguages();
            LoadPlayersLanguage();
        }
        
        private static void SetLangCode()
        {
            defaultLangCode = Config.LoadedConfig.LanguageSystem.DefaultLangCode;
        }

        private static void LoadAllLanguages()
        {
            if (!Directory.Exists(languagesFolderPath))
                return;

            foreach (var file in Directory.GetFiles(languagesFolderPath, "*.json"))
                LoadLanguage(file);
        }

        private static void LoadLanguage(string langPath)
        {
            if (!File.Exists(langPath))
                return;

            var code = Path.GetFileNameWithoutExtension(langPath).ToLower();
            var jsonText = File.ReadAllText(langPath);
            var translations = JsonConvert.DeserializeObject<ConcurrentDictionary<string, string>>(jsonText);

            if (translations != null)
                _translations.AddOrUpdate(code, translations, (k, v) => translations);
        }

        public static bool HasTranslation(string code)
        {
            return _translations.ContainsKey(code.ToLower());
        }

        public static string GetSkillName(this CCSPlayerController player, Skills skill, float? chance = null)
        {
            string langCode = GetLangCode(player);
            if (chance == null)
            {
                var translation = GetTranslation(skill.ToString().ToLower(), langCode);
                if (!translation.Contains("{0}"))
                    return translation;
                
                if (!translation.Contains(' '))
                    return translation.Replace("{0}", "").Trim();
                
                var parts = translation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var filtered = parts.Where(p => !p.Contains("{0}"));
                return string.Join(' ', filtered);
            }

            var value = Math.Round((double)(chance ?? 1), 2);
            var skillNameText = GetTranslation(skill.ToString().ToLower(), langCode, player, value);
            if (skillNameText.Contains('%')) skillNameText = skillNameText.Replace(value.ToString(), Math.Round(value * 100, 0).ToString());
            return skillNameText;
        }

        public static string GetSkillDescription(this CCSPlayerController player, Skills skill, float? chance = null)
        {
            string langCode = GetLangCode(player);
            if (chance == null)
            {
                var translation = GetTranslation($"{skill.ToString().ToLower()}_desc", langCode);
                if (!translation.Contains("{0}"))
                    return translation;

                if (!translation.Contains(' '))
                    return translation.Replace("{0}", "").Trim();

                var parts = translation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var filtered = parts.Where(p => !p.Contains("{0}"));
                return string.Join(' ', filtered);
            }

            var skillName = $"{skill.ToString().ToLower()}_desc2";
            var value = Math.Round((double)(chance ?? 1), 2);
            var desc2 = GetTranslation(skillName, langCode, player, value);

            var skilLDescription = desc2 == skillName
                ? player.GetTranslation($"{skill.ToString().ToLower()}_desc")
                : desc2.Contains('%') ? desc2.Replace(value.ToString(), Math.Round(value * 100, 0).ToString()) : desc2;
            return skilLDescription;
        }

        public static void PrintTranslationToChatAll(string message, string[]? key, params object[][]? args)
        {
            foreach (var player in Utilities.GetPlayers().Where(p => !p.IsBot))
            {
                string langCode = GetLangCode(player);
                if (key == null)
                {
                    player.PrintToChat(message);
                    return;
                }

                ConcurrentBag<string> translations = [];
                for (int i = 0; i < key.Length; i++)
                {
                    object[] currentArgs = args != null && i < args.Length ? args[i] : [];
                    string translation = GetTranslation(key[i], langCode, null, currentArgs);
                    translations.Add(translation);
                }
                player.PrintToChat(string.Format(message, [.. translations]));
            }
        }

        public static string GetTranslation(this CCSPlayerController player, string key, params object[] args)
        {
            string langCode = GetLangCode(player);
            return GetTranslation(key, langCode, player, args);
        }

        public static string GetTranslation(string key, string? langCode = null, CCSPlayerController? player = null, params object[] args)
        {
            langCode ??= defaultLangCode;
            if (_translations.TryGetValue(langCode, out var langDict) && langDict.TryGetValue(key, out var translation))
                if (args.Length != 0 && args[0].ToString() == "welcome")
                    return translation;
                else
                {
                    string output = args.Length == 0 ? translation : string.Format(translation, args);
                    output = output.Replace("CHATCOLORS.RED", ChatColors.Red.ToString());
                    if (Config.LoadedConfig.AlternativeSkillButton != null)
                        output = output.Replace("css_useSkill", $"css_useSkill/{Config.LoadedConfig.AlternativeSkillButton}");

                    if (Illiterate.CheckIlliterateSkill(player))
                        return Illiterate.GetRandomText(output);
                    return output;
                }

            if (langCode != defaultLangCode)
                return GetTranslation(key, null, player, args);
            return key;
        }

        private static string GetLangCode(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return defaultLangCode;

            string? fileLangCode = GetLangCodeFromFile(player.SteamID);
            if (!string.IsNullOrEmpty(fileLangCode))
                return fileLangCode;

            if (Config.LoadedConfig.LanguageSystem.DisableGeoLite == true)
                return defaultLangCode;

            string? geoliteLandCode = GetLangCodeFromDatabase(GetPlayerIP(player)) ?? defaultLangCode;
            ChangePlayerLanguage(player, geoliteLandCode);
            return geoliteLandCode;
        }

        private static string? GetPlayerIP(CCSPlayerController? player)
        {
            if (player == null) return null;
            var playerIP = player.IpAddress;
            if (playerIP == null) return null;
            string[] parts = playerIP.Split(':');
            return parts.Length > 1 ? parts[0] : playerIP;
        }

        private static string? GetLangCodeFromDatabase(string? playerIP)
        {
            if (string.IsNullOrEmpty(playerIP)) return null;
            if (!File.Exists(geoliteFilePath)) return null;
            using var reader = new Reader(geoliteFilePath);

            if (!IPAddress.TryParse(playerIP, out var ip))
                return null;

            var data = reader.Find<ConcurrentDictionary<string, object>>(ip);
            if (data == null || data.IsEmpty) return null;

            if (data.TryGetValue("country", out var _country) && _country is Dictionary<string, object> country)
                if (country.TryGetValue("iso_code", out var _isoCode) && _isoCode is string isoCode)
                {
                    string fileName = Config.LoadedConfig.LanguageSystem.DefaultLangCode;
                    foreach (var langInfo in Config.LoadedConfig.LanguageSystem.LanguageInfos)
                        if (langInfo.IsoCodes.Contains(isoCode))
                            fileName = langInfo.FileName;
                    if (_translations.ContainsKey(fileName))
                        return fileName;
                }
            return null;
        }

        private static void LoadPlayersLanguage()
        {
            string filePath = Path.Combine(configsFolderPath, playersLanguageFileName);
            if (!File.Exists(filePath))
                return;

            var jsonText = File.ReadAllText(filePath);
            _playersLanguage = JsonConvert.DeserializeObject<ConcurrentDictionary<ulong, string>>(jsonText) ?? [];
        }

        private static void SavePlayersLanguage()
        {
            Directory.CreateDirectory(configsFolderPath);
            string filePath = Path.Combine(configsFolderPath, playersLanguageFileName);
            var json = JsonConvert.SerializeObject(_playersLanguage);
            if (json != null)
                File.WriteAllText(filePath, json);
        }

        private static string? GetLangCodeFromFile(ulong? playerSteamID)
        {
            if (playerSteamID == null || playerSteamID == 0) return null;
            if (_playersLanguage.TryGetValue((ulong)playerSteamID, out var langCode))
                return langCode;
            return null;
        }

        public static void ChangePlayerLanguage(CCSPlayerController? player, string language)
        {
            if (player == null || !player.IsValid) return;
            _playersLanguage.AddOrUpdate(player.SteamID, language, (k,v) => language);
            SavePlayersLanguage();
        }
    }
}
