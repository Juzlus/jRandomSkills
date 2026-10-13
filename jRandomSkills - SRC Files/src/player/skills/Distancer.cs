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
        private static readonly ConcurrentDictionary<uint, byte> distancerPlayers = [];
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
            if (distancerPlayers.IsEmpty) return;

            if (!SkillUtils.IsHudFrame()) return;

            if (SkillUtils.IsFreezeTime()) return;
            var alive = PlayerManager.GetAliveSnapshot();
            foreach (var playerIndex in distancerPlayers.Keys)
            {
                var player = PlayerManager.GetController(playerIndex);
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayer(player);
                if (playerInfo == null) continue;

                var playerPawn = player.PlayerPawn.Value;
                if (playerPawn == null || !playerPawn.IsValid) continue;
                if (playerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                var origin = playerPawn.AbsOrigin;
                if (origin == null) continue;

                var team = player.Team;
                double closetDistance = double.MaxValue;
                int closest = -1;

                for (int i = 0; i < alive.Count; i++)
                {
                    var enemy = alive[i];
                    if (enemy.Team == team) continue;

                    float dx = enemy.X - origin.X, dy = enemy.Y - origin.Y, dz = enemy.Z - origin.Z;
                    double distance = (int)Math.Sqrt((double)dx * dx + (double)dy * dy + (double)dz * dz);
                    if (distance >= closetDistance) continue;
                    closetDistance = distance;
                    closest = i;
                }

                string closetEnemy = closest >= 0 ? alive[closest].Controller.PlayerName : "Bot";

                string distanceColor = closetDistance > 1500 ? "#00FF00" : closetDistance > 600 ? "#FFFF00" : "#FF0000";
                playerInfo.PrintHTML = $"{System.Net.WebUtility.HtmlEncode(closetEnemy)}: <font color='{distanceColor}'>{(closetDistance > 3000 ? "3000+" : closetDistance)}</font>";
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            distancerPlayers.TryAdd(player.Key(), 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            distancerPlayers.TryRemove(player.Key(), out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#00f2ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}