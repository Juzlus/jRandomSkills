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
        private static readonly ConcurrentDictionary<ulong, (uint, CDynamicProp)> cameras = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var camera in cameras)
                if (camera.Value.Item2 != null && camera.Value.Item2.IsValid)
                    camera.Value.Item2.AcceptInput("Kill");
            cameras.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            if (playerInfo?.Skill != skillName) return ;

            var pawn = player!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;

            if (cameras.TryGetValue(player!.SteamID, out var cameraInfo) && cameraInfo.Item1 == pawn.CameraServices.ViewEntity.Raw)
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
            ChangeCamera(player, true);
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
                if (cameras.TryGetValue(player.SteamID, out var cameraInfo) && cameraInfo.Item2.IsValid)
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
                    cameraInfo.Item2.Teleport(pos, angle);
                }
        }

        private static void ChangeCamera(CCSPlayerController player, bool forceToDefault = false)
        {
            uint orginalCameraRaw;
            uint newCameraRaw;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;
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

            bool defaultCam = forceToDefault || (pawn.CameraServices.ViewEntity.Raw != orginalCameraRaw);
            pawn!.CameraServices!.ViewEntity.Raw = defaultCam ? orginalCameraRaw : newCameraRaw;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
            BlockWeapon(player, !defaultCam);
        }

        private static uint CreateCamera(CCSPlayerController player)
        {
            var camera = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (camera == null || !camera.IsValid) return 0;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return 0;
            Vector pos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + SkillsInfo.GetValue<float>(skillName, "distance"));

            Server.NextFrame(() =>
            {
                camera.SetModel("models/actors/ghost_speaker.vmdl");
                camera.Render = Color.FromArgb(0, 255, 255, 255);
                camera.Teleport(pos, new QAngle(90, 0, 0));
                camera.DispatchSpawn();
            });

            cameras.AddOrUpdate(player.SteamID, (pawn.CameraServices!.ViewEntity.Raw, camera), (k, v) => (pawn.CameraServices!.ViewEntity.Raw, camera));
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d1f542", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float distance = 1000f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Distance { get; set; } = distance;
        }
    }
}