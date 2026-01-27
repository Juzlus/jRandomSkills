using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using CS2TraceRay.Class;
using CS2TraceRay.Struct;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class Cypher : ISkill
    {
        private const Skills skillName = Skills.Cypher;
        private static readonly ConcurrentDictionary<CCSPlayerController, PlayerSkill> playersInfo = [];
        private static readonly object setLock = new();

        private const string cameraPropModel = "models/props/de_train/hr_train_s2/train_electronics/train_electronics_security_camera_01.vmdl";
        private const string cameraViewModel = "models/actors/ghost_speaker.vmdl";

        public static void LoadSkill()
        {
            // SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            jRandomSkills.Instance.AddToManifest(cameraPropModel);
            jRandomSkills.Instance.AddToManifest(cameraViewModel);
        }

        public static void NewRound()
        {
            foreach (var playerSkill in playersInfo.Values)
                KillCamera(playerSkill);
            lock (setLock)
                playersInfo.Clear();
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;
            if (playersInfo.TryGetValue(player, out var playerSkill))
                ChangeCamera(playerSkill);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.CameraServices == null) return;
            playersInfo.TryAdd(player, new PlayerSkill
            {
                Player = player,
                CameraProp = null,
                CameraView = null,
                CameraActive = false,
                NextCamera = 0,
                PlayerCameraRaw = player.PlayerPawn.Value.CameraServices.ViewEntity.Raw,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (playersInfo.TryGetValue(player, out var playerSkill))
            {
                ChangeCamera(playerSkill, true);
                KillCamera(playerSkill);
            }
            playersInfo.TryRemove(player, out _);
        }

        private static void KillCamera(PlayerSkill playerSkill)
        {
            if (playerSkill.CameraView != null && playerSkill.CameraView.IsValid)
            {
                playerSkill.CameraView.AcceptInput("Kill");
                playerSkill.CameraView = null;
                playerSkill.CameraActive = false;
            }

            if (playerSkill.CameraProp != null && playerSkill.CameraProp.IsValid)
            {
                playerSkill.CameraProp.EmitSound("SolidMetal.BulletImpact");
                playerSkill.CameraProp.AcceptInput("Kill");
                playerSkill.CameraProp = null;
            }
        }

        public static void OnTick()
        {
            foreach (var playerSkill in playersInfo.Values)
            {
                if (playerSkill.CameraActive && playerSkill.CameraView != null && playerSkill.CameraView.IsValid)
                {
                    var pawn = playerSkill.Player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) continue;

                    if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                    {
                        DisableSkill(playerSkill.Player);
                        continue;
                    }
                    playerSkill.CameraView.Teleport(null, pawn.V_angle);
                }
                UpdateHUD(playerSkill);
            }
        }

        private static void ChangeCamera(PlayerSkill player, bool forceToDefault = false)
        {
            player.CameraActive = forceToDefault ? false : !player.CameraActive;
            if (player.CameraProp == null && !forceToDefault)
            {
                if (player.NextCamera > Server.TickCount) return;
                player.CameraProp = CreateCameraProp(player.Player);
                player.CameraView = CreateCameraView(player.Player);
                player.NextCamera = Server.TickCount + SkillsInfo.GetValue<float>(skillName, "Cooldown") * 64;
            }

            var pawn = player.Player.PlayerPawn.Value;
            if (player.PlayerCameraRaw == 0)
                return;

            BlockWeapon(player.Player, player.CameraActive);
            pawn!.CameraServices!.ViewEntity.Raw = player.CameraActive && player.CameraView != null ? player.CameraView.EntityHandle.Raw : player.PlayerCameraRaw;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");

            if (player.CameraActive && player.CameraView != null)
            {
                SkillUtils.ApplyScreenColor(player.Player, 0, 0, 255, 20, 100, 1020);
            }
        }

        private static CDynamicProp? CreateCameraProp(CCSPlayerController player)
        {
            var camera = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic_override");
            if (camera == null || !camera.IsValid) return null;

            camera.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
            camera.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(camera.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            camera.Entity!.Name = camera.Globalname = $"CypherCamera_{Server.TickCount}_{player.SteamID}";
            camera.DispatchSpawn();

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return null;

            Vector? cameraVector = GetNewCameraPosition(player);
            Vector diffVector = SkillUtils.GetForwardVector(playerPawn.V_angle) * 10;
            if (cameraVector == null) return null;
            cameraVector = new Vector(cameraVector.X + diffVector.X, cameraVector.Y + diffVector.Y, cameraVector.Z);

            Server.NextFrame(() =>
            {
                camera.SetModel(cameraPropModel);
                camera.Teleport(cameraVector, new QAngle(0, playerPawn.V_angle.Y + 180, 0));
            });

            return camera;
        }

        private static CDynamicProp? CreateCameraView(CCSPlayerController player)
        {
            var camera = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (camera == null || !camera.IsValid) return null;
            camera.DispatchSpawn();

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return null;

            Vector? cameraVector = GetNewCameraPosition(player);
            Vector diffVector = SkillUtils.GetForwardVector(playerPawn.V_angle) * 25;
            if (cameraVector == null) return null;


            cameraVector = new Vector(cameraVector.X - diffVector.X, cameraVector.Y - diffVector.Y, cameraVector.Z);
            if (cameraVector == null) return null;
            Server.NextFrame(() =>
            {
                camera.SetModel(cameraViewModel);
                camera.Render = Color.FromArgb(0, 255, 255, 255);
                camera.Teleport(cameraVector, playerPawn.EyeAngles);
            });

            return camera;
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null || param2.Attacker == null || param2.Attacker.Value == null)
                return;

            if (string.IsNullOrEmpty(param.Entity.Name)) return;
            if (!param.Entity.Name.StartsWith("CypherCamera_")) return;

            var nameParams = param.Entity.Name.Split('_')[2];
            _ = ulong.TryParse(nameParams, out ulong steamID);
            if (steamID == 0) return;

            var player = Utilities.GetPlayerFromSteamId(steamID);
            if (player == null) return;

            if (playersInfo.TryGetValue(player, out var playerSkill))
            {
                ChangeCamera(playerSkill, true);
                KillCamera(playerSkill);
            }
        }

        private static void BlockWeapon(CCSPlayerController player, bool block)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            foreach (var weapon in pawn.WeaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
                {
                    weapon.Value.NextPrimaryAttackTick = block ? int.MaxValue : Server.TickCount;
                    weapon.Value.NextSecondaryAttackTick = block ? int.MaxValue : Server.TickCount;

                    Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                    Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
                }
        }

        private unsafe static Vector? GetNewCameraPosition(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return null;

            Vector eyePos = new(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + playerPawn.ViewOffset.Z);
            Vector endPos = eyePos + SkillUtils.GetForwardVector(playerPawn.V_angle) * 4096f;

            Ray ray = new(Vector3.Zero);
            CTraceFilter filter = new(playerPawn.Index, playerPawn.Index)
            {
                m_nObjectSetMask = 0xf,
                m_nCollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PROJECTILE,
                m_nInteractsWith = 8589946881,
                m_nInteractsExclude = 0,
                m_nBits = 11,
                m_bIterateEntities = true,
                m_bHitTriggers = false,
                m_nInteractsAs = 0x40000,
                m_bOnlyHitIfHasPhysics = true
            };

            filter.m_nHierarchyIds[0] = playerPawn.GetHierarchyId();
            filter.m_nHierarchyIds[1] = 0;
            CGameTrace trace = TraceRay.TraceHull(eyePos, endPos, filter, ray);

            if (trace.HitWorld(out _))
                return new Vector(trace.EndPos.X, trace.EndPos.Y, trace.EndPos.Z);

            return null;
        }

        private static void UpdateHUD(PlayerSkill playerSkill)
        {
            if (playerSkill == null || playerSkill.Player == null) return;

            float cooldown = 0;
            float time = (playerSkill.NextCamera - Server.TickCount) / 64;
            cooldown = (int)Math.Ceiling(Math.Max(time, 0));

            var playerInfo = jRandomSkills.Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == playerSkill?.Player?.SteamID);
            if (playerInfo == null) return;

            if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else if (playerSkill?.CameraProp ==  null)
                playerInfo.PrintHTML = $"{playerSkill?.Player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public class PlayerSkill
        {
            public required CCSPlayerController Player { get; set; }
            public CDynamicProp? CameraProp { get; set; }
            public CDynamicProp? CameraView { get; set; }
            public uint PlayerCameraRaw { get; set; }
            public bool CameraActive { get; set; }
            public float NextCamera { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#34ebd5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float cooldown = 30) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Cooldown { get; set; } = cooldown;
        }
    }
}