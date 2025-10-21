using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.menu;
using src.player;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.command
{
    public static class Command
    {
        private static bool gamePaused = false;
        private static Config.SettingsModel config = Config.LoadedConfig;
        private static readonly ConcurrentDictionary<string, CommandInfo.CommandCallback> oldCommands = [];
        private static readonly object setLock = new();

        public static void Load()
        {
            config = Config.LoadedConfig;
            if (config == null || config == null) return;

            lock (setLock)
            {
                if (!oldCommands.IsEmpty)
                {
                    foreach (var command in oldCommands)
                        Instance.RemoveCommand(command.Key, command.Value);
                    oldCommands.Clear();
                }

                var commands = new Dictionary<IEnumerable<string>, (string description, CommandInfo.CommandCallback handler)>
                {
                    { SplitCommands(config.NormalCommands.SetSkillCommand.Alias), ("Set skill", Command_SetSkill) },
                    { SplitCommands(config.NormalCommands.SkillsListCommand.Alias), ("Delete all records", Command_SkillsListMenu) },
                    { SplitCommands(config.NormalCommands.UseSkillCommand.Alias), ("Use/Type skill", Command_UseTypeSkill) },
                    { SplitCommands(config.NormalCommands.ConsoleCommand.Alias), ("Console command", Command_CustomCommand) },
                    { SplitCommands(config.NormalCommands.HealCommand.Alias), ("Heal", Command_Heal) },
                    { SplitCommands(config.NormalCommands.HudCommand.Alias), ("Enable/Disable HUD", Command_HUD) },
                    { SplitCommands(config.NormalCommands.SetStaticSkillCommand.Alias), ("Set static skill", Command_SetStaticSkill) },
                    { SplitCommands(config.NormalCommands.ChangeLanguageCommand.Alias), ("Change language", Command_ChangeLanguage) },
                    { SplitCommands(config.NormalCommands.ReloadCommand.Alias), ("Reaload configs", Command_Reload) },

                    { SplitCommands(config.VotingCommands.ChangeMapCommand.Alias), ("Change map", Command_ChangeMap) },
                    { SplitCommands(config.VotingCommands.StartGameCommand.Alias), ("Start game", Command_StartGame) },
                    { SplitCommands(config.VotingCommands.SwapCommand.Alias), ("Swap team", Command_Swap) },
                    { SplitCommands(config.VotingCommands.ShuffleCommand.Alias), ("Shuffle team", Command_Shuffle) },
                    { SplitCommands(config.VotingCommands.PauseCommand.Alias), ("Pause game", Command_Pause) },
                    { SplitCommands(config.VotingCommands.SetScoreCommand.Alias), ("Set teams score", Command_SetScore) },

                    { new []{ "show" }, ("Show layout", Command_Show) },
                };

                foreach (var commandPair in commands)
                    foreach (var command in commandPair.Key)
                    {
                        Instance.AddCommand($"css_{command}", commandPair.Value.description, commandPair.Value.handler);
                        oldCommands.TryAdd($"css_{command}", commandPair.Value.handler);
                    }
            }
        }

        private static IEnumerable<string> SplitCommands(string commands)
        {
            return commands.Split(',').Select(c => c.Trim()).Where(c => !string.IsNullOrEmpty(c));
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
        private static void Command_UseTypeSkill(CCSPlayerController? player, CommandInfo _)
        {
            if (player == null) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null || playerInfo.IsDrawing) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;
            if (!player.IsValid || !player.PawnIsAlive) return;

            string[] commands = _.ArgString.Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);
            Debug.WriteToDebug($"Player {player.PlayerName} used the skill: {playerInfo.Skill}");
            if (commands == null || commands.Length == 0)
                Instance.SkillAction(playerInfo.Skill.ToString(), "UseSkill", [player]);
            else
                Instance.SkillAction(playerInfo.Skill.ToString(), "TypeSkill", [player, commands]);
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_SetSkill(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_setskill {command.ArgString} command.");
            if (!string.IsNullOrEmpty(config.NormalCommands.SetSkillCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.NormalCommands.SetSkillCommand.Permissions)) return;
            var targetPlayer = Utilities.GetPlayers().FirstOrDefault(p => !p.IsBot
                                                                          && (p.SteamID.ToString().Equals(command.GetArg(1), StringComparison.CurrentCultureIgnoreCase)
                                                                          || p.PlayerName.Equals(command.GetArg(1), StringComparison.CurrentCultureIgnoreCase)));

            if (command.ArgCount < 2)
            {
                if (player == null)
                {
                    Server.PrintToConsole(Localization.GetTranslation("correct_form_setskill"));
                    return;
                }
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                SkillUtils.PrintToChat(player, player.GetTranslation("correct_form_setskill"), true);
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                return;
            }

            if (targetPlayer == null)
            {
                if (player == null)
                {
                    Server.PrintToConsole(Localization.GetTranslation("player_not_found_setskill"));
                    return;
                }
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                SkillUtils.PrintToChat(player, player.GetTranslation("player_not_found_setskill"), true);
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                return;
            }

            var skillName = command.ArgCount > 3 ? $"{command.GetArg(2)} {command.GetArg(3)}" : command.GetArg(2);
            var skill = SkillData.Skills.FirstOrDefault(s => player != null && player.GetSkillName(s.Skill).Equals(skillName, StringComparison.OrdinalIgnoreCase) || s.Skill.ToString().Equals(skillName, StringComparison.OrdinalIgnoreCase));

            if (skill == null)
            {
                if (player == null)
                {
                    Server.PrintToConsole(Localization.GetTranslation("skill_not_found_setskill"));
                    return;
                }
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                SkillUtils.PrintToChat(player, player.GetTranslation("skill_not_found_setskill"), true);
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                return;
            }

            var skillPlayer = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == targetPlayer.SteamID);
            if (skillPlayer != null)
            {
                Instance.SkillAction(skillPlayer.Skill.ToString(), "DisableSkill", [targetPlayer]);
                skillPlayer.Skill = skill.Skill;
                skillPlayer.SpecialSkill = src.player.Skills.None;
                Instance.SkillAction(skill.Skill.ToString(), "EnableSkill", [targetPlayer]);
                skillPlayer.SkillDescriptionHudExpired = DateTime.Now.AddSeconds(Config.LoadedConfig.SkillDescriptionDuration);

                if (player == null)
                {
                    Server.PrintToConsole(Localization.GetTranslation("done_setskill"));
                    return;
                }

                if (skill.Display)
                    SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skill.Skill)}{ChatColors.Lime}: {player.GetSkillDescription(skill.Skill)}", false);

                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                SkillUtils.PrintToChat(player, $"{player.GetTranslation("done_setskill")}: {ChatColors.LightRed}{player.GetSkillName(skill.Skill)} {ChatColors.Lime}{player.GetTranslation("for_setskill")} {ChatColors.LightRed}{targetPlayer.PlayerName}", false);
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
            }
            else
            {
                if (player == null)
                {
                    Server.PrintToConsole(Localization.GetTranslation("error_setskill"));
                    return;
                }

                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                SkillUtils.PrintToChat(player, player.GetTranslation("error_setskill"), true);
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
            }
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
        private static void Command_SkillsListMenu(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_skills {command.ArgString} command.");
            if (player == null) return;
            if (!string.IsNullOrEmpty(config.NormalCommands.SkillsListCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.NormalCommands.SkillsListCommand.Permissions)) return;
            Menu.DisplaySkillsList(player);
        }

        [CommandHelper(minArgs: 1, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_ChangeMap(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_map {command.ArgString} command.");
            if (player != null && player.IsValid)
                if (!string.IsNullOrEmpty(config.VotingCommands.ChangeMapCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.VotingCommands.ChangeMapCommand.Permissions))
                {
                    if (!config.VotingCommands.ChangeMapCommand.EnableVoting) return;
                    player.Vote(VoteType.ChangeMap, command.ArgString);
                    return;
                }
            ChangeMap(command);
        }

        private static void ChangeMap(CommandInfo command)
        {
            string map = command.GetArg(1).ToLower();

            if (string.IsNullOrEmpty(map))
            {
                command.ReplyToCommand($" {ChatColors.Red}{command.CallingPlayer?.GetTranslation("invalid_map")}");
                return;
            }

            Localization.PrintTranslationToChatAll($" {ChatColors.Yellow}{{0}} ({ChatColors.Green}{map}{ChatColors.Yellow})...", ["loading_map"]);

            if (uint.TryParse(map, out _))
                Server.ExecuteCommand($"host_workshop_map {map}");
            else if (!Server.IsMapValid(map))
                command.ReplyToCommand($" {ChatColors.Red}{command.CallingPlayer?.GetTranslation("invalid_map")}");
            else
                Server.ExecuteCommand($"changelevel {map}");
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_StartGame(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_start {command.ArgString} command.");
            if (player != null && player.IsValid)
                if (!string.IsNullOrEmpty(config.VotingCommands.StartGameCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.VotingCommands.StartGameCommand.Permissions))
                {
                    if (!config.VotingCommands.StartGameCommand.EnableVoting) return;
                    player.Vote(VoteType.StartGame);
                    return;
                }
            StartGame(command);
        }

        private static void StartGame(CommandInfo command)
        {
            int cheats = command.GetArg(1) == "sv" ? 1 : 0;

            foreach (string consoleCommand in cheats == 1
                                ? Config.LoadedConfig.VotingCommands.StartGameCommand.SVStartParams.Split(";")
                                : Config.LoadedConfig.VotingCommands.StartGameCommand.StartParams.Split(";"))
                Server.ExecuteCommand(consoleCommand);

            if (Instance?.GameRules?.WarmupPeriod == true)
            {
                Server.ExecuteCommand("mp_warmup_end");
                Localization.PrintTranslationToChatAll($" {ChatColors.Green}{{0}}", ["game_start"]);
            }
            else
            {
                Server.ExecuteCommand("mp_restartgame 2");
                Instance?.AddTimer(2.0f, () => Localization.PrintTranslationToChatAll($" {ChatColors.Green}{{0}}", ["game_start"]));
            }
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_Swap(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_swap {command.ArgString} command.");
            if (player != null && player.IsValid)
                if (!string.IsNullOrEmpty(config.VotingCommands.SwapCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.VotingCommands.SwapCommand.Permissions))
                {
                    if (!config.VotingCommands.SwapCommand.EnableVoting) return;
                    player.Vote(VoteType.SwapTeam);
                    return;
                }
            Swap();
        }

        private static void Swap()
        {
            foreach (var player in Utilities.GetPlayers())
                if (Instance.IsPlayerValid(player) && new CsTeam[] { CsTeam.CounterTerrorist, CsTeam.Terrorist }.Contains(player.Team))
                    player.SwitchTeam(player.Team == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist);
            Server.ExecuteCommand($"mp_restartgame 1");
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_Shuffle(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_shuffle {command.ArgString} command.");
            if (player != null && player.IsValid)
                if (!string.IsNullOrEmpty(config.VotingCommands.ShuffleCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.VotingCommands.ShuffleCommand.Permissions))
                {
                    if (!config.VotingCommands.ShuffleCommand.EnableVoting) return;
                    player.Vote(VoteType.ShuffleTeam);
                    return;
                }
            Shuffle();
        }

        private static void Shuffle()
        {
            var players = Utilities.GetPlayers().FindAll(p => Instance.IsPlayerValid(p) && new CsTeam[] { CsTeam.CounterTerrorist, CsTeam.Terrorist }.Contains(p.Team));
            double CTlimit = Instance.Random.Next(0, 2) == 0 ? Math.Floor(players.Count / 2.0) : Math.Ceiling(players.Count / 2.0);

            foreach (var player in players.OrderBy(_ => Instance.Random.Next()).ToList())
            {
                player?.SwitchTeam(CTlimit > 0 ? CsTeam.CounterTerrorist : CsTeam.Terrorist);
                CTlimit--;
            }
            Server.ExecuteCommand($"mp_restartgame 1");
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_Pause(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_pause {command.ArgString} command.");
            if (player != null && player.IsValid)
                if (!string.IsNullOrEmpty(config.VotingCommands.PauseCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.VotingCommands.PauseCommand.Permissions))
                {
                    if (!config.VotingCommands.PauseCommand.EnableVoting) return;
                    player.Vote(VoteType.PauseGame);
                    return;
                }
            Pause();
        }

        private static void Pause()
        {
            Localization.PrintTranslationToChatAll($" {(gamePaused ? ChatColors.Green : ChatColors.Red)}{{0}}", [gamePaused ? "unpause" : "pause"]);
            Server.ExecuteCommand(gamePaused ? "mp_unpause_match" : "mp_pause_match");
            gamePaused = !gamePaused;
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_Heal(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_heal {command.ArgString} command.");
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            if (!string.IsNullOrEmpty(config.NormalCommands.HealCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.NormalCommands.HealCommand.Permissions)) return;
            SkillUtils.AddHealth(player.PlayerPawn.Value, 100);
            player.PrintToChat($" {ChatColors.Green}{player.GetTranslation("healed")}");
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
        private static void Command_HUD(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_hud {command.ArgString} command.");
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            if (!string.IsNullOrEmpty(config.NormalCommands.HudCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.NormalCommands.HudCommand.Permissions)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;

            playerInfo.DisplayHUD = !playerInfo.DisplayHUD;
            SkillUtils.CloseMenu(player);
            player.PrintToChat($" {(playerInfo.DisplayHUD ? ChatColors.Green : ChatColors.Red)}{player.GetTranslation(playerInfo.DisplayHUD ? "hud_on" : "hud_off")}");
        }

        [CommandHelper(minArgs: 2, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_SetScore(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_setscore {command.ArgString} command.");
            if (player != null && player.IsValid)
                if (!string.IsNullOrEmpty(config.VotingCommands.SetScoreCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.VotingCommands.SetScoreCommand.Permissions))
                {
                    if (!config.VotingCommands.SetScoreCommand.EnableVoting) return;
                    player.Vote(VoteType.SetScore, command.ArgString);
                    return;
                }
            SetScore(player, command);
        }

        private static void SetScore(CCSPlayerController? player, CommandInfo command)
        {
            if (!int.TryParse(command.GetArg(1), out int ctScore) || !int.TryParse(command.GetArg(2), out int tScore))
            {
                if (player != null && player.IsValid)
                    SkillUtils.PrintToChat(player, player.GetTranslation("correct_form_setscore"), true);
                return;
            }

            SkillUtils.SetTeamScores((short)ctScore, (short)tScore, RoundEndReason.RoundDraw);
        }

        [CommandHelper(minArgs: 1, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_CustomCommand(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_console {command.ArgString} command.");
            if (player == null) return;
            if (!string.IsNullOrEmpty(config.NormalCommands.ConsoleCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.NormalCommands.ConsoleCommand.Permissions)) return;
            string param = command.ArgString;
            Server.ExecuteCommand(param);
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_SetStaticSkill(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_setstaticskill {command.ArgString} command.");
            if (!string.IsNullOrEmpty(config.NormalCommands.SetStaticSkillCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.NormalCommands.SetStaticSkillCommand.Permissions)) return;
            var targetPlayer = Utilities.GetPlayers().FirstOrDefault(p => !p.IsBot
                                                                          && (p.SteamID.ToString().Equals(command.GetArg(1), StringComparison.CurrentCultureIgnoreCase)
                                                                          || p.PlayerName.Equals(command.GetArg(1), StringComparison.CurrentCultureIgnoreCase)));

            if (command.ArgCount < 2)
            {
                if (player == null)
                {
                    Server.PrintToConsole(Localization.GetTranslation("correct_form_setskill"));
                    return;
                }

                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                SkillUtils.PrintToChat(player, player.GetTranslation("correct_form_setskill"), true);
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                return;
            }

            if (targetPlayer == null)
            {
                if (player == null)
                {
                    Server.PrintToConsole(Localization.GetTranslation("player_not_found_setskill"));
                    return;
                }

                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                SkillUtils.PrintToChat(player, player.GetTranslation("player_not_found_setskill"), true);
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                return;
            }

            var skillName = command.ArgCount > 3 ? $"{command.GetArg(2)} {command.GetArg(3)}" : command.GetArg(2);
            var skill = SkillData.Skills.FirstOrDefault(s => player != null && player.GetSkillName(s.Skill).Equals(skillName, StringComparison.OrdinalIgnoreCase) || s.Skill.ToString().Equals(skillName, StringComparison.OrdinalIgnoreCase));

            if (skill == null)
            {
                if (player == null)
                {
                    Server.PrintToConsole(Localization.GetTranslation("skill_not_found_setskill"));
                    return;
                }

                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                SkillUtils.PrintToChat(player, player.GetTranslation("skill_not_found_setskill"), true);
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                return;
            }

            var skillPlayer = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == targetPlayer.SteamID);
            if (skillPlayer != null)
            {
                Instance.SkillAction(skillPlayer.Skill.ToString(), "DisableSkill", [targetPlayer]);
                skillPlayer.Skill = skill.Skill;
                skillPlayer.SpecialSkill = src.player.Skills.None;
                skillPlayer.SkillDescriptionHudExpired = DateTime.Now.AddSeconds(Config.LoadedConfig.SkillDescriptionDuration);

                if (skill.Skill == src.player.Skills.None)
                    Event.staticSkills.TryRemove(targetPlayer.SteamID, out _);
                else
                    Event.staticSkills.TryAdd(targetPlayer.SteamID, skill);
                Instance.SkillAction(skill.Skill.ToString(), "EnableSkill", [targetPlayer]);

                if (player == null)
                {
                    Server.PrintToConsole(Localization.GetTranslation("done_setskill"));
                    return;
                }

                if (skill.Display)
                    SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skill.Skill)}{ChatColors.Lime}: {player.GetSkillDescription(skill.Skill)}", false);

                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                SkillUtils.PrintToChat(player, $"{player.GetTranslation("done_setskill")}: {ChatColors.LightRed}{player.GetSkillName(skill.Skill)} {ChatColors.Lime}{player.GetTranslation("for_setskill")} {ChatColors.LightRed}{targetPlayer.PlayerName}", false);
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
            }
            else
            {
                if (player == null)
                {
                    Server.PrintToConsole(Localization.GetTranslation("error_setskill"));
                    return;
                }

                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
                SkillUtils.PrintToChat(player, player.GetTranslation("error_setskill"), true);
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◥◣◆◢◤{ChatColors.Green}―――――――――――");
            }
        }

        [CommandHelper(minArgs: 1, whoCanExecute: CommandUsage.CLIENT_ONLY)]
        private static void Command_ChangeLanguage(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_lang {command.ArgString} command.");
            if (player == null || !player.IsValid) return;
            if (!string.IsNullOrEmpty(config.NormalCommands.ChangeLanguageCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.NormalCommands.ChangeLanguageCommand.Permissions)) return;

            string fileName;
            string newLangCode = command.GetArg(1).ToUpper();
            foreach (var langInfo in Config.LoadedConfig.LanguageSystem.LanguageInfos)
                if (langInfo.IsoCodes.Contains(newLangCode))
                    fileName = langInfo.FileName;
            if (!Localization.HasTranslation(newLangCode))
                fileName = Config.LoadedConfig.LanguageSystem.DefaultLangCode;
            else
                fileName = newLangCode.ToLower();
            Localization.ChangePlayerLanguage(player, fileName);
        }

        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_Reload(CCSPlayerController? player, CommandInfo command)
        {
            Debug.WriteToDebug($"Player {player?.PlayerName} used the css_reload {command.ArgString} command.");
            if (!string.IsNullOrEmpty(config.NormalCommands.ReloadCommand.Permissions) && !AdminManager.PlayerHasPermissions(player, config.NormalCommands.ReloadCommand.Permissions)) return;

            lock (setLock)
            {
                Config.LoadConfig();
                SkillsInfo.LoadSkillsInfo();
                Localization.Load();
                Load();

                SkillData.Skills.Clear();
                foreach (var skill in Enum.GetValues(typeof(Skills)))
                    if (SkillsInfo.GetValue<bool>(skill, "active"))
                        Instance.SkillAction(skill.ToString()!, "LoadSkill");

                if (player != null && player.IsValid)
                    player.PrintToChat($" {ChatColors.Green}{player.GetTranslation("reload")}");
                else
                    Server.PrintToConsole($" {ChatColors.Green}{Localization.GetTranslation("reload")}");

                foreach (var target in Instance.SkillPlayer)
                {
                    if (SkillsInfo.GetValue<bool>(target.Skill, "active") == false)
                        target.Skill = Event.noneSkill.Skill;
                    if (SkillsInfo.GetValue<bool>(target.SpecialSkill, "active") == false)
                        target.SpecialSkill = Event.noneSkill.Skill;
                }
            }
        }













        [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        private static void Command_Show(CCSPlayerController? player, CommandInfo command)
        {
            MemoryFunctionVoid show_layout = new("55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC 58 80 3D ? ? ? ? 00 75 ? 0F 28 05 ? ? ? ? C6 05 ? ? ? ? 01 0F 29 05 ? ? ? ? 80 3D ? ? ? ? 00 75 ? 66 0F EF C0 C6 05 ? ? ? ? 01 0F 29 05 ? ? ? ? 80 3D ? ? ? ? 00 0F 84 ? ? ? ? 80 3D ? ? ? ? 00 75 ? 31 D2 C6 05 ? ? ? ? 01 48 89 15 ? ? ? ? 48 89 15 ? ? ? ? C6 05 ? ? ? ? 00 80 3D ? ? ? ? 00 0F 85 ? ? ? ? C6 05 ? ? ? ? 01 4C 8D 3D ? ? ? ? 49 8D 9F ? ? ? ? 49 C7 47 ? 00 00 00 00 BE 00 04 00 00 4C 89 FF 45 31 F6 41 C7 47 ? 00 00 00 00 41 C7 47 ? 00 00 00 00 41 C7 07 00 00 00 00 E8 ? ? ? ? EB ? ? ? ? ? ? ? ? 74 ? 41 C7 45 00 00 00 00 00 4D 8B 6F ? 4D 01 F5 41 81 65 ? 00 00 00 C0 49 83 C6 18 49 81 FE 00 60 00 00 0F 84 ? ? ? ? 4D 8B 6F ? 4D 01 F5 45 8B 65 00 45 85 E4 79 ? 41 8B 7D ? 85 FF 0F 88 ? ? ? ? 49 8B 45 ? 49 63 D4 44 89 E6 41 C7 45 00 00 00 00 00 48 8D 14 ? F7 DE 48 C1 E2 03 48 01 D0 48 C7 40 ? 00 00 00 00 48 C7 40 ? 00 00 00 00 48 C7 40 ? 00 00 00 00 41 83 FC FF 74 ? 48 83 C2 28 B9 01 00 00 00 66 66 2E 0F 1F 84 ? 00 00 00 00 66 66 2E 0F 1F 84 ? 00 00 00 00 66 0F 1F 44 ? 00 49 8B 45 ? 83 C1 01 48 01 D0 48 83 C2 28 48 C7 40 ? 00 00 00 00 48 C7 40 ? 00 00 00 00 48 C7 40 ? 00 00 00 00 39 CE 7F ? E9 ? ? ? ? ? 41 8B 75 ? 81 FE FF FF FF 3F 76 ? 89 F0 25 00 00 00 C0 3D 00 00 00 80 0F 85 ? ? ? ? 81 E6 FF FF FF 3F 31 D2 B9 28 00 00 00 E8 ? ? ? ? 49 63 55 ? 66 0F EF C9 31 F6 66 0F 6E C0 49 8B 7D ? 66 0F 38 3D C1 66 0F 7E C0 66 0F 7E 45 ? 48 8D 0C ? 48 98 48 8D 14 ? 48 C1 E1 03 48 C1 E2 03 41 81 7D ? FF FF FF 3F 40 0F 96 C6 E8 ? ? ? ? 41 8B 55 ? 66 0F 6E 45 ? 49 89 45 ? 81 FA FF FF FF 3F 76 ? 81 E2 FF FF FF 3F 41 89 55 ? 66 41 0F 7E 45 ? E9 ? ? ? ? ? ? ? 4D 8D 6F ? 31 C0 41 C6 47 ? 01 41 C7 47 ? FF 03 00 00 4C 89 EF 49 C7 47 ? 00 00 00 00 66 41 89 47 ? 41 C6 47 ? 00 49 C7 47 ? 00 00 00 00 E8 ? ? ? ? BE 01 00 00 00 4C 89 EF E8 ? ? ? ? 49 8D 7F ? 4C 89 EE 49 83 C7 68 E8 ? ? ? ? 4C 39 FB 0F 85 ? ? ? ? 48 8D 05 ? ? ? ? 49 89 DF 48 39 C3 0F 85 ? ? ? ? 48 8D 15 ? ? ? ? 31 F6 48 8D 3D ? ? ? ? E8 ? ? ? ? 80 3D ? ? ? ? 00 4C 8D 65");
            show_layout.Invoke();
        }
    }
}