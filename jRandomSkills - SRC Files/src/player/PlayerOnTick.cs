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
                foreach (var player in Utilities.GetPlayers())
                    if (player != null && player.IsValid)
                        UpdatePlayerHud(player);
            });

            Instance.RegisterListener<OnMapStart>(OnMapStart);
        }

        private static void OnMapStart(string mapName)
        {
            Instance.GameRules = null;
            Event.OnMapChange();
        }

        private static void InitializeGameRules()
        {
            if (Instance.GameRules != null) return;
            var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
            if (gameRulesProxy != null)
                Instance.GameRules = gameRulesProxy?.GameRules;
        }

        private static void UpdateGameRules()
        {
            if (Instance?.GameRules == null || Instance?.GameRules?.Handle == IntPtr.Zero)
                InitializeGameRules();
            else if (Instance != null && Config.LoadedConfig.FlashingHtmlHudFix && !Instance.GameRules.WarmupPeriod)
                Instance.GameRules.GameRestart = Instance.GameRules?.RestartRoundTime < Server.CurrentTime;
        }

        private static void UpdatePlayerHud(CCSPlayerController player)
        {
            if (player == null) return;
            var skillPlayer = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (skillPlayer == null || !skillPlayer.DisplayHUD || (player.PawnIsAlive && skillPlayer.SkillHudExpired < DateTime.Now)) return;

            string infoLine = "";
            string skillLine = "";
            string remainingLine = "";
            bool showDescirptionHUD = skillPlayer.SkillDescriptionHudExpired >= DateTime.Now;
            bool isDescription = true;

            if (SkillData.Skills.IsEmpty)
            {
                infoLine = player.GetTranslation("your_skill");
                skillLine = player.GetTranslation("none");
            }
            else if (skillPlayer.IsDrawing)
            {
                var randomSkill = SkillData.Skills.ToArray()[Instance.Random.Next(SkillData.Skills.Count)];
                infoLine = player.GetTranslation("drawing_skill");
                skillLine = $"<font color='{randomSkill.Color}'>{player.GetSkillName(randomSkill.Skill)}</font>";
            }
            else if (!skillPlayer.IsDrawing)
            {
                if (player?.IsValid == true && player?.PawnIsAlive == true)
                {
                    var skillInfo = SkillData.Skills.FirstOrDefault(s => s.Skill == skillPlayer.Skill);
                    if (skillInfo != null)
                    {
                        infoLine = player.GetTranslation("your_skill");
                        skillLine = $"<font color='{skillInfo.Color}'>{player.GetSkillName(skillInfo.Skill, skillPlayer.SkillChance)}</font>";
                        if (skillInfo.Skill != Skills.None)
                        {
                            remainingLine = string.IsNullOrEmpty(skillPlayer.PrintHTML)
                                ? showDescirptionHUD ? player.GetSkillDescription(skillInfo.Skill, skillPlayer.SkillChance) : ""
                                : skillPlayer.PrintHTML;
                            isDescription = string.IsNullOrEmpty(skillPlayer.PrintHTML);
                        }
                    }
                } else if (player?.IsValid == true)
                {
                    if ((player.Team is CsTeam.Spectator or CsTeam.None && Config.LoadedConfig.DisableSpectateHUD) || AdminManager.PlayerHasPermissions(player, Config.LoadedConfig.DisableHUDOnDeathPermission))
                        return;

                    var pawn = player.Pawn.Value;
                    if (pawn == null) return;

                    var observedPlayer = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == pawn?.ObserverServices?.ObserverTarget?.Value?.Handle);
                    if (observedPlayer == null) return;

                    var observeredPlayerSkill = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == observedPlayer.SteamID);
                    if (observeredPlayerSkill == null) return;

                    var observeredPlayerSkillInfo = SkillData.Skills.FirstOrDefault(s => s.Skill == observeredPlayerSkill.Skill);
                    if (observeredPlayerSkillInfo == null) return;

                    var observeredPlayerSpecialSkillInfo = SkillData.Skills.FirstOrDefault(s => s.Skill == observeredPlayerSkill.SpecialSkill);
                    if (observeredPlayerSpecialSkillInfo == null) return;

                    string pName = observeredPlayerSkill.PlayerName;
                    if (pName.Length > 18)
                        pName = $"{pName[..17]}...";
                    var observerSkill = player.GetTranslation("observer_skill");
                    infoLine = string.IsNullOrEmpty(observerSkill) ? pName : $"{observerSkill} {pName}";
                    skillLine = $"<font color='{observeredPlayerSkillInfo.Color}'>{(observeredPlayerSkill.SpecialSkill == Skills.None 
                        ? player.GetSkillName(observeredPlayerSkillInfo.Skill, observeredPlayerSkill.SkillChance) 
                        : $"{player.GetSkillName(observeredPlayerSpecialSkillInfo.Skill)}({player.GetSkillName(observeredPlayerSkillInfo.Skill)})")}</font>";
                    if (showDescirptionHUD)
                        remainingLine = player.GetSkillDescription(observeredPlayerSkill.Skill, observeredPlayerSkill.SkillChance);
                }
            }

            if (string.IsNullOrEmpty(skillLine)) return;
            if (player == null || !player.IsValid) return;
            Event.UpdateSkillHUD(player, infoLine, skillLine, remainingLine, isDescription);
        }
    }
}