using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class NoRecoil : ISkill
    {
        private const Skills skillName = Skills.NoRecoil;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            var players = Utilities.GetPlayers();
            foreach (var player in players)
                DisableSkill(player);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            Server.ExecuteCommand("weapon_accuracy_nospread 1");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            Server.ExecuteCommand("weapon_accuracy_nospread 0");
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!Instance.IsPlayerValid(player)) continue;
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);

                if (playerInfo?.Skill == skillName)
                {
                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) continue;

                    if (pawn.AimPunchServices != null)
                    {
                        pawn.AimPunchServices.PredictableBaseTick = 0;
                        pawn.AimPunchServices.PredictableBaseTickInterpAmount = 0;
                        pawn.AimPunchServices.UnpredictableBaseTick = 0;
                    }

                    if (pawn.CameraServices != null)
                    {
                        pawn.CameraServices.CsViewPunchAngleTick = 0;
                        pawn.CameraServices.CsViewPunchAngleTickRatio = 0f;
                    }
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8a42f5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}