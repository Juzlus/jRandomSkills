using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Shade : ISkill
    {
        private const Skills skillName = Skills.Shade;
        private static readonly ConcurrentDictionary<uint, float> noSpace = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void NewRound()
        {
            noSpace.Clear();
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim)) return;

            var victimInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == victim?.SteamID);
            var attackerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);

            if (attackerInfo?.Skill == skillName)
                if (Instance.Random.NextDouble() <= attackerInfo.SkillChance)
                    TeleportAttackerBehindVictim(attacker!, victim!);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            
            float newChance = (float)Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "ChanceTo") - SkillsInfo.GetValue<float>(skillName, "ChanceFrom")) + SkillsInfo.GetValue<float>(skillName, "ChanceFrom");
            playerInfo.SkillChance = newChance;
            
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, newChance)}",
                border: !Utilities.GetPlayers().Any(p => p.Team == player.Team && !p.IsBot && p != player) ? "tb" : "t");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            noSpace.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void OnTick()
        {
            foreach (var (playerIndex, time) in noSpace)
            {
                var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                if (player == null || !player.IsValid) continue;

                if (time >= Server.TickCount)
                    UpdateHUD(player);
                else
                    SkillUtils.ResetPrintHTML(player);
            }
        }

        private static void UpdateHUD(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;
            playerInfo.PrintHTML = $"<font color='#FF0000'>{player.GetTranslation("shade_nospace")}</font>";
        }

        private unsafe static bool CheckTeleport(CCSPlayerController attacker, CCSPlayerController victim, Vector startPos, Vector endPos, QAngle angle)
        {
            var attackerPawn = attacker.PlayerPawn.Value;
            if (attackerPawn == null || !attackerPawn.IsValid) return false;

            var victimPawn = attacker.PlayerPawn.Value;
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

        private static void TeleportAttackerBehindVictim(CCSPlayerController attacker, CCSPlayerController victim)
        {
            var victimPawn = victim.PlayerPawn.Value;
            var attackerPawn = attacker.PlayerPawn.Value;

            if (victimPawn == null || attackerPawn == null || victimPawn.AbsOrigin == null || victimPawn.AbsRotation == null) return;

            Vector victimPos = new(victimPawn.AbsOrigin.X, victimPawn.AbsOrigin.Y, victimPawn.AbsOrigin.Z);
            QAngle victimAngles = new(victimPawn.AbsRotation.X, victimPawn.AbsRotation.Y, victimPawn.AbsRotation.Z);
            float distance = SkillsInfo.GetValue<float>(skillName, "teleportDistance");

            int[] angles = [0, 90, -90];
            bool teleported = false;

            foreach (int extraAngle in angles)
            {
                QAngle targetAngle = new(0, victimAngles.Y + extraAngle, 0);
                Vector direction = SkillUtils.GetForwardVector(targetAngle);
                Vector targetPos = victimPos - (direction * distance);

                if (CheckTeleport(attacker, victim, victimPos, targetPos, targetAngle))
                {
                    attackerPawn.Teleport(targetPos, targetAngle, Vector.Zero);
                    teleported = true;
                    break;
                }
            }

            if (!teleported)
                noSpace.AddOrUpdate(attacker.Index, Server.TickCount + (64 * 2), (_, _) => Server.TickCount + (64 * 2));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#4d4d4d", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float teleportDistance = 100f, float chanceFrom = .3f, float chanceTo = .45f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float TeleportDistance { get; set; } = teleportDistance;
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
        }
    }
}
