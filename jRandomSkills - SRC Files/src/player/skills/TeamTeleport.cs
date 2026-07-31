using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class TeamTeleport : ISkill
    {
        private const Skills skillName = Skills.TeamTeleport;
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
            public string? TeamateName { get; set; }
            public uint? TeamateIndex { get; set; }
            public Vector? TeamatePosition { get; set; }
            public int NoSpace { get; set; }
            public int NoEnemy { get; set; }
        }

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            SkillPlayerInfo.Clear();
        }

        public static void OnTick()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo?.Skill != skillName) continue;

                if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) continue;

                GetTeamate(player, skillInfo);
                UpdateHUD(player, skillInfo);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            SkillPlayerInfo.TryAdd(player.Index, new PlayerSkillInfo
            {
                SteamID = player.Index,
                CanUse = true,
                Cooldown = DateTime.MinValue,
                TeamateName = null,
                TeamateIndex = null,
                TeamatePosition = null,
                NoSpace = int.MinValue,
                NoEnemy = int.MinValue
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.Health <= 0) return;

            if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) return;
            if (!skillInfo.CanUse) return;

            skillInfo.NoEnemy = int.MinValue;
            skillInfo.NoSpace = int.MinValue;

            if (skillInfo.TeamateIndex == null || skillInfo.TeamatePosition == null)
            {
                skillInfo.NoEnemy = Server.TickCount + (64 * 2);
                return;
            }

            var victim = PlayerManager.GetTickPlayers().FirstOrDefault(p => p.Index == skillInfo.TeamateIndex.Value);
            if (victim == null || !victim.IsValid)
            {
                skillInfo.NoEnemy = Server.TickCount + (64 * 2);
                return;
            }

            TeleportToTeamate(player, victim, skillInfo.TeamatePosition, skillInfo);
        }

        private static void GetTeamate(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.V_angle == null || playerPawn.ViewOffset == null)
                return;

            Vector eyePos = new(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + playerPawn.ViewOffset.Z);
            Vector playerForward = SkillUtils.GetForwardVector(playerPawn.V_angle);

            float teleportAngle = SkillsInfo.GetValue<float>(skillName, "teleportAngle");
            float minDot = MathF.Cos(teleportAngle * MathF.PI / 180.0f);

            skillInfo.TeamateIndex = null;
            skillInfo.TeamateName = null;
            skillInfo.TeamatePosition = null;

            foreach (var teammate in PlayerManager.GetTickPlayers())
            {
                if (teammate == null || !teammate.IsValid || teammate.Index == player.Index || teammate.Team != player.Team)
                    continue;

                var teammatePawn = teammate.PlayerPawn?.Value;
                if (teammatePawn == null || !teammatePawn.IsValid || teammatePawn.Health <= 0 || teammatePawn.AbsOrigin == null)
                    continue;

                Vector targetPos = new(teammatePawn.AbsOrigin.X, teammatePawn.AbsOrigin.Y, teammatePawn.AbsOrigin.Z + (teammatePawn.ViewOffset.Z * 0.5f));
                Vector toTarget = new(targetPos.X - eyePos.X, targetPos.Y - eyePos.Y, targetPos.Z - eyePos.Z);

                float distanceSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y + toTarget.Z * toTarget.Z;
                if (distanceSq <= 0.0001f)
                    continue;

                float distance = MathF.Sqrt(distanceSq);

                float invLength = 1.0f / distance;

                toTarget.X *= invLength;
                toTarget.Y *= invLength;
                toTarget.Z *= invLength;

                float dot = playerForward.X * toTarget.X + playerForward.Y * toTarget.Y + playerForward.Z * toTarget.Z;

                if (distance > 30.0f && dot < minDot)
                    continue;

                skillInfo.TeamateIndex = teammate.Index;
                skillInfo.TeamateName = teammate.PlayerName;
                skillInfo.TeamatePosition = new Vector(teammatePawn.AbsOrigin.X, teammatePawn.AbsOrigin.Y, teammatePawn.AbsOrigin.Z);
            }
        }

        private unsafe static bool CheckTeleport(CCSPlayerController attacker, CCSPlayerController victim, Vector startPos, Vector endPos, QAngle angle)
        {
            var attackerPawn = attacker.PlayerPawn.Value;
            if (attackerPawn == null || !attackerPawn.IsValid) return false;

            var victimPawn = victim.PlayerPawn.Value;
            if (victimPawn == null || !victimPawn.IsValid) return false;

            var result = RayTrace.TraceHullShape(
                    startPos,
                    endPos,
                    victim,
                    attackerPawn.Collision.Mins,
                    attackerPawn.Collision.Maxs,
                    null,
                    null,
                    angle
                );

            if (!result.HasValue)
                return false;

            return !result.Value.DidHit;
        }

        private static void TeleportToTeamate(CCSPlayerController player, CCSPlayerController victim, Vector position, PlayerSkillInfo skillInfo)
        {
            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn == null || !playerPawn.IsValid || player.AbsRotation == null) return;

            QAngle playerAngles = new(player.AbsRotation.X, player.AbsRotation.Y, player.AbsRotation.Z);
            float distance = SkillsInfo.GetValue<float>(skillName, "teleportDistance");

            int[] angles = [0, 90, -90, 179];
            bool teleported = false;

            foreach (int extraAngle in angles)
            {
                QAngle targetAngle = new(0, playerAngles.Y + extraAngle, 0);
                Vector direction = SkillUtils.GetForwardVector(targetAngle);

                Vector targetPos = position - (direction * distance);
                Vector traceStart = position - (direction * 20f);

                if (CheckTeleport(player, victim, traceStart, targetPos, targetAngle))
                {
                    playerPawn.Teleport(targetPos, targetAngle, Vector.Zero);
                    teleported = true;
                    break;
                }
            }

            if (!teleported)
                skillInfo.NoSpace = Server.TickCount + (64 * 2);
            else
            {
                skillInfo.NoSpace = int.MinValue;
                skillInfo.NoEnemy = int.MinValue;
                skillInfo.CanUse = false;
                skillInfo.Cooldown = DateTime.Now;
            }
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            float cooldown = 0;

            float time = (float)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
            cooldown = Math.Max(time, 0);

            if (cooldown == 0 && !skillInfo.CanUse)
                skillInfo.CanUse = true;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null) return;

            if (cooldown != 0)
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
            else if (skillInfo.NoSpace >= Server.TickCount)
                playerInfo.PrintHTML = $"<font color='#FF0000'>{player.GetTranslation("shade_nospace")}</font>";
            else if (skillInfo.NoEnemy >= Server.TickCount)
                playerInfo.PrintHTML = $"<font color='#FF0000'>{player.GetTranslation("teamteleport_noenemy")}</font>";
            else
            {
                if (skillInfo.TeamateName == null)
                    playerInfo.PrintHTML = null;
                else
                {
                    string color = player.Team == CsTeam.Terrorist ? "#FFA500" : "#ADD8E6";
                    playerInfo.PrintHTML = $"{player.GetTranslation("teamteleport_hud_info", $"<font color='{color}'>{System.Net.WebUtility.HtmlEncode(skillInfo.TeamateName)}</font>")}";
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#bcf542", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = true, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Common, float cooldown = 15f, float teleportAngle = 10.0f, float teleportDistance = 100f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float TeleportAngle { get; set; } = teleportAngle;
            public float TeleportDistance { get; set; } = teleportDistance;
        }
    }
}