using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;
using src.player;
using static src.jRandomSkills;

namespace src.utils
{
    // CounterStrikeSharp's DispatchSpawn native crashes the server when CounterStrikeSharp doesn't match the
    // installed CS2 build (seen on CS2 1.41.8.5 with CounterStrikeSharp 1.0.375). While spawning is blocked,
    // nothing in the plugin creates entities through it and the skills that need spawned entities are not drawn.
    public static class EntitySafety
    {
        public static bool SpawningBlocked { get; private set; }
        public static string? GameVersion { get; private set; }

        public static void Load()
        {
            var settings = Config.LoadedConfig.EntitySpawnSafety;
            GameVersion = ReadGameVersion();

            SpawningBlocked = settings.Mode.Trim().ToLowerInvariant() switch
            {
                "on" => true,
                "off" => false,
                // Auto: only trust builds someone has verified with the installed CounterStrikeSharp.
                _ => GameVersion != null && !settings.VerifiedGameVersions.Contains(GameVersion),
            };

            if (SpawningBlocked)
                Instance.Logger.LogWarning(
                    "[jRandomSkills] Entity spawning is disabled (EntitySpawnSafety.Mode={Mode}, CS2 {Version}, CounterStrikeSharp {Css}, verified CS2 versions: {Verified}). " +
                    "Skills that spawn entities are not drawn and retakes auto-plant falls back to the planter placing the bomb with one click. " +
                    "Hands-free auto-plant needs a CounterStrikeSharp built for this CS2 version: update CounterStrikeSharp, then add \"{Version}\" to EntitySpawnSafety.VerifiedGameVersions or set Mode to \"Off\".",
                    settings.Mode, GameVersion ?? "unknown", CounterStrikeSharpVersion ?? "unknown", string.Join(", ", settings.VerifiedGameVersions));
        }

        // Version of the CounterStrikeSharp the server runs (the API assembly is loaded from the server install).
        public static string? CounterStrikeSharpVersion
        {
            get
            {
                try
                {
                    var info = typeof(CounterStrikeSharp.API.Core.BasePlugin).Assembly
                        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                        .FirstOrDefault()?.InformationalVersion;
                    return info?.Split('+')[0];
                }
                catch { return null; }
            }
        }

        public static bool IsSkillBlocked(Skills skill)
        {
            return SpawningBlocked && Config.LoadedConfig.EntitySpawnSafety.Skills.Contains(SkillNames.Get(skill));
        }

        private static string? ReadGameVersion()
        {
            try
            {
                var path = Path.Combine(Server.GameDirectory, "csgo", "steam.inf");
                if (!File.Exists(path)) return null;

                foreach (var line in File.ReadLines(path))
                    if (line.StartsWith("PatchVersion=", StringComparison.OrdinalIgnoreCase))
                        return line["PatchVersion=".Length..].Trim();
            }
            catch (Exception ex)
            {
                Instance.Logger.LogError("[jRandomSkills] Could not read the CS2 version: {Message}", ex.Message);
            }

            return null;
        }
    }
}
