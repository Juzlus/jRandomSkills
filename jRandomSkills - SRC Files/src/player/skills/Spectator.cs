using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Spectator : ISkill
    {
        private const Skills skillName = Skills.Spectator;
        private static readonly ConcurrentDictionary<uint, (uint, uint, uint)> cameras = [];
        private static readonly ConcurrentDictionary<uint, DateTime> lastUse = [];
        private static readonly ConcurrentDictionary<uint, QAngle> savedAngles = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var info in cameras)
                EntityManager.DestroyEntity(info.Value.Item2);

            cameras.Clear();
            lastUse.Clear();
            savedAngles.Clear();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            if (cameras.TryRemove(playerIndex, out var cameraInfo) && cameraInfo.Item2 != 0)
                EntityManager.DestroyEntity(cameraInfo.Item2);

            lastUse.TryRemove(playerIndex, out _);
            savedAngles.TryRemove(playerIndex, out _);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            var pawn = player!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.CameraServices == null) return;
            if (cameras.TryGetValue(player.Key(), out var cameraInfo) && cameraInfo.Item1 != pawn.CameraServices.ViewEntity.Raw)
                BlockWeapon(player, true);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            float cooldown = SkillsInfo.GetValue<float>(skillName, "useCooldown");
            if (lastUse.TryGetValue(player.Key(), out var last) && (DateTime.Now - last).TotalSeconds < cooldown) return;
            lastUse[player.Key()] = DateTime.Now;

            ChangeCamera(player);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            ChangeCamera(player, true);
            EntityManager.DestroyPlayerEntities(player.Key());
            cameras.TryRemove(player.Key(), out _);
            lastUse.TryRemove(player.Key(), out _);
            savedAngles.TryRemove(player.Key(), out _);
        }

        public static void OnTick()
        {
            if (cameras.IsEmpty) return;

            foreach (var player in PlayerManager.GetTickPlayers())
                if (cameras.TryGetValue(player.Key(), out var cameraInfo) && cameraInfo.Item2 != 0)
                {
                    if (player == null || !player.IsValid) continue;

                    var enemy = PlayerManager.GetController(cameraInfo.Item3);
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

            if (cameras.TryGetValue(player.Key(), out var cameraInfo) && cameraInfo.Item2 != 0)
            {
                orginalCameraRaw = cameraInfo.Item1;

                var cam = Utilities.GetEntityFromIndex<CCSCustomPlayerCamera>((int)cameraInfo.Item2);
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

            NetStateFast.Changed(pawn, "CBasePlayerPawn", "m_pCameraServices");

            if (defaultCam && cameras.TryGetValue(player.Key(), out var current) && current.Item2 != 0)
                cameras[player.Key()] = (current.Item1, 0, current.Item3);

            BlockWeapon(player, !defaultCam);
            AlignView(player, pawn, defaultCam);
        }

        private static void AlignView(CCSPlayerController player, CCSPlayerPawn pawn, bool defaultCam)
        {
            if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            if (defaultCam)
            {
                if (savedAngles.TryRemove(player.Key(), out var saved))
                    pawn.Look(saved);
                return;
            }

            if (!cameras.TryGetValue(player.Key(), out var cameraInfo)) return;

            var enemyPawn = PlayerManager.GetController(cameraInfo.Item3)?.PlayerPawn?.Value;
            if (enemyPawn == null || !enemyPawn.IsValid) return;

            savedAngles.TryAdd(player.Key(), new QAngle(pawn.V_angle.X, pawn.V_angle.Y, 0));
            pawn.Look(new QAngle(enemyPawn.EyeAngles.X, enemyPawn.EyeAngles.Y, 0));
        }

        private static uint CreateCamera(CCSPlayerController player)
        {
            var viewer = player.PlayerPawn.Value;
            if (viewer == null || !viewer.IsValid) return 0;

            var enemies = PlayerManager.GetTickPlayers().Where(p =>
                p != null &&
                p.IsValid &&
                p.Team != player.Team &&
                p.PlayerPawn?.Value != null &&
                p.PlayerPawn.Value.IsValid &&
                p.PlayerPawn.Value.Health > 0).ToList();

            if (enemies.Count == 0) return 0;

            uint lastTarget = cameras.TryGetValue(player.Key(), out var previous) ? previous.Item3 : 0;
            if (lastTarget != 0 && enemies.Count > 1)
                enemies.RemoveAll(p => p.Key() == lastTarget);

            var enemy = enemies[Random.Shared.Next(enemies.Count)];
            var pawn = enemy.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return 0;

            CBaseEntity target = pawn;
            bool followEyes = true;
            var entities = EntityManager.GetPlayerEntities(enemy.Key(), "empty_prop");
            if (entities.Count > 0)
            {
                var entity = Utilities.GetEntityFromIndex<CDynamicProp>((int)entities[0]);
                if (entity != null && entity.IsValid)
                {
                    target = entity;
                    followEyes = false;
                }
            }

            var camera = PlayerCamera.Create(player.Key(), viewer, CustomCameraMode_t.CUSTOM_CAMERA_MODE_FOLLOW_POSITION);
            if (camera == null) return 0;

            PlayerCamera.Follow(camera, target, SkillsInfo.GetValue<float>(skillName, "distance"), followEyes ? 0f : 34f, followEyes);

            uint originalView = cameras.TryGetValue(player.Key(), out var cameraInfo)
                ? cameraInfo.Item1
                : player.PlayerPawn?.Value?.CameraServices?.ViewEntity.Raw ?? 0;

            cameras[player.Key()] = (originalView, camera.Index, enemy.Key());
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

                    NetStateFast.Changed(weapon.Value, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                    NetStateFast.Changed(weapon.Value, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
                }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#42f5da", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float distance = 100f, float useCooldown = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Distance { get; set; } = distance;
            public float UseCooldown { get; set; } = useCooldown;
        }
    }
}