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
                SkillUtils.BeginFrame();
                try { HudTick(); }
                finally { SkillUtils.EndFrame(); }
            });

            Instance.RegisterListener<OnMapStart>(OnMapStart);
            Instance.RegisterListener<OnMapEnd>(OnMapEnd);
        }

        private static void HudTick()
        {
            UpdateGameRules();
            long panoramaStart = PanoramaHud.Enabled ? PerfLog.Start() : 0;
            PanoramaHud.OnTick();
            PerfLog.Sample("OnTick(panorama)", panoramaStart);
            if (!SkillUtils.IsHudFrame()) return;

            if (PerfLog.Enabled && SkillUtils.Tick % 1920 == 0 && !PlayerManager.IsServerIdle())
            {
                int server = Utilities.GetAllEntities().Count(e => e != null && e.IsValid);
                var (tracked, owners) = EntityManager.GetStatistics();
                PerfLog.Info($"ENTITIES server={server} tracked={tracked} owners={owners}{EntityManager.DescribeTracked()}{Event.PerfContext()}");
                PerfLog.Info(PerfLog.ProcessStats());
            }

            long perfStart = PerfLog.Start();
            // Shared per-tick controller snapshot: the skill OnTick loop already scans the
            // player list this frame, so reuse that native scan instead of running a second one.
            // No skill HUD during warmup or after the match ended.
            var gameRules = Instance?.GameRules;
            if (gameRules != null && gameRules.WarmupPeriod != true && gameRules.GamePhase < 5)
            {
                var now = DateTime.Now;
                int tick = SkillUtils.Tick;
                foreach (var player in PlayerManager.GetTickPlayers())
                {
                    if (player != null && player.IsValid)
                        UpdatePlayerHud(player, now, tick);
                }
            }
            PerfLog.Sample("OnTick(hud)", perfStart);
        }

        private static void OnMapStart(string mapName)
        {
            skills.NoRecoil.ForgetSpread();
            SafeTimer.ForgetMapTimers();
            PlayerManager.SetServerActive(true);
            Instance.GameRules = null;
            Event.OnMapChange();
            BotManager.Initialize();
        }

        private static void OnMapEnd()
        {
            skills.NoRecoil.RestoreSpread();
            skills.NoRecoil.ForgetSpread();
            PerfLog.Info("===== MAP END (clean map change) =====");
            PerfLog.MapHeap();
            Debug.WriteToDebug("===== MAP END (clean map change) =====");
            SafeTimer.ForgetMapTimers();
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

        private static void UpdatePlayerHud(CCSPlayerController player, DateTime now, int tick)
        {
            if (player == null || !player.IsValid || player.IsBot) return;

            var skillPlayer = PlayerManager.GetPlayer(PlayerManager.GetPlayerEvent(player) ?? player);
            if (skillPlayer == null || skillPlayer.HideHUD >= tick) return;

            if (skillPlayer.HudSuppressedUntil > now) return;

            if (player.PawnIsAlive && skillPlayer.SkillHudExpired < now && string.IsNullOrEmpty(skillPlayer.PrintHTML)) return;

            if (SkillUtils.HasMenu(player) && !PanoramaHud.OtherHtmlActive(player))
            {
                SkillUtils.SetMenuPaused(player, false);
                return;
            }

            string infoLine = string.Empty;
            string skillLine = string.Empty;
            string remainingLine = string.Empty;
            string? descriptionLine = null;

            bool showDescriptionHUD = skillPlayer.SkillDescriptionHudExpired >= now || Config.LoadedConfig.DisplayAlwaysDescription;
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
                        (infoLine, skillLine) = SkillLines(player, skillPlayer, skillInfo);

                        if (skillInfo.Skill != Skills.None)
                        {
                            remainingLine = string.IsNullOrEmpty(skillPlayer.PrintHTML)
                                ? (showDescriptionHUD ? player.GetSkillDescriptionHud(skillInfo.Skill, skillPlayer.SkillChance) : "")
                                : skillPlayer.PrintHTML;

                            isDescription = string.IsNullOrEmpty(skillPlayer.PrintHTML);
                            if (!isDescription && showDescriptionHUD)
                                descriptionLine = player.GetSkillDescriptionHud(skillInfo.Skill, skillPlayer.SkillChance);
                        }
                    }
                }
                else
                {
                    if (player.Team is CsTeam.Spectator or CsTeam.None && Config.LoadedConfig.DisableSpectateHUD)
                        return;

                    skillPlayer.HudOnDeathBlocked ??= AdminManager.PlayerHasPermissions(player, Config.LoadedConfig.DisableHUDOnDeathPermission);
                    if (skillPlayer.HudOnDeathBlocked == true) return;

                    uint targetRaw = player.Pawn.Value?.ObserverServices?.ObserverTarget.Raw ?? 0;
                    if (targetRaw == 0 || targetRaw == 0xFFFFFFFF) return;

                    var observedPlayer = PlayerManager.GetControllerByPawnIndex(targetRaw & 0x7FFF);
                    if (observedPlayer == null) return;

                    var observedEvent = PlayerManager.GetPlayerEvent(observedPlayer);
                    if (observedEvent == null || !observedEvent.IsValid) return;

                    var observedSkill = PlayerManager.GetPlayer(observedEvent);
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
                        skillLine = $"<font color='{primaryColor}'>{primaryName}</font>";
                    else
                    {
                        string specialName = player.GetSkillName(observedSpecialInfo.Skill);
                        skillLine = $"<font color='{observedSpecialInfo.Color}'>{specialName}({primaryName})</font>";
                    }

                    if (observedSkill.Skill != Skills.None && !string.IsNullOrEmpty(observedSkill.PrintHTML))
                    {
                        remainingLine = observedSkill.PrintHTML;
                        isDescription = false;
                        if (showDescriptionHUD)
                            descriptionLine = player.GetSkillDescriptionHud(observedSkill.Skill, observedSkill.SkillChance);
                    }
                    else if (showDescriptionHUD)
                        remainingLine = player.GetSkillDescriptionHud(observedSkill.Skill, observedSkill.SkillChance);
                }
            }

            if (string.IsNullOrEmpty(skillLine)) return;

            Event.UpdateSkillHUD(player, skillPlayer, infoLine, skillLine, remainingLine, isDescription, descriptionLine);
        }

        private static (string Info, string Line) SkillLines(CCSPlayerController player, jSkill_PlayerInfo skillPlayer, jSkill_SkillInfo skillInfo)
        {
            if (skills.Illiterate.CheckIlliterateSkill(player))
                return (player.GetTranslationWithoutIlliterate("your_skill"), $"<font color='{skillInfo.Color}'>{player.GetSkillName(skillInfo.Skill, skillPlayer.SkillChance)}</font>");

            string lang = Localization.LangCodeOf(player);
            var cache = skillPlayer.HudLines ??= new HudLineCache();
            if (cache.Line == null
                || cache.Skill != skillInfo.Skill
                || cache.Chance != skillPlayer.SkillChance
                || !string.Equals(cache.Lang, lang, StringComparison.Ordinal)
                || !ReferenceEquals(cache.Color, skillInfo.Color))
            {
                cache.Skill = skillInfo.Skill;
                cache.Chance = skillPlayer.SkillChance;
                cache.Lang = lang;
                cache.Color = skillInfo.Color;
                cache.Info = player.GetTranslationWithoutIlliterate("your_skill");
                cache.Line = $"<font color='{skillInfo.Color}'>{player.GetSkillName(skillInfo.Skill, skillPlayer.SkillChance)}</font>";
            }
            return (cache.Info!, cache.Line);
        }
    }
}
