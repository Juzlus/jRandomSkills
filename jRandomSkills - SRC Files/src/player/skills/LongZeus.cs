using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using CS2TraceRay.Class;
using CS2TraceRay.Enum;
using CS2TraceRay.Struct;
using src.utils;
using System.Drawing;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class LongZeus : ISkill
    {
        private const Skills skillName = Skills.LongZeus;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
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
            Vector endPos = eyePos + SkillUtils.GetForwardVector(pawn.EyeAngles) * SkillsInfo.GetValue<float>(skillName, "maxDistance");

            ulong hitboxes = 0x2;
            ulong mask = pawn.Collision.CollisionAttribute.InteractsWith | hitboxes;
            ulong contents = pawn.Collision.CollisionGroup;
            CGameTrace trace = TraceRay.TraceShape(eyePos, endPos, mask, contents, player);

            if (Config.LoadedConfig.CS2TraceRayDebug)
            {
                SkillUtils.CreateLine(eyePos, endPos, Color.FromArgb(255, 255, 255, 0));
                SkillUtils.CreateLine(new Vector(trace.StartPos.X, trace.StartPos.Y, trace.StartPos.Z), new Vector(trace.EndPos.X, trace.EndPos.Y, trace.EndPos.Z), Color.FromArgb(255, 255, 0, 0));
                SkillUtils.CreateLine(new Vector(trace.StartPos.X, trace.StartPos.Y, trace.StartPos.Z), new Vector(trace.Position.X, trace.Position.Y, trace.Position.Z), Color.FromArgb(255, 0, 0, 255));

                if (trace.DidHit())
                {
                    var val = Activator.CreateInstance(typeof(CBaseEntity), trace.HitEntity) as CBaseEntity;
                    player.PrintToChat($"Hit: {trace.DidHit()}, Entity: {(val == null ? "null" : val.DesignerName)}, Solid: {trace.AllSolid}, Contents: {(Contents)trace.Contents}, Hitbox: {trace.HitboxData[0].HitGroup}");
                }
                else
                    player.PrintToChat($"Hit: {trace.DidHit()}, Object: {trace.HitEntity}, Solid: {trace.AllSolid}, Contents: {(Contents)trace.Contents}, Hitbox: {trace.HitboxData[0].HitGroup}");
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#6effc7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float maxDistance = 4096f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float MaxDistance { get; set; } = maxDistance;
        }
    }
}