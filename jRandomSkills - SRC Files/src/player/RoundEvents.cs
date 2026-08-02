using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using RayTraceAPI;
using src.player.skills;
using src.utils;
using System.Collections.Concurrent;
using static CounterStrikeSharp.API.Core.Listeners;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player
{
    public static partial class Event
    {
        private static jSkill_SkillInfo ChooseSkillByRarityAndMax(List<jSkill_SkillInfo> candidates, Dictionary<Skills, int> assignmentCounts, Config.GameModes gameMode)
        {
            if (candidates == null || candidates.Count == 0) return noneSkill;

            bool ignoreMax = gameMode == Config.GameModes.SameSkills || gameMode == Config.GameModes.TeamSkills;

            const int attempts = 6;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var (roll, rolled) = RarityManager.RollRarity();

                var filtered = candidates.Where(s =>
                {
                    if (s == null) return false;
                    var def = SkillsInfo.LoadedConfig.FirstOrDefault(d => d.Name == s.Skill.ToString());
                    if (def == null) return false;

                    if (!string.Equals(def.Rarity ?? string.Empty, rolled.ToString(), StringComparison.OrdinalIgnoreCase))
                        return false;

                    if (!ignoreMax && def.MaxPerServer >= 0)
                    {
                        int current = assignmentCounts.TryGetValue(s.Skill, out var c) ? c : 0;
                        if (current >= def.MaxPerServer) return false;
                    }

                    return true;
                }).ToList();

                if (filtered.Count > 0)
                    return filtered[Random.Shared.Next(filtered.Count)];
            }

            var fallback = candidates.Where(s =>
            {
                var def = SkillsInfo.LoadedConfig.FirstOrDefault(d => d.Name == s.Skill.ToString());
                if (def == null) return true;
                if (ignoreMax) return true;
                if (def.MaxPerServer < 0) return true;
                int current = assignmentCounts.TryGetValue(s.Skill, out var c) ? c : 0;
                return current < def.MaxPerServer;
            }).ToList();

            if (fallback.Count > 0)
                return fallback[Random.Shared.Next(fallback.Count)];

            return candidates[Random.Shared.Next(candidates.Count)];
        }

        private static HookResult RoundStart(EventRoundStart @event, GameEventInfo info)
        {
            lock (setLock)
            {
                bool isWarmup = Instance.GameRules == null || Instance.GameRules.WarmupPeriod == true;
                isTransmitRegistered = false;
                SkillUtils.ClearKillCredits();
                SkillUtils.ClearCurses();
                Instance.AddTimer(.1f, () => DisableAll(), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

                foreach (var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsHLTV && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist))
                {
                    var skillPlayer = PlayerManager.GetPlayerByIndex(player!.Index);
                    if (skillPlayer == null) continue;
                    skillPlayer.IsDrawing = !isWarmup;
                    skillPlayer.PrintHTML = null;
                }

                Instance.RemoveListener<CheckTransmit>(CheckTransmit);
                int freezetime = ConVar.Find("mp_freezetime")?.GetPrimitiveValue<Int32>() ?? 0;
                freezeTimeEnd = DateTime.Now.AddSeconds(freezetime + (Instance?.GameRules?.TeamIntroPeriod == true ? 7 : 0));

                setSkillTimer?.Kill();

                if (isWarmup)
                {
                    setSkillTimer = Instance?.AddTimer(1f, SetSkill, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                    return HookResult.Continue;
                }

                float timeToDraw = (Instance?.GameRules?.TeamIntroPeriod == true ? 7 : 0) + Math.Max(freezetime - Config.LoadedConfig.SkillTimeBeforeStart, 0) + .3f;
                setSkillTimer = Instance?.AddTimer(timeToDraw, SetSkill, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                return HookResult.Continue;
            }
        }

        private static void DisableAll()
        {
            long perfStart = PerfLog.Start();
            DisableAllCore();
            PerfLog.End("DisableAll total", perfStart, 2.0);
        }

        private static void DisableAllCore()
        {
            lock (setLock)
            {
                // Re-register CheckTransmit so the dying-entity filter covers the kills below.
                EnableTransmit();

                Fortnite.skillInThisRound = false;
                EntityManager.DestroyAllTracked();

                foreach (var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid))
                {
                    if (player == null || !player.IsValid) continue;

                    var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                    if (playerInfo == null) continue;

                    ActiveSkillsThisRound.TryAdd(playerInfo.Skill.ToString(), 0);
                    SkillsUsedThisMap.TryAdd(playerInfo.Skill.ToString(), 0);
                    if (playerInfo.SpecialSkill != noneSkill.Skill)
                    {
                        ActiveSkillsThisRound.TryAdd(playerInfo.SpecialSkill.ToString(), 0);
                        SkillsUsedThisMap.TryAdd(playerInfo.SpecialSkill.ToString(), 0);
                    }

                    Instance.SkillAction(playerInfo.Skill.ToString(), "DisableSkill", [player]);

                    playerInfo.Skill = noneSkill.Skill;
                    playerInfo.SpecialSkill = noneSkill.Skill;
                    playerInfo.PrintHTML = null;
                    playerInfo.SkillChance = 1;
                    playerInfo.SkillUsed = false;

                    RestorePlayer(player);
                }

                // Reset every skill used so far on this map, not only the ones held this round: a skill
                // nobody drew now would otherwise never clear state left over from an earlier round.
                // Skills that never ran cannot hold state, so they stay out of the sweep.
                foreach (var skillName in SkillsUsedThisMap.Keys)
                    SafeNewRound(skillName);
                ActiveSkillsThisRound.Clear();
                tickFailuresLogged.Clear();
            }
        }

        private static void SafeNewRound(string skillName)
        {
            try
            {
                Instance.SkillAction(skillName, "NewRound");
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[jRandomSkills] {skillName}.NewRound failed, cleanup continues: {(ex.InnerException ?? ex).Message}");
            }
        }

        public static void OnMapChange()
        {
            lock (setLock)
            {
                isTransmitRegistered = false;
                Instance.RemoveListener<CheckTransmit>(CheckTransmit);

                Fortnite.skillInThisRound = false;

                EntityManager.SuppressKills = true;
                try
                {
                    EntityManager.DestroyAllTracked();
                    foreach (var skill in SkillData.Skills)
                        SafeNewRound(skill.Skill.ToString());
                }
                finally
                {
                    EntityManager.SuppressKills = false;
                }

                ActiveSkillsThisRound.Clear();
                SkillsUsedThisMap.Clear();
                nextRoundPicks.Clear();

                playersSkills.Clear();
                staticSkills.Clear();

                ctSkill = noneSkill;
                tSkill = noneSkill;
                allSkill = noneSkill;

                PlayerManager.Clear();

                ConVar.Find("sv_legacy_jump")?.SetValue("1");
            }
        }

        private static HookResult RoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            Illiterate.Disable();
            DispatchToActiveSkills("RoundEnd");

            lock (setLock)
            {
                Instance.AddTimer(.5f, () =>
                {
                    if (!Config.LoadedConfig.SummaryAfterTheRound) return;

                    var _players = Utilities.GetPlayers().Where(p => p.IsValid && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist).OrderBy(p => p.Team).ToList();

                    foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid))
                    {
                        string skillsText = "";
                        foreach (var _player in _players)
                        {
                            var _playerSkill = PlayerManager.GetPlayerByIndex(_player.Index);
                            if (_playerSkill == null) continue;

                            var skillInfo = SkillData.Skills.FirstOrDefault(s => s.Skill == _playerSkill.Skill);
                            var specialSkillInfo = SkillData.Skills.FirstOrDefault(s => s.Skill == _playerSkill.SpecialSkill);
                            if (skillInfo == null) continue;

                            skillsText += $" {ChatColors.DarkRed}\u202A{_player.PlayerName}\u202C{ChatColors.Lime}: {(_playerSkill.SpecialSkill == Skills.None || specialSkillInfo == null ? player.GetSkillName(skillInfo.Skill, _playerSkill.SkillChance) : $"{player.GetSkillName(specialSkillInfo.Skill)} -> {player.GetSkillName(skillInfo.Skill, _playerSkill.SkillChance)}")}\n";
                        }

                        if (string.IsNullOrEmpty(skillsText)) continue;

                        SkillUtils.PrintToChat(player, string.Empty, title: player.GetTranslation("summary"), border: "t");
                        foreach (string text in skillsText.Split("\n"))
                            if (!string.IsNullOrEmpty(text))
                                SkillUtils.PrintToChat(player, text, title: player.GetTranslation("teammate_skills"), border: "");
                        SkillUtils.PrintToChat(player, string.Empty, border: "b");
                    }
                }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

                // Before the optional disable below, so the "don't repeat the current skill"
                // exclusion still sees this round's skills.
                Instance.AddTimer(.6f, PrecomputeNextRoundSkills, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

                if (Config.LoadedConfig.DisableSkillsOnRoundEnd)
                {
                    isTransmitRegistered = false;
                    Instance.AddTimer(1f, () => DisableAll(), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                    Instance.RemoveListener<CheckTransmit>(CheckTransmit);
                }
                return HookResult.Continue;
            }
        }

        private static void SetSkill()
        {
            long perfStart = PerfLog.Start();
            SetSkillCore();
            PerfLog.End("SetSkill total", perfStart, 2.0);
        }

        private sealed class PickContext
        {
            public required List<jSkill_SkillInfo> BaseList { get; init; }
            public required Dictionary<Skills, string> RequiredPermissions { get; init; }
            public required HashSet<Skills> NeedsTeammates { get; init; }
            public required HashSet<Skills> CtOnly { get; init; }
            public required HashSet<Skills> TOnly { get; init; }
            public required int TerroristCount { get; init; }
            public required int CounterTerroristCount { get; init; }
        }

        private static PickContext BuildPickContext(List<CCSPlayerController> validPlayers)
        {
            Dictionary<Skills, string> perms = [];
            foreach (var s in SkillData.Skills)
            {
                if (s == null || s.Skill == Skills.None) continue;
                string perm = SkillsInfo.GetValue<string>(s.Skill, "requiredPermission");
                if (!string.IsNullOrEmpty(perm)) perms[s.Skill] = perm;
            }

            return new PickContext
            {
                BaseList = [.. SkillData.Skills.Where(s => s != null && s.Skill != Skills.None)],
                RequiredPermissions = perms,
                NeedsTeammates = ToSkillSet(SkillsInfo.LoadedConfig.Where(s => s.NeedsTeammates).Select(s => s.Name)),
                CtOnly = ToSkillSet(counterterroristSkills.Select(s => s.Name)),
                TOnly = ToSkillSet(terroristSkills.Select(s => s.Name)),
                TerroristCount = validPlayers.Count(p => p.Team == CsTeam.Terrorist),
                CounterTerroristCount = validPlayers.Count(p => p.Team == CsTeam.CounterTerrorist),
            };
        }

        private static HashSet<Skills> ToSkillSet(IEnumerable<string> names)
        {
            HashSet<Skills> set = [];
            foreach (var name in names)
                if (Enum.TryParse<Skills>(name, out var skill)) set.Add(skill);
            return set;
        }

        private static jSkill_SkillInfo PickSkillForPlayer(CCSPlayerController player, jSkill_PlayerInfo skillPlayer, PickContext ctx, Dictionary<Skills, int> assignmentCounts, Config.GameModes gameMode)
        {
            List<jSkill_SkillInfo> skillList = [.. ctx.BaseList];

            if (!player.IsBot && ctx.RequiredPermissions.Count != 0)
                skillList.RemoveAll(s => ctx.RequiredPermissions.TryGetValue(s.Skill, out var perm) && !AdminManager.PlayerHasPermissions(player, perm));

            if (gameMode != Config.GameModes.FullRandom)
                skillList.RemoveAll(s => s?.Skill == skillPlayer?.Skill || s?.Skill == skillPlayer?.SpecialSkill);

            int teamCount = player.Team == CsTeam.Terrorist ? ctx.TerroristCount : ctx.CounterTerroristCount;
            if (teamCount == 1)
                skillList.RemoveAll(s => ctx.NeedsTeammates.Contains(s.Skill));

            if (player.Team == CsTeam.Terrorist)
                skillList.RemoveAll(s => ctx.CtOnly.Contains(s.Skill));
            else
                skillList.RemoveAll(s => ctx.TOnly.Contains(s.Skill));

            if (gameMode == Config.GameModes.NoRepeat && playersSkills.TryGetValue(player.Index, out ConcurrentBag<jSkill_SkillInfo>? skills))
            {
                skillList.RemoveAll(s => skills.Any(s2 => s2.Skill == s.Skill));
                if (skillList.Count == 0) skills.Clear();
            }

            var randomSkill = skillList.Count == 0 ? noneSkill : ChooseSkillByRarityAndMax(skillList, assignmentCounts, gameMode);

            if (gameMode == Config.GameModes.NoRepeat)
            {
                if (playersSkills.TryGetValue(player.Index, out ConcurrentBag<jSkill_SkillInfo>? value))
                    value.Add(randomSkill);
                else
                    playersSkills.TryAdd(player.Index, [randomSkill]);
            }

            return randomSkill;
        }

        private static bool IsPickStillValid(jSkill_SkillInfo pick, CCSPlayerController player, List<CCSPlayerController> validPlayers, Dictionary<Skills, int> assignmentCounts)
        {
            if (pick.Skill == Skills.None) return true;
            if (!SkillData.Skills.Any(s => s.Skill == pick.Skill)) return false;

            string name = pick.Skill.ToString();
            if (player.Team == CsTeam.Terrorist && counterterroristSkills.Any(s => s.Name == name)) return false;
            if (player.Team == CsTeam.CounterTerrorist && terroristSkills.Any(s => s.Name == name)) return false;

            var def = SkillsInfo.LoadedConfig.FirstOrDefault(d => d.Name == name);
            if (def == null) return false;
            if (def.NeedsTeammates && validPlayers.Count(p => p.Team == player.Team) == 1) return false;
            if (def.MaxPerServer >= 0 && assignmentCounts.TryGetValue(pick.Skill, out var c) && c >= def.MaxPerServer) return false;

            return true;
        }

        // Runs at round end so the expensive skill selection is off the round-start hot path;
        // SetSkillCore then only applies the picks.
        private static void PrecomputeNextRoundSkills()
        {
            long perfStart = PerfLog.Start();
            lock (setLock)
            {
                nextRoundPicks.Clear();

                var gameMode = (Config.GameModes)Config.LoadedConfig.GameMode;
                if (gameMode is not (Config.GameModes.Normal or Config.GameModes.FullRandom or Config.GameModes.NoRepeat)) return;
                if (Instance?.GameRules == null || Instance.GameRules.WarmupPeriod == true) return;

                var validPlayers = Utilities.GetPlayers()
                    .Where(p => p != null && p.IsValid && !p.IsHLTV)
                    .Where(p => { try { return p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist; } catch { return false; } }).ToList();

                var ctx = BuildPickContext(validPlayers);

                Dictionary<Skills, int> assignmentCounts = [];
                foreach (var player in validPlayers)
                {
                    var skillPlayer = PlayerManager.GetPlayerByIndex(player.Index);
                    if (skillPlayer == null) continue;

                    var pick = PickSkillForPlayer(player, skillPlayer, ctx, assignmentCounts, gameMode);
                    nextRoundPicks[player.Index] = pick;

                    if (pick.Skill != Skills.None)
                        assignmentCounts[pick.Skill] = assignmentCounts.TryGetValue(pick.Skill, out var c) ? c + 1 : 1;
                }
            }
            PerfLog.End("PrecomputeSkills total", perfStart, 2.0);
        }

        public static void UpdateSkillHudExpired(jSkill_PlayerInfo skillPlayer, Skills skill)
        {
            float globalHudExpired = Config.LoadedConfig.SkillHudDuration;
            float? skillHudExpired = SkillsInfo.GetValue<float?>(skill, "hudDuration");

            skillPlayer.SkillHudExpired =
                !skillHudExpired.HasValue ?
                    (globalHudExpired == -1 ? DateTime.MaxValue : DateTime.Now.AddSeconds(globalHudExpired))
                : skillHudExpired.Value == -1 ? DateTime.MaxValue
                : DateTime.Now.AddSeconds(skillHudExpired.Value);

            float globalDescriptionHudExpired = Config.LoadedConfig.SkillDescriptionDuration;
            float? skillDescriptionHudExpired = SkillsInfo.GetValue<float?>(skill, "descriptionHudDuration");

            skillPlayer.SkillDescriptionHudExpired =
                !skillDescriptionHudExpired.HasValue ?
                    (globalDescriptionHudExpired == -1 ? DateTime.MaxValue : DateTime.Now.AddSeconds(globalDescriptionHudExpired))
                : skillDescriptionHudExpired.Value == -1 ? DateTime.MaxValue
                : DateTime.Now.AddSeconds(skillDescriptionHudExpired.Value);
        }

        private static void SetSkillCore()
        {
            setSkillTimer = null;
            lock (setLock)
            {
                if (Instance == null) return;

                // GameRules null = not ready; keep polling so skills land right after warmup ends.
                if (Instance.GameRules == null || Instance.GameRules.WarmupPeriod == true)
                {
                    setSkillTimer?.Kill();
                    setSkillTimer = Instance.AddTimer(1f, SetSkill, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                    return;
                }

                var validPlayers = Utilities.GetPlayers()
                    .Where(p => p != null && p.IsValid && !p.IsHLTV)
                    .Where(p =>
                    {
                        try { return p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist; }
                        catch { return false; }
                    }).ToList();

                if (Config.LoadedConfig.GameMode == (int)Config.GameModes.TeamSkills)
                {
                    List<jSkill_SkillInfo> tSkills = [.. SkillData.Skills];
                    tSkills.RemoveAll(s => s.Skill == tSkill.Skill || s.Skill == Skills.None || counterterroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                    tSkill = tSkills.Count == 0 ? noneSkill : tSkills[Instance.Random.Next(tSkills.Count)];

                    List<jSkill_SkillInfo> ctSkills = [.. SkillData.Skills];
                    ctSkills.RemoveAll(s => s.Skill == ctSkill.Skill || s.Skill == Skills.None || terroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                    ctSkill = ctSkills.Count == 0 ? noneSkill : ctSkills[Instance.Random.Next(ctSkills.Count)];
                }
                else if (Config.LoadedConfig.GameMode == (int)Config.GameModes.SameSkills)
                {
                    List<jSkill_SkillInfo> allSkills = [.. SkillData.Skills];
                    allSkills.RemoveAll(s => s.Skill == allSkill.Skill || s.Skill == Skills.None || !allTeamsSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                    allSkill = allSkills.Count == 0 ? noneSkill : allSkills[Instance.Random.Next(allSkills.Count)];
                }
                else if (Config.LoadedConfig.GameMode == (int)Config.GameModes.Debug && debugSkills.Count == 0)
                    debugSkills = [.. SkillData.Skills];

                Dictionary<Skills, int> assignmentCounts = new();
                foreach (var sp in Instance.SkillPlayer)
                {
                    if (sp == null) continue;
                    if (assignmentCounts.TryGetValue(sp.Skill, out var cnt)) assignmentCounts[sp.Skill] = cnt + 1;
                    else assignmentCounts[sp.Skill] = 1;
                }

                PickContext? pickContext = null;

                foreach (var player in validPlayers)
                {
                    if (player == null) continue;
                    var teammates = validPlayers.Where(p => p != null && p.IsValid && p.Team == player.Team && p != player).ToList();
                    string teammateSkills = "";

                    var skillPlayer = PlayerManager.GetPlayerByIndex(player!.Index);
                    if (skillPlayer == null) continue;

                    skillPlayer.IsDrawing = false;
                    skillPlayer.HudOnDeathBlocked = null;
                    if (player.PlayerPawn.Value == null || !player.PlayerPawn.IsValid)
                    {
                        skillPlayer.Skill = Skills.None;
                        continue;
                    }

                    jSkill_SkillInfo randomSkill = noneSkill;

                    Config.GameModes gameMode = (Config.GameModes)Config.LoadedConfig.GameMode;
                    if (gameMode == Config.GameModes.Normal || gameMode == Config.GameModes.FullRandom || gameMode == Config.GameModes.NoRepeat)
                    {
                        // Prefer the pick made at the end of the previous round; re-pick only when
                        // it no longer fits (team change, missing player, max reached).
                        if (nextRoundPicks.TryGetValue(player.Index, out var pre) && IsPickStillValid(pre, player, validPlayers, assignmentCounts))
                            randomSkill = pre;
                        else
                        {
                            pickContext ??= BuildPickContext(validPlayers);
                            randomSkill = PickSkillForPlayer(player, skillPlayer, pickContext, assignmentCounts, gameMode);
                        }
                    }
                    else if (gameMode == Config.GameModes.TeamSkills)
                        randomSkill = player.Team == CsTeam.Terrorist ? tSkill : ctSkill;
                    else if (gameMode == Config.GameModes.SameSkills)
                        randomSkill = allSkill;
                    else if (gameMode == Config.GameModes.Debug)
                    {
                        if (debugSkills.Count == 0)
                            debugSkills = [.. SkillData.Skills];
                        randomSkill = debugSkills[0];
                        debugSkills.RemoveAt(0);
                        player.PrintToChat($"{SkillData.Skills.Count - debugSkills.Count}/{SkillData.Skills.Count}");
                    }

                    Instance?.SkillAction(skillPlayer.Skill.ToString(), "DisableSkill", [player]);
                    skillPlayer.Skill = randomSkill.Skill;
                    skillPlayer.SpecialSkill = Skills.None;

                    if (randomSkill.Skill != Skills.None)
                    {
                        if (assignmentCounts.TryGetValue(randomSkill.Skill, out var cnt)) assignmentCounts[randomSkill.Skill] = cnt + 1;
                        else assignmentCounts[randomSkill.Skill] = 1;
                    }

                    if (randomSkill.Skill == Skills.Illiterate)
                        Illiterate.Enable();

                    var playerIndex = player.Index;
                    Instance?.AddTimer(.2f, () =>
                    {
                        var playerTarget = Utilities.GetPlayerFromIndex((int)playerIndex);
                        if (playerTarget == null || !playerTarget.IsValid) return;

                        if (randomSkill.Display)
                            SkillUtils.PrintToChat(playerTarget, $"{ChatColors.DarkRed}{playerTarget.GetSkillName(randomSkill.Skill)}{ChatColors.Lime}: {playerTarget.GetSkillDescription(randomSkill.Skill)}",
                                border: !Utilities.GetPlayers().Any(p => p != null && p.IsValid && p.Team == playerTarget.Team && p != playerTarget) ? "tb" : "t");

                        if (SkillsInfo.GetValue<bool>(randomSkill.Skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                            Instance?.AddTimer(Config.LoadedConfig.SkillTimeBeforeStart, () =>
                            {
                                var playerTarget = Utilities.GetPlayerFromIndex((int)playerIndex);
                                if (playerTarget == null || !playerTarget.IsValid) return;

                                if (PlayerManager.GetPlayerByIndex(playerTarget!.Index)?.Skill != randomSkill.Skill) return;
                                Debug.WriteToDebug("Enabling skill after freeze time: " + randomSkill.Skill);
                                Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [playerTarget]);
                            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                        else
                        {
                            if (PlayerManager.GetPlayerByIndex(playerTarget!.Index)?.Skill != randomSkill.Skill) return;
                            Debug.WriteToDebug("Enabling skill: " + randomSkill.Skill);
                            Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [playerTarget]);
                        }
                    }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

                    Debug.WriteToDebug($"Player {skillPlayer.PlayerName} has got the skill \"{player.GetSkillName(randomSkill.Skill)}\".");
                    UpdateSkillHudExpired(skillPlayer, randomSkill.Skill);

                    if (Config.LoadedConfig.TeamMateSkillChatInfo)
                    {
                        Instance?.AddTimer(.6f, () =>
                        {
                            if (player == null || !player.IsValid) return;

                            foreach (var teammate in teammates)
                            {
                                var teammateInfo = PlayerManager.GetPlayerByIndex(teammate.Index);
                                if (teammateInfo != null && teammateInfo?.Skill != null)
                                {
                                    var skillInfo = SkillData.Skills.FirstOrDefault(p => p.Skill == teammateInfo.Skill);
                                    teammateSkills += $" {ChatColors.DarkRed}\u202A{teammate.PlayerName}\u202C{ChatColors.Lime}: {(skillInfo == null ? player.GetSkillName(Skills.None) : player.GetSkillName(skillInfo.Skill, teammateInfo.SkillChance))}\n";
                                }
                            }

                            if (!string.IsNullOrEmpty(teammateSkills))
                            {
                                SkillUtils.PrintToChat(player, string.Empty, title: player.GetTranslation("teammate_skills"), border: "t");
                                foreach (string text in teammateSkills.Split("\n"))
                                    if (!string.IsNullOrEmpty(text))
                                        SkillUtils.PrintToChat(player, text, title: player.GetTranslation("teammate_skills"), border: "");
                                SkillUtils.PrintToChat(player, string.Empty, title: player.GetTranslation("teammate_skills"), border: "b");
                            }
                        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                    }
                }

                nextRoundPicks.Clear();
            }
        }

        public static void SetRandomSkill(CCSPlayerController player)
        {
            lock (setLock)
            {
                var validPlayers = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsHLTV && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist).ToList();

                if (Config.LoadedConfig.GameMode == (int)Config.GameModes.TeamSkills)
                {
                    List<jSkill_SkillInfo> tSkills = [.. SkillData.Skills];
                    tSkills.RemoveAll(s => s.Skill == tSkill.Skill || s.Skill == Skills.None || counterterroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                    tSkill = tSkills.Count == 0 ? noneSkill : tSkills[0];

                    List<jSkill_SkillInfo> ctSkills = [.. SkillData.Skills];
                    ctSkills.RemoveAll(s => s.Skill == ctSkill.Skill || s.Skill == Skills.None || terroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                    ctSkill = ctSkills.Count == 0 ? noneSkill : ctSkills[0];
                }

                if (player == null) return;
                var skillPlayer = PlayerManager.GetPlayerByIndex(player!.Index);
                if (skillPlayer == null) return;

                skillPlayer.IsDrawing = false;
                if (player.PlayerPawn.Value == null || !player.PlayerPawn.IsValid)
                {
                    skillPlayer.Skill = Skills.None;
                    return;
                }

                jSkill_SkillInfo randomSkill = noneSkill;
                if (Instance?.GameRules != null && Instance?.GameRules.WarmupPeriod == false)
                {
                    Config.GameModes gameMode = (Config.GameModes)Config.LoadedConfig.GameMode;
                    if (staticSkills.TryGetValue(player.Index, out var staticSkill))
                        randomSkill = staticSkill;
                    else if (gameMode == Config.GameModes.Normal || gameMode == Config.GameModes.FullRandom || gameMode == Config.GameModes.NoRepeat)
                    {
                        List<jSkill_SkillInfo> skillList = [.. SkillData.Skills];
                        skillList.RemoveAll(s => s?.Skill == Skills.None);
                        if (!player.IsBot)
                            skillList.RemoveAll(s => !string.IsNullOrEmpty(SkillsInfo.GetValue<string>(s.Skill, "requiredPermission")) && !AdminManager.PlayerHasPermissions(player, SkillsInfo.GetValue<string>(s.Skill, "requiredPermission")));

                        if (gameMode != Config.GameModes.FullRandom)
                            skillList.RemoveAll(s => s?.Skill == skillPlayer?.Skill || s?.Skill == skillPlayer?.SpecialSkill);

                        if (validPlayers.Count(p => p.Team == player.Team) == 1)
                        {
                            SkillsInfo.DefaultSkillInfo[] skillsNeedsTeammates = [.. SkillsInfo.LoadedConfig.Where(s => s.NeedsTeammates)];
                            skillList.RemoveAll(s => skillsNeedsTeammates.Any(s2 => s2.Name == s.Skill.ToString()));
                        }

                        if (player.Team == CsTeam.Terrorist)
                            skillList.RemoveAll(s => counterterroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));
                        else
                            skillList.RemoveAll(s => terroristSkills.Any(s2 => s2.Name == s.Skill.ToString()));

                        if (gameMode == Config.GameModes.NoRepeat && playersSkills.TryGetValue(player.Index, out ConcurrentBag<jSkill_SkillInfo>? skills))
                        {
                            skillList.RemoveAll(s => skills.Any(s2 => s2.Skill == s.Skill));
                            if (skillList.Count == 0) skills.Clear();
                        }

                        var assignmentCounts = new Dictionary<Skills, int>();
                        foreach (var sp in Instance.SkillPlayer)
                        {
                            if (sp == null) continue;
                            if (assignmentCounts.TryGetValue(sp.Skill, out var cnt)) assignmentCounts[sp.Skill] = cnt + 1;
                            else assignmentCounts[sp.Skill] = 1;
                        }

                        randomSkill = skillList.Count == 0 ? noneSkill : ChooseSkillByRarityAndMax(skillList, assignmentCounts, gameMode);
                    }
                    else if (gameMode == Config.GameModes.TeamSkills)
                        randomSkill = player.Team == CsTeam.Terrorist ? tSkill : ctSkill;
                    else if (gameMode == Config.GameModes.SameSkills)
                        randomSkill = allSkill;
                    else if (gameMode == Config.GameModes.Debug)
                    {
                        if (debugSkills.Count == 0)
                            debugSkills = [.. SkillData.Skills];
                        randomSkill = debugSkills[0];
                        debugSkills.RemoveAt(0);
                        player.PrintToChat($"{SkillData.Skills.Count - debugSkills.Count}/{SkillData.Skills.Count}");
                    }
                }

                Instance?.SkillAction(skillPlayer.Skill.ToString(), "DisableSkill", [player]);
                skillPlayer.Skill = randomSkill.Skill;
                skillPlayer.SpecialSkill = Skills.None;

                if (randomSkill.Display && Config.LoadedConfig.YourSkillChatInfo)
                    SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(randomSkill.Skill)}{ChatColors.Lime}: {player.GetSkillDescription(randomSkill.Skill)}",
                        border: !Utilities.GetPlayers().Any(p => p != null && p.IsValid && p.Team == player.Team && p != player) ? "tb" : "t");

                if (randomSkill.Skill == Skills.Illiterate)
                    Illiterate.Enable();

                Instance?.AddTimer(.2f, () =>
                {
                    if (SkillsInfo.GetValue<bool>(randomSkill.Skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                        Instance?.AddTimer(Config.LoadedConfig.SkillTimeBeforeStart, () =>
                        {
                            if (PlayerManager.GetPlayerByIndex(player!.Index)?.Skill != randomSkill.Skill) return;
                            Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [player]);
                        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                    else
                        Instance?.SkillAction(randomSkill.Skill.ToString(), "EnableSkill", [player]);
                }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

                Debug.WriteToDebug($"Player {skillPlayer.PlayerName} has got the skill \"{player.GetSkillName(randomSkill.Skill)}\".");
                UpdateSkillHudExpired(skillPlayer, randomSkill.Skill);
            }
        }

        public static DateTime GetFreezeTimeEnd() => freezeTimeEnd;
    }
}
