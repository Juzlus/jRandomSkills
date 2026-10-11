using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static CounterStrikeSharp.API.Core.Listeners;
using static src.jRandomSkills;

namespace src.player
{
    public static class PlayerOnTick
    {
        public static void Load()
        {
            Instance.RegisterListener<OnTick>(() =>
            {
                UpdateGameRules();
                if (!SkillUtils.IsHudFrame()) return;

                if (PerfLog.Enabled && Server.TickCount % 1920 == 0 && !PlayerManager.IsServerIdle())
                {
                    int server = Utilities.GetAllEntities().Count(e => e != null && e.IsValid);
                    var (tracked, owners) = EntityManager.GetStatistics();
                    PerfLog.Info($"ENTITIES server={server} tracked={tracked} owners={owners}{EntityManager.DescribeTracked()}{Event.PerfContext()}");
                }

                long perfStart = PerfLog.Start();
                // Shared per-tick controller snapshot: the skill OnTick loop already scans the
                // player list this frame, so reuse that native scan instead of running a second one.
                var now = DateTime.Now;
                foreach (var player in PlayerManager.GetTickPlayers())
                {
                    if (player != null && player.IsValid)
                        UpdatePlayerHud(player, now);
                }
                PerfLog.Sample("OnTick(hud)", perfStart);
            });

            Instance.RegisterListener<OnMapStart>(OnMapStart);
            Instance.RegisterListener<OnMapEnd>(OnMapEnd);
        }

        private static void OnMapStart(string mapName)
        {
            skills.NoRecoil.ForgetSpread();
            PlayerManager.SetServerActive(true);
            Instance.GameRules = null;
            Event.OnMapChange();
            BotManager.Initialize();

            // After the retakes config has run (it executes 1 s after map start) and the game rules exist.
            Instance.AddTimer(3f, () => SkillUtils.EndWarmupIfSkipped("map start"), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        private static void OnMapEnd()
        {
            skills.NoRecoil.RestoreSpread();
            skills.NoRecoil.ForgetSpread();
            ScreenText.Clear();
            PerfLog.Info("===== MAP END (clean map change) =====");
            Debug.WriteToDebug("===== MAP END (clean map change) =====");
            BotManager.Stop();
            PlayerManager.SetServerActive(false);
        }

        public static void InitializeGameRules()
        {
            if (Instance.GameRules != null && Instance.GameRules.Handle != IntPtr.Zero) return;
            var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();

            if (gameRulesProxy != null)
                Instance.GameRules = gameRulesProxy.GameRules;
        }

        private static void UpdateGameRules()
        {
            if (Instance?.GameRules == null || Instance.GameRules.Handle == IntPtr.Zero)
                InitializeGameRules();
            else if (Instance != null && Config.LoadedConfig.EnableFlashingHtmlHudFix && !Instance.GameRules.WarmupPeriod)
                Instance.GameRules.GameRestart = Instance.GameRules.RestartRoundTime < Server.CurrentTime;
        }

        private static void UpdatePlayerHud(CCSPlayerController player, DateTime now)
        {
            if (player == null || !player.IsValid || player.IsBot) return;

            // No skill HUD during warmup or after the match ended.
            var gameRules = Instance?.GameRules;
            if (gameRules == null || gameRules.WarmupPeriod == true || gameRules.GamePhase >= 5) return;

            var skillPlayer = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index);
            if (skillPlayer == null || skillPlayer.HideHUD >= Server.TickCount) return;

            if (skillPlayer.HudSuppressedUntil > now) return;

            // A pending centre notice (site call, plant prompt) is drawn right away, even before the skill is drawn.
            bool noticePending = skillPlayer.CenterNoticeUntil > now && !string.IsNullOrEmpty(skillPlayer.CenterNotice);
            if (player.PawnIsAlive && skillPlayer.SkillHudExpired < now && string.IsNullOrEmpty(skillPlayer.PrintHTML) && !noticePending) return;

            if (SkillUtils.HasMenu(player))
            {
                SkillUtils.SetMenuPaused(player, false);
                return;
            }

            string infoLine = string.Empty;
            string skillLine = string.Empty;
            string remainingLine = string.Empty;
            string? statusLine = null;

            // The description (what the skill does and which key uses it) stays on screen for the whole round.
            // SkillDescriptionDuration / DisplayAlwaysDescription used to hide it after a few seconds; players kept
            // missing how to use their skill, so the timer is no longer applied.
            bool showDescriptionHUD = true;
            bool isDescription = true;

            var skills = SkillData.GetSnapshot();

            if (skills.Length == 0)
            {
                infoLine = player.GetTranslationWithoutIlliterate("your_skill");
                skillLine = player.GetTranslationWithoutIlliterate("none");
            }
            else if (skillPlayer.IsDrawing && player.PawnIsAlive)
            {
                var randomSkill = skills[Random.Shared.Next(skills.Length)];

                infoLine = player.GetTranslationWithoutIlliterate("drawing_skill");
                skillLine = $"<font color='{randomSkill.Color}'>{player.GetSkillName(randomSkill.Skill)}</font>";
            }
            else
            {
                if (player.PawnIsAlive)
                {
                    var skillInfo = SkillData.GetInfo(skillPlayer.Skill);

                    if (skillInfo != null)
                    {
                        infoLine = player.GetTranslationWithoutIlliterate("your_skill");
                        skillLine = ComboManager.HudSkillLine(player, skillPlayer);
                        if (string.IsNullOrEmpty(skillLine))
                            skillLine = $"<font color='{skillInfo.Color}'>{player.GetSkillName(skillInfo.Skill, skillPlayer.SkillChance)}</font>";

                        if (skillInfo.Skill != Skills.None)
                        {
                            // Description and cooldown/status are separate lines: using a skill never hides its description.
                            remainingLine = showDescriptionHUD ? ComboManager.HudDescription(player, skillPlayer) : "";
                            statusLine = skillPlayer.PrintHTML;
                            isDescription = true;
                        }
                    }
                }
                else
                {
                    if (player.Team is CsTeam.Spectator or CsTeam.None && Config.LoadedConfig.DisableSpectateHUD)
                        return;

                    skillPlayer.HudOnDeathBlocked ??= AdminManager.PlayerHasPermissions(player, Config.LoadedConfig.DisableHUDOnDeathPermission);
                    if (skillPlayer.HudOnDeathBlocked == true) return;

                    var pawn = player.Pawn.Value;
                    if (pawn?.ObserverServices == null) return;

                    var observerTarget = pawn.ObserverServices.ObserverTarget?.Value;
                    if (observerTarget == null || !observerTarget.IsValid) return;

                    var observedPlayer = PlayerManager.GetControllerByPawn(observerTarget.Handle);
                    if (observedPlayer == null) return;

                    var observedEvent = PlayerManager.GetPlayerEvent(observedPlayer);
                    if (observedEvent == null || !observedEvent.IsValid) return;

                    var observedSkill = PlayerManager.GetPlayerByIndex(observedEvent.Index);
                    if (observedSkill == null) return;

                    var observedSkillInfo = SkillData.GetInfo(observedSkill.Skill);
                    var observedSpecialInfo = observedSkill.SpecialSkill != Skills.None
                        ? SkillData.GetInfo(observedSkill.SpecialSkill)
                        : null;

                    string primaryName = player.GetSkillName(observedSkill.Skill, observedSkill.SkillChance);
                    string primaryColor = observedSkillInfo?.Color ?? SkillsInfo.GetValue<string>(Skills.None, "color");
                    string pName = System.Net.WebUtility.HtmlEncode(observedSkill.PlayerName);

                    if (pName.Length > 18)
                        pName = $"{pName[..17]}...";

                    var observerSkill = player.GetTranslationWithoutIlliterate("observer_skill");
                    infoLine = string.IsNullOrEmpty(observerSkill) ? pName : $"{observerSkill} {pName}";

                    if (observedSkill.SpecialSkill == Skills.None || observedSpecialInfo == null)
                    {
                        skillLine = ComboManager.HudSkillLine(player, observedSkill);
                        if (string.IsNullOrEmpty(skillLine))
                            skillLine = $"<font color='{primaryColor}'>{primaryName}</font>";
                    }
                    else
                    {
                        string specialName = player.GetSkillName(observedSpecialInfo.Skill);
                        skillLine = $"<font color='{observedSpecialInfo.Color}'>{specialName}({primaryName})</font>";
                    }

                    if (showDescriptionHUD)
                        remainingLine = ComboManager.HudDescription(player, observedSkill);
                    if (observedSkill.Skill != Skills.None && !string.IsNullOrEmpty(observedSkill.PrintHTML))
                        statusLine = observedSkill.PrintHTML;
                }
            }

            if (string.IsNullOrEmpty(skillLine)) return;

            Event.UpdateSkillHUD(player, skillPlayer, infoLine, skillLine, remainingLine, isDescription, statusLine);
        }
    }
}
