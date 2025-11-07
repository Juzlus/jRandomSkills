using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Distancer : ISkill
    {
        private const Skills skillName = Skills.Distancer;
        private static readonly ConcurrentDictionary<CCSPlayerController, byte> distancerPlayers = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
                distancerPlayers.Clear();
        }

        public static void OnTick()
        {
            if (SkillUtils.IsFreezeTime()) return;
            foreach (var player in distancerPlayers.Keys)
            {
                var playerInfo = jRandomSkills.Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
                if (playerInfo == null) continue;

                var playerPawn = player.PlayerPawn.Value;
                if (playerPawn == null || !playerPawn.IsValid) continue;
                if (playerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                string closetEnemy = "Bot";
                double closetDistance = double.MaxValue;

                foreach (var enemy in Utilities.GetPlayers().Where(p => p.Team != player.Team))
                {
                    var enemyPawn = enemy.PlayerPawn.Value;
                    if (enemyPawn == null || !enemyPawn.IsValid) continue;
                    if (enemyPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || playerPawn.AbsOrigin == null || enemyPawn.AbsOrigin == null) continue;
                    double distance = (int)SkillUtils.GetDistance(playerPawn.AbsOrigin, enemyPawn.AbsOrigin);
                    if (distance >= closetDistance) continue;
                    closetDistance = distance;
                    closetEnemy = enemy.PlayerName;
                }

                string distanceColor = closetDistance > 1500 ? "#00FF00" : closetDistance > 600 ? "#FFFF00" : "#FF0000";
                playerInfo.PrintHTML = $"{closetEnemy}: <font color='{distanceColor}'>{(closetDistance > 3000 ? "3000+" : closetDistance)}</font>";
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            distancerPlayers.TryAdd(player, 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            distancerPlayers.TryRemove(player, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#00f2ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}