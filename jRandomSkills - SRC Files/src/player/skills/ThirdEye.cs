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
            EntityManager.DestroyPlayerEntities(player.Index);
            cameras.TryRemove(player.Index, out _);
        }

        public static bool IsOrginalCamera(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return true;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return true;

            var cameraServices = pawn.CameraServices;
            if (cameraServices == null) return true;

            if (cameras.TryGetValue(player.Index, out var cameraInfo) && cameraInfo.Item1 != 0)
                return cameraServices.ViewEntity.Raw == cameraInfo.Item1;

            return true;
        }

        public static void OnTick()
        {
            if (cameras.IsEmpty) return;

            foreach (var player in PlayerManager.GetTickPlayers())
                if (cameras.TryGetValue(player.Index, out var cameraInfo) && cameraInfo.Item2 != 0)
                {
                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) continue;
                    if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                    {
                        ChangeCamera(player, true);
                        continue;
                    }

                    var cam = Utilities.GetEntityFromIndex<CDynamicProp>((int)cameraInfo.Item2);
                    if (cam == null || !cam.IsValid || cam.AbsOrigin == null || cam.AbsRotation == null) continue;

                    // Camera sits behind and slightly above the head, pulled in when a wall is in the way so it never clips.
                    // OnTick runs before this tick's movement, so the camera is placed where the player will be
                    // one tick later; without that it trails the player and the view judders while moving.
                    Vector predicted = new(
                        pawn.AbsOrigin.X + pawn.AbsVelocity.X / 64f,
                        pawn.AbsOrigin.Y + pawn.AbsVelocity.Y / 64f,
                        pawn.AbsOrigin.Z + pawn.AbsVelocity.Z / 64f);
                    Vector eye = new(predicted.X, predicted.Y, predicted.Z + pawn.ViewOffset.Z + SkillsInfo.GetValue<float>(skillName, "height"));
                    Vector back = SkillUtils.GetForwardVector(pawn.V_angle) * -SkillsInfo.GetValue<float>(skillName, "distance");
                    Vector wanted = eye + back;

                    var trace = RayTrace.TraceShape(player, eye, wanted);
                    float fraction = trace.HasValue && trace.Value.DidHit ? Math.Max(0f, trace.Value.Fraction - 0.1f) : 1f;
                    Vector pos = eye + back * fraction;

                    cam.Teleport(pos, new QAngle(pawn.V_angle.X, pawn.V_angle.Y, 0));
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

                var cam = Utilities.GetEntityFromIndex<CDynamicProp>((int)cameraInfo.Item2);
                if (cam == null || !cam.IsValid) return;

                newCameraRaw = cam.EntityHandle.Raw;
            }
            else
            {
                if (forceToDefault) return;
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
            var camera = EntityManager.CreateTrackedDynamicProp(player.Index);
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

            cameras.AddOrUpdate(player.Index, (pawn.CameraServices!.ViewEntity.Raw, camera.Index), (v, k) => (pawn.CameraServices!.ViewEntity.Raw, camera.Index));
            return camera.EntityHandle.Raw;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#1b04cc", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float distance = 100f, float height = 12f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Distance { get; set; } = distance;
            public float Height { get; set; } = height;
        }
    }
}