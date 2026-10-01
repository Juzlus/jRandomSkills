using System.Text;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;

namespace RetakesPlugin.Utils;

public static class ServerHelper
{
    private static string RetakesCfgDirectory => Path.Combine(Server.GameDirectory, "csgo", "cfg", "cs2-retakes");
    private static string RetakesCfgPath => Path.Combine(RetakesCfgDirectory, "retakes.cfg");
    private static string RetakesUnloadCfgPath => Path.Combine(RetakesCfgDirectory, "retakes_unload.cfg");

    public static void ExecuteRetakesConfiguration()
    {
        if (!File.Exists(RetakesCfgPath))
        {
            CreateRetakesConfig();
        }
        else
        {
            MigrateRetakesConfig();
        }

        // Create the unload config eagerly so server owners can customise it
        // before the plugin is ever unloaded
        if (!File.Exists(RetakesUnloadCfgPath))
        {
            CreateRetakesUnloadConfig();
        }

        Server.ExecuteCommand("exec cs2-retakes/retakes.cfg");
        Logger.LogInfo("Server", "Retakes configuration executed");
    }

    private static void MigrateRetakesConfig()
    {
        try
        {
            var contents = File.ReadAllText(RetakesCfgPath);

            // The previously shipped default of mp_roundtime_defuse 0.25 makes the
            // 10 second bomb warning play at the wrong time. Only the exact old
            // default is migrated, custom values are left alone.
            var migrated = Regex.Replace(
                contents,
                @"(?m)^([ \t]*)mp_roundtime_defuse[ \t]+0\.25[ \t]*(\r?)$",
                "${1}mp_roundtime_defuse 1.25${2}");

            if (migrated != contents)
            {
                File.WriteAllText(RetakesCfgPath, migrated);
                Logger.LogInfo("Server", "Migrated mp_roundtime_defuse 0.25 -> 1.25 in retakes.cfg (fixes the bomb warning sound timing)");
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("Server", ex);
        }
    }

    public static void ExecuteRetakesUnloadConfiguration()
    {
        if (!File.Exists(RetakesUnloadCfgPath))
        {
            CreateRetakesUnloadConfig();
        }

        Server.ExecuteCommand("exec cs2-retakes/retakes_unload.cfg");
        Logger.LogInfo("Server", "Retakes unload configuration executed");
    }

    private static void CreateRetakesUnloadConfig()
    {
        try
        {
            Directory.CreateDirectory(RetakesCfgDirectory);

            var unloadCfgContents = @"
                // This file is executed when the retakes plugin is unloaded.
                // It restores the game convars that retakes.cfg changes back to
                // sensible defaults. Adjust these values to suit your server.
                bot_quota 10
                mp_autoteambalance 1
                mp_forcecamera 1
                mp_give_player_c4 1
                mp_halftime 1
                mp_join_grace_time 30
                mp_match_can_clinch 1
                mp_maxmoney 16000
                mp_playercashawards 1
                mp_teamcashawards 1
                mp_solid_teammates 1
                mp_warmup_pausetimer 0
                mp_roundtime_defuse 1.92
                mp_autokick 1
                mp_c4timer 40
                mp_freezetime 15
                mp_friendlyfire 0
                mp_round_restart_delay 7
                mp_match_restart_delay 25
                mp_maxrounds 24
                mp_timelimit 0
                mp_warmuptime 60
                sv_talk_enemy_dead 0
                sv_talk_enemy_living 0
                sv_deadtalk 0
                spec_replay_enable 1
                mp_death_drop_gun 1
                mp_death_drop_defuser 1
                mp_death_drop_grenade 1

                echo [Retakes] Unload config loaded!
            ";

            File.WriteAllText(RetakesUnloadCfgPath, unloadCfgContents, Encoding.UTF8);

            Logger.LogInfo("Server", "Created retakes_unload.cfg file");
        }
        catch (Exception ex)
        {
            Logger.LogException("Server", ex);
        }
    }

    private static void CreateRetakesConfig()
    {
        try
        {
            Directory.CreateDirectory(RetakesCfgDirectory);

            var retakesCfg = File.Create(RetakesCfgPath);

            var retakesCfgContents = @"
                // Things you shouldn't change:
                bot_kick
                bot_quota 0
                mp_autoteambalance 0
                mp_forcecamera 1
                mp_give_player_c4 0
                mp_halftime 0
                mp_ignore_round_win_conditions 0
                mp_join_grace_time 0
                mp_match_can_clinch 0
                mp_maxmoney 0
                mp_playercashawards 0
                mp_respawn_on_death_ct 0
                mp_respawn_on_death_t 0
                mp_solid_teammates 1
                mp_teamcashawards 0
                mp_warmup_pausetimer 0
                sv_skirmish_id 0

                // Things you can change, and may want to:
                mp_roundtime_defuse 1.25
                mp_autokick 0
                mp_c4timer 40
                mp_freezetime 1
                mp_friendlyfire 0
                mp_round_restart_delay 2
                sv_talk_enemy_dead 0
                sv_talk_enemy_living 0
                sv_deadtalk 1
                spec_replay_enable 0
                mp_maxrounds 30
                mp_match_end_restart 0
                mp_timelimit 0
                mp_match_restart_delay 10
                mp_death_drop_gun 1
                mp_death_drop_defuser 1
                mp_death_drop_grenade 1
                mp_warmuptime 15

                echo [Retakes] Config loaded!
            ";

            var retakesCfgBytes = Encoding.UTF8.GetBytes(retakesCfgContents);
            retakesCfg.Write(retakesCfgBytes, 0, retakesCfgBytes.Length);
            retakesCfg.Close();

            Logger.LogInfo("Server", "Created retakes.cfg file");
        }
        catch (Exception ex)
        {
            Logger.LogException("Server", ex);
        }
    }
}