using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
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

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return ;

            var pawn = player!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;

            if (cameras.TryGetValue(player!.Index, out var cameraInfo) && cameraInfo.Item1 == pawn.CameraServices.ViewEntity.Raw)
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
            EntityManager.DestroyPlayerEntities(player.Index);
            cameras.TryRemove(player.Index, out _);
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
                if (cameras.TryGetValue(player.Index, out var cameraInfo) && cameraInfo.Item2 != 0)
                {
                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid || pawn.AbsOrigin ==  null) continue;

                    if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                    {
                        ChangeCamera(player, true);
                        continue;
                    }

                    Vector pos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + SkillsInfo.GetValue<float>(skillName, "distance"));
                    QAngle angle = new(90, 0, -pawn.V_angle.Y);

                    var camera = Utilities.GetEntityFromIndex<CDynamicProp>((int)cameraInfo.Item2);
                    if (camera == null || !camera.IsValid) continue;
                    camera.Teleport(pos, angle);
                }
        }

        private static void ChangeCamera(CCSPlayerController player, bool forceToDefault = false)
        {
            uint orginalCameraRaw;
            uint newCameraRaw;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;

            if (cameras.TryGetValue(player.Index, out var cameraInfo) && cameraInfo.Item2 != 0)
            {
                orginalCameraRaw = cameraInfo.Item1;

                var camera = Utilities.GetEntityFromIndex<CDynamicProp>((int)cameraInfo.Item2);
                if (camera == null || !camera.IsValid) return;

                newCameraRaw = camera.EntityHandle.Raw;
            }
            else
            {
                orginalCameraRaw = pawn!.CameraServices!.ViewEntity.Raw;
                newCameraRaw = CreateCamera(player);
            }

            if (newCameraRaw == 0)
                return;

            bool defaultCam = forceToDefault || (pawn.CameraServices.ViewEntity.Raw != orginalCameraRaw);
            pawn!.CameraServices!.ViewEntity.Raw = defaultCam ? orginalCameraRaw : newCameraRaw;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
            BlockWeapon(player, !defaultCam);
        }

        private static uint CreateCamera(CCSPlayerController player)
        {
            var camera = EntityManager.CreateTrackedDynamicProp(player.Index);
            if (camera == null || !camera.IsValid) return 0;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return 0;
            Vector pos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + SkillsInfo.GetValue<float>(skillName, "distance"));

            Server.NextFrame(() =>
            {
                if (camera == null || !camera.IsValid) return;
                camera.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(camera.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
                camera.SetModel("models/sprays/spray_plane.vmdl");
                camera.Render = Color.FromArgb(0, 255, 255, 255);
                camera.Teleport(pos, new QAngle(90, 0, 0));
                camera.DispatchSpawn();
            });

            cameras.AddOrUpdate(player.Index, (pawn.CameraServices!.ViewEntity.Raw, camera.Index), (k, v) => (pawn.CameraServices!.ViewEntity.Raw, camera.Index));
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

                    Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                    Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
                }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d1f542", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float distance = 1000f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float Distance { get; set; } = distance;
        }
    }
}