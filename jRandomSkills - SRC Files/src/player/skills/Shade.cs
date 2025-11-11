using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using CS2TraceRay.Class;
using CS2TraceRay.Struct;
using System.Numerics;
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
        }

        private static void UpdateHUD(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;
            playerInfo.PrintHTML = $"{player.GetTranslation("shade_nospace")}";
        }

        private unsafe static bool CheckTeleport(CCSPlayerController player, Vector startPos, Vector endPos)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return false;
            Ray ray = new(new Vector3(-16, -16, -0), new Vector3(16, 16, 72));
            CTraceFilter filter = new(pawn.Index, pawn.Index)
            {
                m_nObjectSetMask = 0xf,
                m_nCollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT,
                m_nInteractsWith = pawn.GetInteractsWith(),
                m_nInteractsExclude = 0,
                m_nBits = 11,
                m_bIterateEntities = true,
                m_bHitTriggers = false,
                m_nInteractsAs = 0x40000
            };

            filter.m_nHierarchyIds[0] = pawn.GetHierarchyId();
            filter.m_nHierarchyIds[1] = 0;
            CGameTrace trace = TraceRay.TraceHull(startPos, endPos, filter, ray);
            return !trace.HitWorld(out _);
        }

        private static void TeleportAttackerBehindVictim(CCSPlayerController attacker, CCSPlayerController victim)
        {
            var victimPawn = victim.PlayerPawn.Value;
            var attackerPawn = attacker.PlayerPawn.Value;

            if (victimPawn == null || attackerPawn == null || victimPawn.AbsOrigin == null || victimPawn.AbsRotation == null) return;

            QAngle victimAngles = victimPawn.AbsRotation;
            Vector victimEyePos = new(victimPawn.AbsOrigin.X, victimPawn.AbsOrigin.Y, victimPawn.AbsOrigin.Z + victimPawn.ViewOffset.Z);
            int[] angles = [0, 90, -90];

            bool teleported = false;
            foreach (int extraAngle in angles)
            {
                QAngle newAngle = new(victimAngles.X, victimAngles.Y + extraAngle, victimAngles.Z);
                Vector behindPosition = victimEyePos - SkillUtils.GetForwardVector(newAngle) * SkillsInfo.GetValue<float>(skillName, "teleportDistance");
                if (!CheckTeleport(victim, victimEyePos, behindPosition)) continue;
                attackerPawn.Teleport(behindPosition, newAngle, new(0, 0, 0));
                teleported = true;
                break;
            }
            if (!teleported)
                noSpace.AddOrUpdate(attacker, Server.TickCount + (64 * 2), (k, v) => Server.TickCount + (64 * 2));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#4d4d4d", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float teleportDistance = 100f, float chanceFrom = .3f, float chanceTo = .45f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float TeleportDistance { get; set; } = teleportDistance;
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
        }
    }
}
