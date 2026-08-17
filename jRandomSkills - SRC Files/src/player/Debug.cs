using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static CounterStrikeSharp.API.Core.Listeners;
using static src.jRandomSkills;

namespace src.player
{
    public static class Debug
    {
        private static string sessionId = "00000";
        private static readonly string debugFolder = Path.Combine(Instance.ModuleDirectory, "logs");
        private static StreamWriter? _writer;
        private static readonly object _writeLock = new();
        private static bool damageHooked;

        public static void Load()
        {
            sessionId = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
            lock (_writeLock) { _writer?.Dispose(); _writer = null; }

            if (Config.DebugFlags == DebugCategory.None)
                return;

            if (Config.DebugEnabled(DebugCategory.Round))
            {
                Instance.RegisterEventHandler<EventPlayerConnectFull>((@event, info) =>
                {
                    var player = PlayerManager.GetPlayerEvent(@event.Userid);
                    if (player == null || !player.IsValid) return HookResult.Continue;
                    WriteToDebug($"{(player.IsBot ? "Bot" : "Player")} {player.PlayerName} joined the game.", DebugCategory.Round);
                    return HookResult.Continue;
                });

                Instance.RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
                {
                    var player = PlayerManager.GetPlayerEvent(@event.Userid);
                    if (player == null || !player.IsValid) return HookResult.Continue;
                    WriteToDebug($"{(player.IsBot ? "Bot" : "Player")} {player.PlayerName} disconnected.", DebugCategory.Round);
                    return HookResult.Continue;
                });

                Instance.RegisterEventHandler<EventRoundStart>((@event, info) =>
                {
                    var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager").Where(t => t != null).ToList();
                    var tTeam = teams.FirstOrDefault(t => t.TeamNum == (int)CsTeam.Terrorist);
                    var ctTeam = teams.FirstOrDefault(t => t.TeamNum == (int)CsTeam.CounterTerrorist);
                    WriteToDebug($"Round #{tTeam?.Score + ctTeam?.Score + 1} (CT {ctTeam?.Score} : {tTeam?.Score} TT) started.{WarmupTag()}", DebugCategory.Round);
                    return HookResult.Continue;
                });

                Instance.RegisterEventHandler<EventRoundFreezeEnd>((@event, info) =>
                {
                    WriteToDebug($"Freeze time ended.{WarmupTag()}", DebugCategory.Round);
                    return HookResult.Continue;
                });

                Instance.RegisterEventHandler<EventRoundEnd>((@event, info) =>
                {
                    var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager").Where(t => t != null).ToList();
                    var tTeam = teams.FirstOrDefault(t => t.TeamNum == (int)CsTeam.Terrorist);
                    var ctTeam = teams.FirstOrDefault(t => t.TeamNum == (int)CsTeam.CounterTerrorist);
                    WriteToDebug($"Round #{tTeam?.Score + ctTeam?.Score} (CT {ctTeam?.Score} : {tTeam?.Score} TT) ended.{WarmupTag()}", DebugCategory.Round);
                    return HookResult.Continue;
                });

                Instance.RegisterEventHandler<EventPlayerDeath>((@event, info) =>
                {
                    var victim = PlayerManager.GetPlayerEvent(@event.Userid);
                    var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
                    if (victim != null)
                    {
                        if (attacker != null)
                            WriteToDebug($"{(victim.IsBot ? "Bot" : "Player")} {victim.PlayerName} died from {(attacker.IsBot ? "bot" : "player")} {attacker.PlayerName}.", DebugCategory.Round);
                        else
                            WriteToDebug($"{(victim.IsBot ? "Bot" : "Player")} {victim.PlayerName} died.", DebugCategory.Round);
                    }
                    return HookResult.Continue;
                });

                Instance.RegisterEventHandler<EventBombPlanted>((@event, info) =>
                {
                    WriteToDebug($"Bomb planted.", DebugCategory.Round);
                    return HookResult.Continue;
                });

                Instance.RegisterEventHandler<EventBombDefused>((@event, info) =>
                {
                    WriteToDebug($"Bomb defused.", DebugCategory.Round);
                    return HookResult.Continue;
                });
            }

            Instance.RegisterListener<OnMapStart>((mapName) =>
            {
                WriteToDebug($"Map changed: {mapName}.");
            });

            if (Config.DebugEnabled(DebugCategory.Damage))
            {
                VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Hook(OnTakeDamage, HookMode.Pre);
                damageHooked = true;
            }
        }

        public static void Unload()
        {
            if (damageHooked)
            {
                try { VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Unhook(OnTakeDamage, HookMode.Pre); }
                catch { }
                damageHooked = false;
            }

            lock (_writeLock) { _writer?.Dispose(); _writer = null; }
        }

        private static HookResult OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null || param2.Attacker == null || param2.Attacker.Value == null)
                return HookResult.Continue;

            CCSPlayerPawn attackerPawn = new(param2.Attacker.Value.Handle);
            CCSPlayerPawn victimPawn = new(param.Handle);

            if (attackerPawn.DesignerName != "player" || victimPawn.DesignerName != "player")
                return HookResult.Continue;

            if (attackerPawn == null || attackerPawn.Controller?.Value == null || victimPawn == null || victimPawn.Controller?.Value == null)
                return HookResult.Continue;

            CCSPlayerController attacker = PlayerManager.GetPlayerEvent(attackerPawn.Controller.Value.As<CCSPlayerController>())!;
            CCSPlayerController victim = PlayerManager.GetPlayerEvent(victimPawn.Controller.Value.As<CCSPlayerController>())!;

            var playerInfo = PlayerManager.GetPlayerByIndex(attacker!.Index);
            if (playerInfo == null) return HookResult.Continue;

            var nativeHitGroup = SkillUtils.GetHitGroup(param2);

            WriteToDebug($"{(victim.IsBot ? "Bot" : "Player")} {victim.PlayerName} took damage from {(attacker.IsBot ? "bot" : "player")} {attacker.PlayerName}. " +
                $"[dmg={param2.Damage:0.#} hp={victimPawn.Health}/{victimPawn.MaxHealth} armor={victimPawn.ArmorValue} hitgroup={nativeHitGroup} " +
                $"takes={victimPawn.TakesDamage} vskill={PlayerManager.GetPlayerByIndex(victim.Index)?.Skill} askill={playerInfo.Skill}]", DebugCategory.Damage);
            return HookResult.Continue;
        }

        private static string WarmupTag()
        {
            var gameRules = Instance?.GameRules;

            if (gameRules == null || gameRules.Handle == IntPtr.Zero)
            {
                PlayerOnTick.InitializeGameRules();
                gameRules = Instance?.GameRules;
            }

            if (gameRules == null || gameRules.Handle == IntPtr.Zero) return " [gamerules unavailable]";
            return gameRules.WarmupPeriod ? " [WARMUP]" : string.Empty;
        }

        public static void WriteToDebug(string message, DebugCategory category = DebugCategory.Core)
        {
            if (!Config.DebugEnabled(category))
                return;

            lock (_writeLock)
            {
                _writer ??= CreateWriter();
                _writer?.WriteLine($"[{DateTime.Now:dd.MM.yyyy HH:mm:ss}] {message}");
            }
        }

        private static StreamWriter? CreateWriter()
        {
            try
            {
                Directory.CreateDirectory(debugFolder);
                string path = Path.Combine(debugFolder, $"debug_{sessionId}.txt");
                return new StreamWriter(path, append: true, System.Text.Encoding.UTF8) { AutoFlush = true };
            }
            catch
            {
                return null;
            }
        }

        private static void GetAllEntityIndexes()
        {
            if (Instance.GameRules == null) return;

            var entities = Utilities.GetAllEntities();

            foreach (var entity in entities)
                if (entity != null && entity.IsValid && !string.IsNullOrEmpty(entity.DesignerName))
                {
                    string text = $"Entity: {entity.DesignerName}, ID: {entity.Index}";
                    Console.WriteLine(text);

                    string filename = $"debug_{sessionId}.txt";
                    string path = Path.Combine(debugFolder, filename);

                    Directory.CreateDirectory(debugFolder);
                    File.AppendAllText(path, text, System.Text.Encoding.UTF8);
                }
        }
    }
}