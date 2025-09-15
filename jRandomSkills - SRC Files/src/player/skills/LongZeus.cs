﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using CS2TraceRay.Class;
using CS2TraceRay.Enum;
using CS2TraceRay.Struct;
using jRandomSkills.src.player;
using System.Drawing;
using System.Numerics;
using static jRandomSkills.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace jRandomSkills
{
    public class LongZeus : ISkill
    {
        private const Skills skillName = Skills.LongZeus;
        private static readonly float maxDistance = Config.GetValue<float>(skillName, "maxDistance");

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, Config.GetValue<string>(skillName, "color"));
        }

        public unsafe static void WeaponFire(EventWeaponFire @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            if (playerInfo?.Skill != skillName) return;

            var pawn = player!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.WeaponServices == null) return;

            var activeWeapon = pawn.WeaponServices.ActiveWeapon.Value;
            if (activeWeapon == null || !activeWeapon.IsValid || activeWeapon.DesignerName != "weapon_taser") return;

            Vector eyePos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            Vector endPos = eyePos + SkillUtils.GetForwardVector(pawn.EyeAngles) * maxDistance;

            Ray ray = new(Vector3.Zero);
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
            CGameTrace trace = TraceRay.TraceHull(eyePos, endPos, filter, ray);

            if (Config.LoadedConfig.Settings.CS2TraceRayDebug)
            {
                CreateLine(eyePos, endPos, Color.FromArgb(255, 255, 255, 0));
                CreateLine(new Vector(trace.StartPos.X, trace.StartPos.Y, trace.StartPos.Z), new Vector(trace.EndPos.X, trace.EndPos.Y, trace.EndPos.Z), Color.FromArgb(255, 255, 0, 0));
                CreateLine(new Vector(trace.StartPos.X, trace.StartPos.Y, trace.StartPos.Z), new Vector(trace.Position.X, trace.Position.Y, trace.Position.Z), Color.FromArgb(255, 0, 0, 255));

                if (trace.DidHit())
                {
                    var val = Activator.CreateInstance(typeof(CBaseEntity), trace.HitEntity) as CBaseEntity;
                    player.PrintToChat($"Hit: {trace.DidHit()}, Entity: {(val == null ? "null" : val.DesignerName)}, Solid: {trace.AllSolid}, Contents: {(Contents)trace.Contents}");
                }
                else
                    player.PrintToChat($"Hit: {trace.DidHit()}, Entity: {trace.HitEntity}, Solid: {trace.AllSolid}, Contents: {(Contents)trace.Contents}");
            }

            if (!trace.HitPlayer(out CCSPlayerController? target) || target == null)
                return;

            if (target.Handle == player.Handle) return;
            SkillUtils.TakeHealth(target.PlayerPawn.Value, 9999);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.Zeus);
        }

        private static void CreateLine(Vector start, Vector end, Color color)
        {
            CBeam beam = Utilities.CreateEntityByName<CBeam>("beam")!;
            if (beam == null) return;

            beam.Render = color;
            beam.Width = 2.0f;
            beam.EndWidth = 2.0f;
            beam.Teleport(start);

            beam.EndPos.X = end.X;
            beam.EndPos.Y = end.Y;
            beam.EndPos.Z = end.Z;

            beam.DispatchSpawn();
            beam.AcceptInput("FollowEntity", beam, null!, "");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#6effc7", CsTeam onlyTeam = CsTeam.None, bool needsTeammates = false, float maxDistance = 4096f) : Config.DefaultSkillInfo(skill, active, color, onlyTeam, needsTeammates)
        {
            public float MaxDistance { get; set; } = maxDistance;
        }
    }
}