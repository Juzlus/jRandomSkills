using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using RayTraceAPI;
using System.Drawing;
using System.Numerics;
using TraceOptions = RayTraceAPI.TraceOptions;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.utils
{
    public static class RayTrace
    {
        private static PluginCapability<CRayTraceInterface> RayTraceInterface { get; } = new("raytrace:craytraceinterface");

        public static CustomTraceResult? TraceShape(CCSPlayerController player, Vector startPos, Vector endPos, ulong? mask = null, ulong? contents = null)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null)
                return null;

            var rayTrace = RayTraceInterface.Get();
            if (rayTrace == null)
                return null;

            if (mask == null)
            {
                mask = playerPawn.Collision.CollisionAttribute.InteractsWith | (ulong)InteractionLayers.Hitboxes;
                mask &= ~(ulong)InteractionLayers.PlayerClip;
            }
            contents ??= playerPawn.Collision.CollisionGroup;

            bool drawBeam = Config.LoadedConfig.TraceRayBeam;

            TraceOptions options = new()
            {
                InteractsWith = (ulong)mask,
                InteractsExclude = (ulong)contents,
                DrawBeam = drawBeam == true ? 1 : 0,
            };

            rayTrace.TraceEndShape(startPos, endPos, playerPawn, options, out TraceResult result);

            return new CustomTraceResult(result, startPos, (ulong)mask, (ulong)contents, drawBeam);
        }

        public static CustomTraceResult? EyeTrace(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null)
                return null;

            var playerInfo = jRandomSkills.Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            if (playerInfo == null) return null;

            float maxDistance = SkillsInfo.GetValue<float>(playerInfo.Skill, "maxDistance");
            if (maxDistance == 0) maxDistance = 4096f;

            Vector startPos = new(playerPawn.AbsOrigin!.X, playerPawn.AbsOrigin!.Y, playerPawn.AbsOrigin!.Z + playerPawn.ViewOffset.Z);
            Vector endPos = startPos + SkillUtils.GetForwardVector(playerPawn.EyeAngles) * maxDistance;

            return TraceShape(player, startPos, endPos);
        }

        public static CustomTraceResult? TraceHullShape(Vector startPos, Vector endPos, CCSPlayerController player, Vector? mins = null, Vector? maxs = null, ulong? mask = null, ulong? contents = null, QAngle? angle = null)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null)
                return null;

            var rayTrace = RayTraceInterface.Get();
            if (rayTrace == null)
                return null;

            mask ??= playerPawn.Collision.CollisionAttribute.InteractsWith;
            contents ??= playerPawn.Collision.CollisionGroup;

            bool drawBeam = Config.LoadedConfig.TraceRayBeam;

            TraceOptions options = new()
            {
                InteractsWith = (ulong)mask,
                InteractsExclude = (ulong)contents,
                DrawBeam = drawBeam == true ? 1 : 0,
            };

            mins ??= playerPawn.Collision.Mins;
            maxs ??= playerPawn.Collision.Maxs;

            rayTrace.TraceHullShape(startPos, endPos, mins, maxs, playerPawn, options, out TraceResult result);

            if (drawBeam)
            {
                angle ??= new(0, playerPawn.EyeAngles.Y, 0);
                DrawBoxEdges(startPos, endPos, angle, mins, maxs, Color.Green);
            }

            return new CustomTraceResult(result, startPos, (ulong)mask, (ulong)contents, drawBeam);
        }

        private static void DrawBoxEdges(Vector start, Vector end, QAngle angles, Vector mins, Vector maxs, Color color)
        {
            AngleVectors(angles, out Vector forward, out Vector right, out Vector up);

            float halfLength = (maxs.X - mins.X) / 2.0f;

            Vector visualStart = start - (forward * halfLength);
            Vector visualEnd = end + (forward * halfLength);
            
            Vector GetVertex(Vector center, float rx, float ux)
            {
                return center + (right * rx) + (up * ux);
            }

            Vector[] s = [
                GetVertex(visualStart, mins.Y, mins.Z),
                GetVertex(visualStart, maxs.Y, mins.Z),
                GetVertex(visualStart, maxs.Y, maxs.Z),
                GetVertex(visualStart, mins.Y, maxs.Z)
            ];

            Vector[] e = [
                GetVertex(visualEnd, mins.Y, mins.Z),
                GetVertex(visualEnd, maxs.Y, mins.Z),
                GetVertex(visualEnd, maxs.Y, maxs.Z),
                GetVertex(visualEnd, mins.Y, maxs.Z)
            ];

            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                CreateBeamLine(s[i], s[next], color);
                CreateBeamLine(e[i], e[next], color);
                CreateBeamLine(s[i], e[i], color);
            }
        }

        private static void AngleVectors(QAngle angles, out Vector forward, out Vector right, out Vector up)
        {
            float sp, sy, cp, cy, sr, cr;

            float pitch = angles.X * (MathF.PI / 180.0f);
            float yaw = angles.Y * (MathF.PI / 180.0f);
            float roll = angles.Z * (MathF.PI / 180.0f);

            sp = MathF.Sin(pitch); cp = MathF.Cos(pitch);
            sy = MathF.Sin(yaw); cy = MathF.Cos(yaw);
            sr = MathF.Sin(roll); cr = MathF.Cos(roll);

            forward = new Vector(cp * cy, cp * sy, -sp);
            right = new Vector(-1 * sr * sp * cy + cr * sy, -1 * sr * sp * sy - cr * cy, -1 * sr * cp);
            up = new Vector(cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp);
        }

        private static void CreateBeamLine(Vector start, Vector end, Color color)
        {
            var beam = Utilities.CreateEntityByName<CBeam>("env_beam");
            if (beam == null || !beam.IsValid) return;

            beam.Render = color;
            beam.Width = 1.3f;

            beam.Teleport(start, new QAngle(0, 0, 0), new Vector(0, 0, 0));
            beam.EndPos.X = end.X;
            beam.EndPos.Y = end.Y;
            beam.EndPos.Z = end.Z;

            beam.DispatchSpawn();
        }

        public static float Distance(this CustomTraceResult result)
        {
            return Vector3.Distance(result.StartPos, result.EndPos);
        }

        public static Vector3 Direction(this CustomTraceResult result)
        {
            return Vector3.Normalize(result.EndPos - result.Normal);
        }

        public static bool HitEntityByDesignerName<T>(this CustomTraceResult result, out T? entity, string designerName, DesignerNameMatchType matchType = DesignerNameMatchType.Equals) where T : CEntityInstance
        {
            T? val = (T?)Activator.CreateInstance(typeof(T), result.HitEntity);
            if ((object?)val != null && matchType switch
            {
                DesignerNameMatchType.Equals => val.DesignerName == designerName,
                DesignerNameMatchType.StartsWith => val.DesignerName.StartsWith(designerName, StringComparison.OrdinalIgnoreCase),
                DesignerNameMatchType.EndsWith => val.DesignerName.EndsWith(designerName, StringComparison.OrdinalIgnoreCase),
                _ => false,
            })
            {
                entity = val;
                return true;
            }

            entity = null;
            return false;
        }

        public static bool HitEntity(this CustomTraceResult result, out CBaseEntity? entity)
        {
            CEntityInstance entityInstance = new(result.HitEntity);
            if (string.IsNullOrEmpty(entityInstance.DesignerName))
            {
                entity = null;
                return false;
            }

            entity = entityInstance.As<CBaseEntity>();
            return entity != null;
        }

        public static bool HitPlayer(this CustomTraceResult result, out CCSPlayerController? player)
        {
            if (result.HitEntityByDesignerName<CCSPlayerPawn>(out CCSPlayerPawn? entity, "player"))
            {
                player = entity?.OriginalController.Value;
                return player != null;
            }

            player = null;
            return false;
        }

        public static bool HitWeapon(this CustomTraceResult result, out CBasePlayerWeapon? weapon)
        {
            return result.HitEntityByDesignerName<CBasePlayerWeapon>(out weapon, "weapon_", DesignerNameMatchType.StartsWith);
        }

        public static bool HitChicken(this CustomTraceResult result, out CChicken? chicken)
        {
            return result.HitEntityByDesignerName<CChicken>(out chicken, "chicken");
        }

        public static bool HitButton(this CustomTraceResult result, out CBaseButton? button)
        {
            return result.HitEntityByDesignerName<CBaseButton>(out button, "func_door");
        }

        public static bool HitBuyzone(this CustomTraceResult result, out CBuyZone? buyzone)
        {
            return result.HitEntityByDesignerName<CBuyZone>(out buyzone, "func_buyzone");
        }

        public static bool HitSky(this CustomTraceResult result, out CEnvSky? sky)
        {
            return result.HitEntityByDesignerName<CEnvSky>(out sky, "env_sky");
        }

        public static bool HitDoor(this CustomTraceResult result, out CBaseDoor? door)
        {
            return result.HitEntityByDesignerName<CBaseDoor>(out door, "func_door");
        }

        public static bool HitDoor(this CustomTraceResult result, out CRotDoor? door)
        {
            return result.HitEntityByDesignerName<CRotDoor>(out door, "func_door_rotating");
        }

        public static bool HitLadder(this CustomTraceResult result, out CFuncLadder? ladder)
        {
            return result.HitEntityByDesignerName<CFuncLadder>(out ladder, "func_ladder");
        }

        public static bool HitGrenade(this CustomTraceResult result, out CBaseCSGrenade? grenade)
        {
            return result.HitEntityByDesignerName<CBaseCSGrenade>(out grenade, "grenade");
        }

        public static bool HitPlantedC4(this CustomTraceResult result, out CPlantedC4? c4)
        {
            return result.HitEntityByDesignerName<CPlantedC4>(out c4, "planted_c4");
        }

        public static bool HitPointWorldText(this CustomTraceResult result, out CPointWorldText? pointWorldText)
        {
            return result.HitEntityByDesignerName<CPointWorldText>(out pointWorldText, "point_worldtext");
        }

        public static bool HitC4(this CustomTraceResult result, out CC4? c4)
        {
            return result.HitEntityByDesignerName<CC4>(out c4, "weapon_c4");
        }

        public static bool HitWorld(this CustomTraceResult result, out CWorld? world)
        {
            return result.HitEntityByDesignerName<CWorld>(out world, "worldent");
        }

        public enum DesignerNameMatchType
        {
            Equals,
            StartsWith,
            EndsWith
        }
    }
}
