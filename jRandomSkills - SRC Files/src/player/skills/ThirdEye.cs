using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class ThirdEye : ISkill
    {
        private const Skills skillName = Skills.ThirdEye;
        private static readonly ConcurrentDictionary<ulong, (uint, CDynamicProp)> cameras = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var camera in cameras)
                if (camera.Value.Item2 != null && camera.Value.Item2.IsValid)
                    camera.Value.Item2.AcceptInput("Kill");
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
            ChangeCamera(player, true);
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
                if (cameras.TryGetValue(player.SteamID, out var cameraInfo) && cameraInfo.Item2.IsValid)
                {
                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) continue;
                    if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                    {
                        ChangeCamera(player, true);
                        continue;
                    }

                    var pos = pawn.AbsOrigin - SkillUtils.GetForwardVector(pawn.EyeAngles) * SkillsInfo.GetValue<float>(skillName, "distance");
                    pos.Z += pawn.ViewOffset.Z;

                    var cam = cameraInfo.Item2;
                    if (cam == null || cam.AbsOrigin == null || cam.AbsRotation == null) continue;
                    cam.Teleport(pos, pawn.V_angle);
                }
        }

        private static void ChangeCamera(CCSPlayerController player, bool forceToDefault = false)
        {
            uint orginalCameraRaw;
            uint newCameraRaw;
            var pawn = player.PlayerPawn.Value;
            if (cameras.TryGetValue(player.SteamID, out var cameraInfo) && cameraInfo.Item2.IsValid)
            {
                orginalCameraRaw = cameraInfo.Item1;
                newCameraRaw = cameraInfo.Item2.EntityHandle.Raw;
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
            var camera = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (camera == null || !camera.IsValid) return 0;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null) return 0;

            Server.NextFrame(() =>
            {
                camera.SetModel("models/actors/ghost_speaker.vmdl");
                camera.Render = Color.FromArgb(0, 255, 255, 255);
                camera.Teleport(pawn!.AbsOrigin, pawn.EyeAngles);
                camera.DispatchSpawn();
            });

            cameras.AddOrUpdate(player.SteamID, (pawn.CameraServices!.ViewEntity.Raw, camera), (v, k) => (pawn.CameraServices!.ViewEntity.Raw, camera));
            return camera.EntityHandle.Raw;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#1b04cc", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float distance = 100f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Distance { get; set; } = distance;
        }
    }
}