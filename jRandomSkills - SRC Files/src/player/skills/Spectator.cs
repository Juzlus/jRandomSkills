using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Spectator : ISkill
    {
        private const Skills skillName = Skills.Spectator;
        private static readonly ConcurrentDictionary<ulong, (uint, CDynamicProp, CCSPlayerPawn)> cameras = [];

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
            if (playerInfo?.Skill != skillName) return;

            var pawn = player!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;
            if (cameras.TryGetValue(player.SteamID, out var cameraInfo) && cameraInfo.Item1 != pawn.CameraServices.ViewEntity.Raw)
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
                    var pawn = cameraInfo.Item3;
                    if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE
                        || (player.PlayerPawn.Value != null && player.PlayerPawn.Value.LifeState != (byte)LifeState_t.LIFE_ALIVE))
                        ChangeCamera(player, true);
                }
        }

        private static void ChangeCamera(CCSPlayerController player, bool forceToDefault = false)
        {
            uint orginalCameraRaw;
            uint newCameraRaw = 0;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;

            if (cameras.TryGetValue(player.SteamID, out var cameraInfo) && cameraInfo.Item2 != null && cameraInfo.Item2.IsValid)
            {
                orginalCameraRaw = cameraInfo.Item1;
                cameraInfo.Item2.AcceptInput("Kill");
                if (!forceToDefault)
                    newCameraRaw = CreateCamera(player);
            } else
            {
                orginalCameraRaw = pawn.CameraServices.ViewEntity.Raw;
                if (!forceToDefault)
                    newCameraRaw = CreateCamera(player);
            }

            bool defaultCam = forceToDefault;
            if (newCameraRaw != 0)
            {
                defaultCam = forceToDefault || (pawn.CameraServices.ViewEntity.Raw != orginalCameraRaw);
                pawn.CameraServices.ViewEntity.Raw = defaultCam ? orginalCameraRaw : newCameraRaw;
            }
            else
                pawn.CameraServices.ViewEntity.Raw = orginalCameraRaw;

            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
            BlockWeapon(player, !defaultCam);
        }

        private static uint CreateCamera(CCSPlayerController player)
        {
            var camera = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (camera == null || !camera.IsValid) return 0;

            var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team).ToList();
            if (enemies.Count == 0)
                return 0;
            var enemy = enemies[Instance.Random.Next(enemies.Count)];

            var pawn = enemy.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null || pawn.AbsOrigin == null) return 0;

            var pos = pawn.AbsOrigin - SkillUtils.GetForwardVector(pawn.EyeAngles) * SkillsInfo.GetValue<float>(skillName, "distance");
            pos.Z += pawn.ViewOffset.Z;

            Server.NextFrame(() =>
            {
                camera.SetModel("models/actors/ghost_speaker.vmdl");
                camera.Render = Color.FromArgb(0, 255, 255, 255);
                camera.Teleport(pos, new QAngle(0, pawn.EyeAngles.Y, 0));
                camera.DispatchSpawn();
                camera.AcceptInput("SetParent", pawn, pawn, "!activator");
            });

            if (cameras.TryGetValue(player.SteamID, out var cameraInfo))
                cameras.AddOrUpdate(player.SteamID, (cameraInfo.Item1, camera, pawn), (k, v) => (cameraInfo.Item1, camera, pawn));
            else
                cameras.AddOrUpdate(player.SteamID, (pawn.CameraServices.ViewEntity.Raw, camera, pawn), (k, v) => (pawn.CameraServices.ViewEntity.Raw, camera, pawn));
            return camera.EntityHandle.Raw;
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#42f5da", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float distance = 100f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Distance { get; set; } = distance;
        }
    }
}