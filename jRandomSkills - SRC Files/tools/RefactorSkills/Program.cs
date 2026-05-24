using System;
using System.IO;
using System.Text.RegularExpressions;

var skillsDir = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "player", "skills"));

if (!Directory.Exists(skillsDir))
{
    Console.Error.WriteLine($"Skills directory not found: {skillsDir}");
    return 1;
}

Console.WriteLine($"Refactoring skills in: {skillsDir}");

var skipFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "OPTIMIZED_SKILL_TEMPLATE.cs",
    "OPTIMIZATION_SUMMARY.cs"
};

var files = Directory.GetFiles(skillsDir, "*.cs");
int updated = 0;

foreach (var file in files)
{
    if (skipFiles.Contains(Path.GetFileName(file))) continue;

    var text = File.ReadAllText(file);
    var original = text;

    text = ApplyRefactoring(text, file);

    if (text != original)
    {
        File.WriteAllText(file, text);
        updated++;
        Console.WriteLine($"Updated: {Path.GetFileName(file)}");
    }
}

Console.WriteLine($"Refactor complete. Files updated: {updated}");
return 0;

static string ApplyRefactoring(string text, string filePath)
{
    // --- PlayerManager lookups (all FirstOrDefault variants) ---
    text = Regex.Replace(text,
        @"Instance\.SkillPlayer\.FirstOrDefault\(p => p\.SteamID == (\w+)\?\.SteamID\)",
        "PlayerManager.GetPlayerByIndex($1.Index)");

    text = Regex.Replace(text,
        @"Instance\.SkillPlayer\.FirstOrDefault\(p => p\.SteamID == (\w+)\.SteamID\)",
        "PlayerManager.GetPlayerByIndex($1.Index)");

    text = Regex.Replace(text,
        @"Instance\.SkillPlayer\.FirstOrDefault\([a-z]\s*=>\s*[a-z]\.SteamID\s*==\s*(\w+)\?\.SteamID\)",
        "PlayerManager.GetPlayerByIndex($1.Index)");

    text = Regex.Replace(text,
        @"jRandomSkills\.Instance\.SkillPlayer\.FirstOrDefault\([a-z]\s*=>\s*[a-z]\.SteamID\s*==\s*(\w+)\?\.SteamID\)",
        "PlayerManager.GetPlayerByIndex($1.Index)");

    text = Regex.Replace(text,
        @"jRandomSkills\.Instance\.SkillPlayer\.FirstOrDefault\([a-z]\s*=>\s*[a-z]\.SteamID\s*==\s*(\w+)\.Index\)",
        "PlayerManager.GetPlayerByIndex($1.Index)");

    text = Regex.Replace(text,
        @"Instance\.SkillPlayer\.FirstOrDefault\([a-z]\s*=>\s*[a-z]\.Index\s*==\s*(\w+)\.Index\)",
        "PlayerManager.GetPlayerByIndex($1.Index)");

    text = Regex.Replace(text,
        @"Instance\.SkillPlayer\.FirstOrDefault\([a-z]\s*=>\s*[a-z]\.Index\s*==\s*playerIndex\s*&&\s*[a-z]\.Skill\s*==\s*(\w+)\)",
        "PlayerManager.GetPlayerByIndex(playerIndex) is { Skill: $1 } playerInfo ? playerInfo : null");

    // --- ConcurrentDictionary ulong -> uint ---
    text = Regex.Replace(text, @"ConcurrentDictionary<\s*ulong\s*,", "ConcurrentDictionary<uint,");

    // --- Dictionary keys: .SteamID -> .Index for common player variables ---
    foreach (var varName in new[] { "player", "enemy", "attacker", "victim", "target", "pl", "p", "e" })
    {
        text = text.Replace($"{varName}.SteamID", $"{varName}.Index");
    }

    // --- ulong steamID local vars used as keys ---
    text = Regex.Replace(text, @"ulong\s+steamID\s*=\s*(\w+)\.Index", "uint playerIndex = $1.Index");
    text = Regex.Replace(text, @"ulong\s+steamID\s*=\s*player\.Index", "uint playerIndex = player.Index");

    // Replace steamID variable references with playerIndex when it was renamed
    if (text.Contains("uint playerIndex = "))
    {
        text = Regex.Replace(text, @"\bsteamID\b", "playerIndex");
    }

    // --- Remove bot restrictions from player filters ---
    text = Regex.Replace(text, @"\s*&&\s*!p\.IsBot", "", RegexOptions.Multiline);
    text = Regex.Replace(text, @"\s*&&\s*!(\w+)\.IsBot", "", RegexOptions.Multiline);
    text = Regex.Replace(text, @"!p\.IsBot\s*&&\s*", "", RegexOptions.Multiline);
    text = Regex.Replace(text, @"!(\w+)\.IsBot\s*&&\s*", "", RegexOptions.Multiline);
    text = Regex.Replace(text, @"\|\|\s*(\w+)\.IsBot", "", RegexOptions.Multiline);
    text = Regex.Replace(text, @"(\w+)\.IsBot\s*\|\|", "", RegexOptions.Multiline);
    text = Regex.Replace(text, @"if\s*\([^)]*\.IsBot\)\s*continue;", "", RegexOptions.Multiline);

    // --- Menu enemy lookup: SteamID string -> index TryParse ---
    text = Regex.Replace(text,
        @"var\s+enemy\s*=\s*Utilities\.GetPlayers\(\)\.FirstOrDefault\(p\s*=>\s*p\.SteamID\.ToString\(\)\s*==\s*enemyId\)",
        @"if (!uint.TryParse(enemyId, out uint enemyIndex)) { player.PrintToChat($"" {ChatColors.Red}"" + player.GetTranslation(""selectplayerskill_incorrect_enemy_index"")); return; }
            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex)");

    text = Regex.Replace(text,
        @"var\s+enemy\s*=\s*Utilities\.GetPlayers\(\)\.FirstOrDefault\(p\s*=>\s*p\.Team\s*!=\s*player\.Team\s*&&\s*p\.Index\.ToString\(\)\s*==\s*enemyId\)",
        @"if (!uint.TryParse(enemyId, out uint enemyIndex)) { player.PrintToChat($"" {ChatColors.Red}"" + player.GetTranslation(""selectplayerskill_incorrect_enemy_index"")); return; }
            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex)");

    // --- Add using src.utils if PlayerManager/EntityManager used but missing ---
    if ((text.Contains("PlayerManager.") || text.Contains("EntityManager."))
        && !text.Contains("using src.utils;"))
    {
        var lastUsing = text.LastIndexOf("using ", StringComparison.Ordinal);
        if (lastUsing >= 0)
        {
            var lineEnd = text.IndexOf('\n', lastUsing);
            if (lineEnd >= 0)
                text = text.Insert(lineEnd + 1, "using src.utils;\n");
        }
    }

    // --- Null safety: common EnableSkill/DisableSkill entry points ---
    if (text.Contains("public static void EnableSkill(CCSPlayerController player)")
        && !text.Contains("if (player == null || !player.IsValid) return;")
        && !text.Contains("if (player == null) return;"))
    {
        text = text.Replace(
            "public static void EnableSkill(CCSPlayerController player)\n        {",
            "public static void EnableSkill(CCSPlayerController player)\n        {\n            if (player == null || !player.IsValid) return;");
    }

    if (text.Contains("public static void DisableSkill(CCSPlayerController player)")
        && !Regex.IsMatch(text, @"DisableSkill\(CCSPlayerController player\)\s*\{[^}]*if \(player == null"))
    {
        text = text.Replace(
            "public static void DisableSkill(CCSPlayerController player)\n        {",
            "public static void DisableSkill(CCSPlayerController player)\n        {\n            if (player == null) return;");
    }

    return text;
}
