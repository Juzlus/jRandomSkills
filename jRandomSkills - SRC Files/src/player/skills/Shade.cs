using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CS2TraceRay.Class;
using CS2TraceRay.Struct;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Shade : ISkill
    {
        private const Skills skillName = Skills.Shade;
        private static readonly ConcurrentDictionary<CCSPlayerController, float> noSpace = [];

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
            noSpace.TryRemove(player, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void OnTick()
        {
            foreach (var item in noSpace)
                if (item.Value >= Server.TickCount)
                    UpdateHUD(item.Key);
                else
                    SkillUtils.ResetPrintHTML(item.Key);
        }

        private static void UpdateHUD(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;
            playerInfo.PrintHTML = $"<font color='#FF0000'>{player.GetTranslation("shade_nospace")}</font>";
        }

        private unsafe static bool CheckTeleport(CCSPlayerController player, Vector startPos, Vector endPos)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return false;

            Vector s = startPos + new Vector(0, 0, pawn.ViewOffset.Z / 2);
            Vector e = endPos + new Vector(0, 0, pawn.ViewOffset.Z / 2);

            ulong mask = pawn.Collision.CollisionAttribute.InteractsWith;
            ulong contents = pawn.Collision.CollisionGroup;
            CGameTrace trace = TraceRay.TraceShape(startPos, endPos, mask, contents, player);

            if (trace.DidHit()) return false;
            return IsPositionSafe(player, endPos);
        }

        private static bool IsPositionSafe(CCSPlayerController player, Vector pos)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return false;

            float footHeight = 0;
            float headHeight = 70;
            float innerDist = 12;

            ulong mask = playerPawn.Collision.CollisionAttribute.InteractsWith;
            ulong contents = playerPawn.Collision.CollisionGroup;

            Vector s1 = new(pos.X - innerDist, pos.Y - innerDist, pos.Z + footHeight);
            Vector e1 = new(pos.X + innerDist, pos.Y + innerDist, pos.Z + headHeight);
            CGameTrace t1 = TraceRay.TraceShape(s1, e1, mask, contents, player);
            if (t1.DidHit() || t1.AllSolid) return false;

            Vector s2 = new(pos.X + innerDist, pos.Y - innerDist, pos.Z + footHeight);
            Vector e2 = new(pos.X - innerDist, pos.Y + innerDist, pos.Z + headHeight);
            CGameTrace t2 = TraceRay.TraceShape(s2, e2, mask, contents, player);
            if (t2.DidHit() || t2.AllSolid) return false;

            return true;
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

                if (CheckTeleport(victim, victimPos, targetPos))
                {
                    attackerPawn.Teleport(targetPos, targetAngle, Vector.Zero);
                    teleported = true;
                    break;
                }
            }

            if (!teleported)
                noSpace.AddOrUpdate(attacker, Server.TickCount + (64 * 2), (_, _) => Server.TickCount + (64 * 2));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#4d4d4d", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float teleportDistance = 100f, float chanceFrom = .3f, float chanceTo = .45f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float TeleportDistance { get; set; } = teleportDistance;
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
        }
    }
}
