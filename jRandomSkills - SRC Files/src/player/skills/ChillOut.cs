using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class ChillOut : ISkill
    {
        private const Skills skillName = Skills.ChillOut;
        private static readonly ConcurrentDictionary<ulong, float> plantingPlayers = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            plantingPlayers.Clear();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            plantingPlayers.TryRemove(player.SteamID, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void BombAbortplant(EventBombAbortplant @event)
        {
            var user = @event.Userid;
            if (user == null || !user.IsValid || !user.PawnIsAlive) return;
            plantingPlayers.TryRemove(user.SteamID, out _);
            SkillUtils.ResetPrintHTML(user);
        }

        public static void BombBeginplant(EventBombBeginplant @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            plantingPlayers.TryAdd(player!.SteamID, Server.CurrentTime);

            var anyChillOut = Instance.SkillPlayer.FirstOrDefault(p => p.Skill == skillName);
            if (anyChillOut != null)
            {
                var bombEntities = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").ToList();
                if (bombEntities.Count != 0)
                {
                    var bomb = bombEntities.FirstOrDefault();
                    if (bomb != null)
                        bomb.ArmedTime = Server.CurrentTime + SkillsInfo.GetValue<float>(skillName, "bombArmedTime");
                }
            }
        }

        public static void BombPlanted(EventBombPlanted @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            plantingPlayers.TryRemove(player!.SteamID, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void OnTick()
        {
            float currentTime = Server.CurrentTime;
            float extraTime = SkillsInfo.GetValue<float>(skillName, "bombArmedTime");

            foreach (var player in Utilities.GetPlayers().Where(p => p.Team == CsTeam.Terrorist))
            {
                if (!Instance.IsPlayerValid(player)) continue;

                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo == null) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;

                if (pawn.WeaponServices == null) continue;
                var activeWeapon = pawn.WeaponServices.ActiveWeapon.Value;
                if (activeWeapon == null || !activeWeapon.IsValid || activeWeapon.DesignerName != "weapon_c4") continue;

                if (plantingPlayers.TryGetValue(player.SteamID, out float plantTime))
                {
                    float remaining = plantTime + extraTime - currentTime;
                    playerInfo.PrintHTML = $"{player.GetTranslation("planter_planting", $"<font color='#00FF00'>{Math.Max(0, remaining):0.0}s</font>")}";
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#343deb", CsTeam onlyTeam = CsTeam.CounterTerrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float bombArmedTime = 10f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float BombArmedTime { get; set; } = bombArmedTime;
        }
    }
}