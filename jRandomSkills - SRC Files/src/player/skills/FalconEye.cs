using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class FalconEye : ISkill
    {
        private const Skills skillName = Skills.FalconEye;
        private static readonly ConcurrentDictionary<uint, (uint, uint)> cameras = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var cameraIndex in cameras.Values)
                EntityManager.DestroyEntity(cameraIndex.Item2);

            cameras.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            var pawn = player!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;

            if (cameras.TryGetValue(player!.Key(), out var cameraInfo) && cameraInfo.Item1 != pawn.CameraServices.ViewEntity.Raw)
                BlockWeapon(player, true);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;
            ChangeCamera(player);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            ChangeCamera(player, true);
            EntityManager.DestroyPlayerEntities(player.Key());
            cameras.TryRemove(player.Key(), out _);
        }

        public static void OnTick()
        {
            if (cameras.IsEmpty || Server.TickCount % 8 != 0) return;

            foreach (var (playerKey, cameraInfo) in cameras)
            {
                if (cameraInfo.Item2 == 0) continue;

                var player = PlayerManager.GetController(playerKey);
                if (player == null) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) continue;

                if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE && pawn.CameraServices.ViewEntity.Raw != cameraInfo.Item1)
                    ChangeCamera(player, true);
            }
        }

        private static void ChangeCamera(CCSPlayerController player, bool forceToDefault = false)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;

            bool tracked = cameras.TryGetValue(player.Key(), out var cameraInfo) && cameraInfo.Item2 != 0;

            if (forceToDefault)
            {
                if (tracked)
                {
                    pawn.CameraServices.ViewEntity.Raw = cameraInfo.Item1;
                    NetStateFast.Changed(pawn, "CBasePlayerPawn", "m_pCameraServices");
                }

                BlockWeapon(player, false);
                return;
            }

            uint orginalCameraRaw;
            uint newCameraRaw;

            if (tracked)
            {
                orginalCameraRaw = cameraInfo.Item1;

                var camera = Utilities.GetEntityFromIndex<CCSCustomPlayerCamera>((int)cameraInfo.Item2);
                if (camera == null || !camera.IsValid)
                {
                    pawn.CameraServices.ViewEntity.Raw = orginalCameraRaw;
                    NetStateFast.Changed(pawn, "CBasePlayerPawn", "m_pCameraServices");
                    BlockWeapon(player, false);
                    return;
                }

                newCameraRaw = camera.EntityHandle.Raw;
            }
            else
            {
                orginalCameraRaw = pawn!.CameraServices!.ViewEntity.Raw;
                newCameraRaw = CreateCamera(player);
            }

            if (newCameraRaw == 0)
                return;

            bool defaultCam = pawn.CameraServices.ViewEntity.Raw != orginalCameraRaw;
            pawn!.CameraServices!.ViewEntity.Raw = defaultCam ? orginalCameraRaw : newCameraRaw;
            NetStateFast.Changed(pawn, "CBasePlayerPawn", "m_pCameraServices");
            BlockWeapon(player, !defaultCam);
        }

        private static uint CreateCamera(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.CameraServices == null) return 0;
            Vector pos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + SkillsInfo.GetValue<float>(skillName, "distance"));

            var camera = PlayerCamera.Create(player.Key(), pawn, CustomCameraMode_t.CUSTOM_CAMERA_MODE_CONTROLLED, pos, new QAngle(90, pawn.AbsRotation?.Y ?? 0, 0));
            if (camera == null) return 0;

            camera.AcceptInput("SetParent", pawn, pawn, "!activator");

            cameras.AddOrUpdate(player.Key(), (pawn.CameraServices!.ViewEntity.Raw, camera.Index), (k, v) => (pawn.CameraServices!.ViewEntity.Raw, camera.Index));
            return camera.EntityHandle.Raw;
        }

        private static void BlockWeapon(CCSPlayerController player, bool block)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null) return;

            foreach (var weapon in weaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
                {
                    weapon.Value.NextPrimaryAttackTick = block ? int.MaxValue : Server.TickCount;
                    weapon.Value.NextSecondaryAttackTick = block ? int.MaxValue : Server.TickCount;

                    NetStateFast.Changed(weapon.Value, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                    NetStateFast.Changed(weapon.Value, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
                }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d1f542", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float distance = 1000f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Distance { get; set; } = distance;
        }
    }
}