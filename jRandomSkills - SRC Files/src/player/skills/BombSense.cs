using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class BombSense : ISkill
    {
        private const Skills skillName = Skills.BombSense;
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.ResetPrintHTML(player);
        }

        public static void OnTick()
        {
            if (!SkillUtils.IsHudFrame()) return;

            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            if (holderBuffer.Count == 0) return;

            var bombOrigin = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault(b => b != null && b.IsValid)?.AbsOrigin;

            float closest = float.MaxValue;
            bool defusing = false;
            if (bombOrigin != null)
                foreach (var ct in PlayerManager.GetTickPlayers())
                {
                    if (ct == null || !ct.IsValid || ct.Team != CsTeam.CounterTerrorist) continue;

                    var pawn = ct.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.Health <= 0) continue;

                    defusing |= pawn.IsDefusing;
                    closest = Math.Min(closest, SkillUtils.Distance(pawn.AbsOrigin, bombOrigin));
                }

            foreach (var player in holderBuffer)
            {
                if (!Instance.IsPlayerValid(player)) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo == null) continue;

                if (bombOrigin == null || closest == float.MaxValue)
                    playerInfo.PrintHTML = null;
                else if (defusing)
                    playerInfo.PrintHTML = $"<font color='#FF0000'>{player.GetTranslation("bombsense_defusing")}</font>";
                else
                {
                    // Hammer units are inches.
                    int meters = (int)Math.Round(closest * 0.0254f);
                    string color = meters <= 10 ? "#FF0000" : meters <= 25 ? "#FFA500" : "#00FF00";
                    playerInfo.PrintHTML = player.GetTranslation("bombsense_hud", $"<font color='{color}'>{meters} m</font>");
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e8a13a", CsTeam onlyTeam = CsTeam.Terrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
