using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;

namespace src.player.skills
{
    public class ThirdEye : ISkill
    {
        private const Skills skillName = Skills.ThirdEye;
        private const string cameraViewModel = "models/sprays/spray_plane.vmdl";
        private static readonly ConcurrentDictionary<uint, (uint, uint)> cameras = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            jRandomSkills.Instance.AddToManifest(cameraViewModel);
        }

        public static void NewRound()
        {
            foreach (var cameraIndex in cameras.Values)
                EntityManager.DestroyEntity(cameraIndex.Item2);

            lock (setLock)
                cameras.Clear();
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

        public static bool IsOrginalCamera(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return true;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return true;

            var cameraServices = pawn.CameraServices;
            if (cameraServices == null) return true;

            if (cameras.TryGetValue(player.Key(), out var cameraInfo) && cameraInfo.Item1 != 0)
                return cameraServices.ViewEntity.Raw == cameraInfo.Item1;

            return true;
        }

        public static void OnTick()
        {
            if (cameras.IsEmpty) return;

            float distance = SkillsInfo.GetValue<float>(skillName, "distance");

            foreach (var (playerKey, cameraInfo) in cameras)
            {
                if (cameraInfo.Item2 == 0) continue;

                var player = PlayerManager.GetController(playerKey);
                if (player == null) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) continue;
                if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                {
                    ChangeCamera(player, true);
                    continue;
                }

                if (pawn.CameraServices == null || pawn.CameraServices.ViewEntity.Raw == cameraInfo.Item1) continue;

                var pos = pawn.AbsOrigin - SkillUtils.GetForwardVector(pawn.EyeAngles) * distance;
                pos.Z += pawn.ViewOffset.Z;

                var cam = Utilities.GetEntityFromIndex<CDynamicProp>((int)cameraInfo.Item2);
                if (cam == null || !cam.IsValid) continue;
                cam.Teleport(pos, pawn.V_angle);
            }
        }

        private static void ChangeCamera(CCSPlayerController player, bool forceToDefault = false)
        {
            uint orginalCameraRaw;
            uint newCameraRaw;
            var pawn = player.PlayerPawn.Value;
            if (cameras.TryGetValue(player.Key(), out var cameraInfo) && cameraInfo.Item2 != 0)
            {
                orginalCameraRaw = cameraInfo.Item1;

                var cam = Utilities.GetEntityFromIndex<CDynamicProp>((int)cameraInfo.Item2);
                if (cam == null || !cam.IsValid) return;

                newCameraRaw = cam.EntityHandle.Raw;
            }
            else
            {
                orginalCameraRaw = pawn!.CameraServices!.ViewEntity.Raw;
                newCameraRaw = CreateCamera(player);
            }

            if (newCameraRaw == 0)
                return;

            if (forceToDefault)
                pawn!.CameraServices!.ViewEntity.Raw = orginalCameraRaw;
            else
                pawn!.CameraServices!.ViewEntity.Raw =
                                pawn.CameraServices!.ViewEntity!.Raw == orginalCameraRaw
                                ? newCameraRaw
                                : orginalCameraRaw;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
        }

        private static uint CreateCamera(CCSPlayerController player)
        {
            var camera = EntityManager.CreateTrackedDynamicProp(player.Key());
            if (camera == null || !camera.IsValid) return 0;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null) return 0;

            Server.NextFrame(() =>
            {
                if (camera == null || !camera.IsValid) return;
                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

                var camNode = camera.CBodyComponent?.SceneNode?.Owner?.Entity;
                if (camNode != null)
                    camNode.Flags = (uint)(camNode.Flags & ~(1 << 2));

                camera.SetModel(cameraViewModel);
                camera.Render = Color.FromArgb(0, 255, 255, 255);
                camera.Teleport(pawn.AbsOrigin, pawn.EyeAngles);
                camera.DispatchSpawn();
            });

            cameras.AddOrUpdate(player.Key(), (pawn.CameraServices!.ViewEntity.Raw, camera.Index), (v, k) => (pawn.CameraServices!.ViewEntity.Raw, camera.Index));
            return camera.EntityHandle.Raw;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#1b04cc", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float distance = 100f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Distance { get; set; } = distance;
        }
    }
}