using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class AreaReaper : ISkill
    {
        private const Skills skillName = Skills.AreaReaper;
        private static readonly string[] bombsiteA = ["A", "a"];
        private static readonly string[] bombsiteB = ["B", "b"];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            if (playerInfo.SkillUsed)
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("areareaper_used_info")}");
                return;
            }

            int site = bombsiteA.Contains(commands[0]) ? 0 : bombsiteB.Contains(commands[0]) ? 1 : -1;
            if (site == -1) {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("areareaper_incorrect_site")}");
                return;
            }
            
            var bombTargets = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target").ToArray();
            if (bombTargets.Length == 2)
            {
                bombTargets[site].BombPlantedHere = true;
                Utilities.SetStateChanged(bombTargets[site], "CBombTarget", "m_bBombPlantedHere");

                playerInfo.SkillUsed = true;
                player.PrintToChat($" {ChatColors.Green}{player.GetTranslation("areareaper_site_disabled", (site == 0 ? 'A' : 'B'))}");
            }
            else
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("areareaper_no_site")}");
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;
            SkillUtils.CreateMenu(player, [(player.GetTranslation("bombsite_a"), "a")], (player.GetTranslation("bombsite_b"), "b", true));
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.CloseMenu(player);
            Server.NextWorldUpdate(() =>
            {
                if (Instance.SkillPlayer.FirstOrDefault(p => p.Skill == skillName) != null) return;
                EnableBombsite();
            });
        }

        private static void EnableBombsite()
        {
            var bombTargets = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target");
            foreach (var bombTarget in bombTargets)
            {
                bombTarget.BombPlantedHere = false;
                Utilities.SetStateChanged(bombTarget, "CBombTarget", "m_bBombPlantedHere");
            }
        }

        public static void OnTick()
        {
            var bombTargets = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target");
            foreach (var player in Utilities.GetPlayers().Where(p => p.Team == CsTeam.Terrorist))
            {
                if (!Instance.IsPlayerValid(player)) continue;

                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == null) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;

                if (pawn.WeaponServices == null) continue;
                var activeWeapon = pawn.WeaponServices.ActiveWeapon.Value;
                if (activeWeapon == null || !activeWeapon.IsValid || activeWeapon.DesignerName != "weapon_c4") continue;

                if (!pawn.InBombZone && pawn.InBombZoneTrigger)
                    player.PrintToCenterAlert(player.GetTranslation("areareaper_bombsite_disabled"));
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#edf5b5", CsTeam onlyTeam = CsTeam.CounterTerrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}