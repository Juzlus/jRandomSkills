using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class AreaReaper : ISkill
    {
        private const Skills skillName = Skills.AreaReaper;
        private static readonly string[] bombsiteA = ["A", "a"];
        private static readonly string[] bombsiteB = ["B", "b"];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player != null && player.IsValid)
                    SkillUtils.CloseMenu(player);
            }

            playersToTarget.Clear();
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || commands == null || commands.Length == 0) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (playerInfo.SkillUsed)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("areareaper_used_info")}");
                return;
            }

            int site = bombsiteA.Contains(commands[0]) ? 0 : bombsiteB.Contains(commands[0]) ? 1 : -1;
            if (site == -1)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("areareaper_incorrect_site")}");
                return;
            }

            var bombTargets = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target").ToArray();
            if (bombTargets.Length == 2)
            {
                var targetSite = bombTargets[site];
                if (targetSite != null && targetSite.IsValid)
                {
                    targetSite.BombPlantedHere = true;
                    Utilities.SetStateChanged(targetSite, "CBombTarget", "m_bBombPlantedHere");
                    playerInfo.SkillUsed = true;

                    playersToTarget[player.Index] = targetSite.Index;

                    string siteLetter = site == 0 ? "A" : "B";
                    foreach (var ct in Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsHLTV && p.Team == CsTeam.CounterTerrorist))
                        ct.PrintToChat($" {ChatColors.Green}" + ct.GetTranslation("areareaper_teammates_info", siteLetter));
                }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            playerInfo.SkillUsed = false;
            SkillUtils.CreateMenu(player, [(playerEvent.GetTranslation("bombsite_a"), "a")], (playerEvent.GetTranslation("bombsite_b"), "b", true));
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            Server.NextWorldUpdate(() =>
            {
                if (PlayerManager.GetPlayerCountBySkill(skillName) != 0) return;
                EnableBombsite();
            });

            SkillUtils.CloseMenu(player);
        }

        private static void EnableBombsite(uint? bombsiteIndex = null)
        {
            var bombTargets = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target");
            foreach (var bombTarget in bombTargets)
                if (bombTarget != null && bombTarget.IsValid)
                {
                    bombTarget.BombPlantedHere = false;
                    Utilities.SetStateChanged(bombTarget, "CBombTarget", "m_bBombPlantedHere");
                }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 16 != 0) return;

            foreach (var player in Utilities.GetPlayers())
            {
                var playerEvent = PlayerManager.GetPlayerFromEvent(player);
                if (playerEvent == null || !playerEvent.IsValid || player == null || !player.IsValid || player.Team != CsTeam.Terrorist || player.PlayerPawn?.Value?.Health <= 0) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill == null) continue;

                var pawn = player.PlayerPawn!.Value;
                if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) continue;

                var activeWeapon = pawn.WeaponServices.ActiveWeapon.Value;
                if (activeWeapon == null || !activeWeapon.IsValid || activeWeapon.DesignerName != "weapon_c4") continue;
  
                if (!pawn.InBombZone && pawn.InBombZoneTrigger)
                    playerEvent.PrintToCenterAlert(playerEvent.GetTranslation("areareaper_bombsite_disabled"));
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#edf5b5", CsTeam onlyTeam = CsTeam.CounterTerrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = 1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}