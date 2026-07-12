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
                if (Server.TickCount % 2 != 0) return;

                if (PerfLog.Enabled && Server.TickCount % 1920 == 0)
                {
                    int server = Utilities.GetAllEntities().Count(e => e != null && e.IsValid);
                    var (tracked, owners) = EntityManager.GetStatistics();
                    PerfLog.Info($"ENTITIES server={server} tracked={tracked} owners={owners}");
                }

                long perfStart = PerfLog.Start();
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player != null && player.IsValid)
                        UpdatePlayerHud(player);
                }
                PerfLog.Sample("OnTick(hud)", perfStart);
            });

            Instance.RegisterListener<OnMapStart>(OnMapStart);
            Instance.RegisterListener<OnMapEnd>(OnMapEnd);
        }

        private static void OnMapStart(string mapName)
        {
            Instance.GameRules = null;
            Event.OnMapChange();
            BotManager.Initialize();
        }

        private static void OnMapEnd()
        {
            PerfLog.Info("===== MAP END (clean map change) =====");
            Debug.WriteToDebug("===== MAP END (clean map change) =====");
            BotManager.Stop();
        }

        private static void InitializeGameRules()
        {
            if (Instance.GameRules != null) return;
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

        private static void UpdatePlayerHud(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.IsBot) return;

            // No skill HUD during warmup or after the match ended.
            var gameRules = Instance?.GameRules;
            if (gameRules == null || gameRules.WarmupPeriod == true || gameRules.GamePhase >= 5) return;

            var skillPlayer = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index);
            if (skillPlayer == null || !skillPlayer.DisplayHUD) return;

            var now = DateTime.Now;

            if (player.PawnIsAlive && skillPlayer.SkillHudExpired < now) return;

            string infoLine = string.Empty;
            string skillLine = string.Empty;
            string remainingLine = string.Empty;

            bool showDescriptionHUD = skillPlayer.SkillDescriptionHudExpired >= now || Config.LoadedConfig.DisplayAlwaysDescription;
            bool isDescription = true;

            var skills = SkillData.Skills;

            if (skills == null || skills.IsEmpty)
            {
                infoLine = player.GetTranslation("your_skill");
                skillLine = player.GetTranslation("none");
            }
            else if (skillPlayer.IsDrawing && player.PawnIsAlive)
            {
                int skillCount = skills.Count;

                if (skillCount > 0)
                {
                    var skillsArray = skills.ToArray();
                    var randomSkill = skillsArray[Instance.Random.Next(skillCount)];

                    infoLine = player.GetTranslation("drawing_skill");
                    skillLine = $"<font color='{randomSkill.Color}'>{player.GetSkillName(randomSkill.Skill)}</font>";
                }
            }
            else
            {
                if (player.PawnIsAlive)
                {
                    var skillInfo = SkillData.GetInfo(skillPlayer.Skill);

                    if (skillInfo != null)
                    {
                        infoLine = player.GetTranslation("your_skill");
                        skillLine = $"<font color='{skillInfo.Color}'>{player.GetSkillName(skillInfo.Skill, skillPlayer.SkillChance)}</font>";

                        if (skillInfo.Skill != Skills.None)
                        {
                            remainingLine = string.IsNullOrEmpty(skillPlayer.PrintHTML)
                                ? (showDescriptionHUD ? player.GetSkillDescription(skillInfo.Skill, skillPlayer.SkillChance) : "")
                                : skillPlayer.PrintHTML;

                            isDescription = string.IsNullOrEmpty(skillPlayer.PrintHTML);
                        }
                    }
                }
                else
                {
                    if ((player.Team is CsTeam.Spectator or CsTeam.None && Config.LoadedConfig.DisableSpectateHUD)
                        || AdminManager.PlayerHasPermissions(player, Config.LoadedConfig.DisableHUDOnDeathPermission))
                        return;

                    var pawn = player.Pawn.Value;
                    if (pawn?.ObserverServices == null) return;

                    var observerTarget = pawn.ObserverServices.ObserverTarget?.Value;
                    if (observerTarget == null || !observerTarget.IsValid) return;

                    var observedPlayer = Utilities.GetPlayers().FirstOrDefault(p =>
                        p != null && p.IsValid && p.Pawn?.Value?.Handle == observerTarget.Handle);

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

                    var observerSkill = player.GetTranslation("observer_skill");
                    infoLine = string.IsNullOrEmpty(observerSkill) ? pName : $"{observerSkill} {pName}";

                    if (observedSkill.SpecialSkill == Skills.None || observedSpecialInfo == null)
                        skillLine = $"<font color='{primaryColor}'>{primaryName}</font>";
                    else
                    {
                        string specialName = player.GetSkillName(observedSpecialInfo.Skill);
                        skillLine = $"<font color='{observedSpecialInfo.Color}'>{specialName}({primaryName})</font>";
                    }

                    if (showDescriptionHUD)
                        remainingLine = player.GetSkillDescription(observedSkill.Skill, observedSkill.SkillChance);
                }
            }

            if (string.IsNullOrEmpty(skillLine)) return;
            if (SkillUtils.HasMenu(player)) return;

            Event.UpdateSkillHUD(player, infoLine, skillLine, remainingLine, isDescription);
        }
    }
}
