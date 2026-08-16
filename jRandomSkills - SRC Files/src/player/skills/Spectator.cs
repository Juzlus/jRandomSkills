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
        private const string cameraViewModel = "models/sprays/spray_plane.vmdl";
        private static readonly ConcurrentDictionary<uint, (uint, uint, uint)> cameras = [];
        private static readonly ConcurrentDictionary<uint, DateTime> lastUse = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(cameraViewModel);
        }

        public static void NewRound()
        {
            foreach (var info in cameras)
                EntityManager.DestroyEntity(info.Value.Item2);

            cameras.Clear();
            lastUse.Clear();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            if (cameras.TryRemove(playerIndex, out var cameraInfo) && cameraInfo.Item2 != 0)
                EntityManager.DestroyEntity(cameraInfo.Item2);

            lastUse.TryRemove(playerIndex, out _);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            var pawn = player!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;
            if (cameras.TryGetValue(player.Index, out var cameraInfo) && cameraInfo.Item1 != pawn.CameraServices.ViewEntity.Raw)
                BlockWeapon(player, true);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            float cooldown = SkillsInfo.GetValue<float>(skillName, "useCooldown");
            if (lastUse.TryGetValue(player.Index, out var last) && (DateTime.Now - last).TotalSeconds < cooldown) return;
            lastUse[player.Index] = DateTime.Now;

            ChangeCamera(player);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            ChangeCamera(player, true);
            EntityManager.DestroyPlayerEntities(player.Index);
            cameras.TryRemove(player.Index, out _);
            lastUse.TryRemove(player.Index, out _);
        }

        public static void OnTick()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
                if (cameras.TryGetValue(player.Index, out var cameraInfo) && cameraInfo.Item2 != 0)
                {
                    if (player == null || !player.IsValid) continue;

                    var enemy = Utilities.GetPlayerFromIndex((int)cameraInfo.Item3);
                    if (enemy == null || !enemy.IsValid || enemy.PlayerPawn == null)
                    {
                        ChangeCamera(player, true);
                        continue;
                    }

                    var enemyPawn = enemy.PlayerPawn.Value;
                    if (enemyPawn == null || !enemyPawn.IsValid)
                    {
                        ChangeCamera(player, true);
                        continue;
                    }

                    if (enemyPawn.Health <= 0 || (player.PlayerPawn?.Value != null && player.PlayerPawn.Value.Health <= 0))
                        ChangeCamera(player, true);
                }
        }

        private static void ChangeCamera(CCSPlayerController player, bool forceToDefault = false)
        {
            if (player == null || !player.IsValid) return;

            uint orginalCameraRaw;
            uint newCameraRaw = 0;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;

            if (cameras.TryGetValue(player.Index, out var cameraInfo) && cameraInfo.Item2 != 0)
            {
                orginalCameraRaw = cameraInfo.Item1;

                var cam = Utilities.GetEntityFromIndex<CDynamicProp>((int)cameraInfo.Item2);
                if (cam != null && cam.IsValid)
                    EntityManager.DestroyEntity(cam.Index);

                if (!forceToDefault && pawn.CameraServices.ViewEntity.Raw == orginalCameraRaw)
                    newCameraRaw = CreateCamera(player);
            }
            else
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
            {
                defaultCam = true;
                pawn.CameraServices.ViewEntity.Raw = orginalCameraRaw;
            }

            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");

            if (defaultCam && cameras.TryGetValue(player.Index, out var current) && current.Item2 != 0)
                cameras[player.Index] = (current.Item1, 0, current.Item3);

            BlockWeapon(player, !defaultCam);
        }

        private static uint CreateCamera(CCSPlayerController player)
        {
            var camera = EntityManager.CreateTrackedDynamicProp(player.Index);
            if (camera == null || !camera.IsValid) return 0;

            var enemies = PlayerManager.GetTickPlayers().Where(p =>
                p != null &&
                p.IsValid &&
                p.Team != player.Team &&
                p.PlayerPawn?.Value != null &&
                p.PlayerPawn.Value.IsValid &&
                p.PlayerPawn.Value.Health > 0).ToList();

            if (enemies.Count == 0)
            {
                EntityManager.DestroyEntity(camera.Index);
                return 0;
            }

            uint lastTarget = cameras.TryGetValue(player.Index, out var previous) ? previous.Item3 : 0;
            if (lastTarget != 0 && enemies.Count > 1)
                enemies.RemoveAll(p => p.Index == lastTarget);

            var enemy = enemies[Random.Shared.Next(enemies.Count)];

            var pawn = enemy.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null || pawn.AbsOrigin == null)
            {
                EntityManager.DestroyEntity(camera.Index);
                return 0;
            }

            QAngle angle = new(0, pawn.EyeAngles.Y, 0);

            var pos = pawn.AbsOrigin - SkillUtils.GetForwardVector(angle) * SkillsInfo.GetValue<float>(skillName, "distance");
            pos.Z += pawn.ViewOffset.Z;

            Server.NextFrame(() =>
            {
                if (camera == null || !camera.IsValid) return;

                var camNode = camera.CBodyComponent?.SceneNode?.Owner?.Entity;
                if (camNode != null)
                    camNode.Flags = (uint)(camNode.Flags & ~(1 << 2));

                camera.SetModel(cameraViewModel);
                camera.Teleport(pos, angle);
                camera.DispatchSpawn();

                camera.Render = Color.FromArgb(1, 255, 255, 255);
                Utilities.SetStateChanged(camera, "CBaseModelEntity", "m_clrRender");

                CBaseEntity? target = pawn != null && pawn.IsValid ? pawn : null;
                var entities = EntityManager.GetPlayerEntities(enemy.Index, "empty_prop");

                if (entities.Count > 0)
                {
                    var entity = Utilities.GetEntityFromIndex<CDynamicProp>((int)entities[0]);
                    if (entity != null && entity.IsValid)
                        target = entity;
                }

                if (target == null || !target.IsValid) return;
                camera.AcceptInput("SetParent", target, target, "!activator");
            });

            uint originalView = cameras.TryGetValue(player.Index, out var cameraInfo)
                ? cameraInfo.Item1
                : player.PlayerPawn?.Value?.CameraServices?.ViewEntity.Raw ?? 0;

            cameras[player.Index] = (originalView, camera.Index, enemy.Index);
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#42f5da", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float distance = 100f, float useCooldown = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Distance { get; set; } = distance;
            public float UseCooldown { get; set; } = useCooldown;
        }
    }
}