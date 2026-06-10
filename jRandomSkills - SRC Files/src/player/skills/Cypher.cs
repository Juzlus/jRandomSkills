using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using RayTraceAPI;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class Cypher : ISkill
    {
        private const Skills skillName = Skills.Cypher;
        private static readonly ConcurrentDictionary<uint, PlayerSkill> playersInfo = new();
        private static readonly object setLock = new();

        private const string cameraPropModel = "models/props/de_train/hr_train_s2/train_electronics/train_electronics_security_camera_01.vmdl";
        private const string cameraViewModel = "models/sprays/spray_plane.vmdl";

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
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
            if (playersInfo.TryGetValue(player.Index, out var playerSkill))
                ChangeCamera(playerSkill);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.CameraServices == null) return;
            playersInfo.TryAdd(player.Index, new PlayerSkill
            {
                Player = player.Index,
                CameraProp = null,
                CameraView = null,
                CameraActive = false,
                NextCamera = 0,
                NoSpace = 0,
                PlayerCameraRaw = player.PlayerPawn.Value.CameraServices.ViewEntity.Raw,
                LastPlayerAngle = QAngle.Zero,
                LastCameraAngle = QAngle.Zero
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (playersInfo.TryGetValue(player.Index, out var playerSkill))
            {
                ChangeCamera(playerSkill, true);
                KillCamera(playerSkill);
            }
            playersInfo.TryRemove(player.Index, out _);
        }

        private static void KillCamera(PlayerSkill playerSkill)
        {
            if (playerSkill == null) return;

            if (playerSkill.CameraView.HasValue && playerSkill.CameraView.Value != 0)
            {
                SkillUtils.SafeKillEntity<CDynamicProp>(playerSkill.CameraView);
                playerSkill.CameraView = null;
            }

            if (playerSkill.CameraProp.HasValue && playerSkill.CameraProp.Value != 0)
            {
                var cameraProp = Utilities.GetEntityFromIndex<CDynamicProp>((int)playerSkill.CameraProp);
                if (cameraProp != null && cameraProp.IsValid)
                    cameraProp.EmitSound("SolidMetal.BulletImpact");

                SkillUtils.SafeKillEntity<CDynamicProp>(playerSkill.CameraProp);
                playerSkill.CameraProp = null;
            }

            playerSkill.CameraActive = false;
            playerSkill.NextCamera = Server.TickCount + SkillsInfo.GetValue<float>(skillName, "Cooldown") * 64;
        }

        public static void OnTick()
        {
            foreach (var playerSkill in playersInfo.Values)
            {
                if (playerSkill.CameraActive && playerSkill.CameraView != null && playerSkill.CameraView != 0)
                {
                    var enemy = Utilities.GetPlayerFromIndex((int)playerSkill.Player);
                    if (enemy == null || !enemy.IsValid || enemy.PlayerPawn == null) continue;

                    var enemyPawn = enemy.PlayerPawn.Value;
                    if (enemyPawn == null || !enemyPawn.IsValid) continue;

                    if (enemyPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                    {
                        DisableSkill(enemy);
                        continue;
                    }

                    var cameraView = Utilities.GetEntityFromIndex<CDynamicProp>((int)playerSkill.CameraView);
                    if (cameraView == null || !cameraView.IsValid) continue;

                    cameraView.Teleport(null, enemyPawn.V_angle);
                }
                UpdateHUD(playerSkill);
            }
        }

        private static void ChangeCamera(PlayerSkill playerInfo, bool forceToDefault = false)
        {
            var player = Utilities.GetPlayerFromIndex((int)playerInfo.Player);
            if (player == null || !player.IsValid) return;

            playerInfo.CameraActive = forceToDefault ? false : !playerInfo.CameraActive;
            if (playerInfo.CameraProp == null && !forceToDefault)
            {
                if (playerInfo.NextCamera > Server.TickCount) return;

                var newProp = CreateCameraProp(player);
                playerInfo.CameraProp = newProp?.Index ?? null;
                
                if (playerInfo.CameraProp == null)
                {
                    playerInfo.NoSpace = Server.TickCount + (64 * 2);
                    return;
                }
                else
                    playerInfo.CameraView = CreateCameraView(newProp)?.Index ?? null;
                
                if (playerInfo.CameraView == null) return;
            }

            var pawn = player.PlayerPawn.Value;
            if (playerInfo.PlayerCameraRaw == 0)
                return;

            BlockWeapon(player, playerInfo.CameraActive);

            Server.NextWorldUpdate(() =>
            {
                if (pawn != null && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                {
                    if (playerInfo.CameraActive && playerInfo.CameraProp != null)
                    {
                        if (pawn.AbsRotation != null)
                            playerInfo.LastPlayerAngle = new QAngle(pawn.V_angle.X, pawn.V_angle.Y, 0);

                        var cameraProp = Utilities.GetEntityFromIndex<CDynamicProp>((int)playerInfo.CameraProp);
                        if (cameraProp == null || !cameraProp.IsValid || cameraProp.AbsRotation == null) return;

                        if (playerInfo.LastCameraAngle == QAngle.Zero)
                            playerInfo.LastCameraAngle = new QAngle(cameraProp.AbsRotation.X, cameraProp.AbsRotation.Y, 0);

                        pawn.Look(playerInfo.LastCameraAngle);
                    }
                    else if (!forceToDefault)
                    {
                        playerInfo.LastCameraAngle = new QAngle(pawn.V_angle.X, pawn.V_angle.Y, 0);
                        pawn.Look(playerInfo.LastPlayerAngle);
                    }
                }

                if (playerInfo.CameraActive && playerInfo.CameraView != null)
                {
                    var cameraView = Utilities.GetEntityFromIndex<CDynamicProp>((int)playerInfo.CameraView);
                    if (cameraView == null || !cameraView.IsValid) return;

                    pawn!.CameraServices!.ViewEntity.Raw = cameraView.EntityHandle.Raw;

                    var eventTarget = PlayerManager.GetPlayerFromEvent(player);
                    if (eventTarget != null && eventTarget.IsValid)
                        SkillUtils.ApplyScreenColor(eventTarget, 0, 0, 255, 20, 100, 1020);

                    uint playerIndex = player.Index;

                    Timer? cameraTimer = null;
                    cameraTimer = jRandomSkills.Instance.AddTimer(2f, () =>
                    {
                        var target = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && p.Index == playerIndex);
                        if (target == null || !target.IsValid || target.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                        {
                            cameraTimer?.Kill();
                            return;
                        }

                        if (!playersInfo.TryGetValue(target.Index, out var playerSkill) || playerSkill.CameraActive == false)
                        {
                            cameraTimer?.Kill();
                            return;
                        }

                        var eventTarget = PlayerManager.GetPlayerFromEvent(player);
                        if (eventTarget == null || !eventTarget.IsValid)
                        {
                            cameraTimer?.Kill();
                            return;
                        }

                        SkillUtils.ApplyScreenColor(eventTarget, 0, 0, 255, 20, 100, 1020);
                    }, TimerFlags.STOP_ON_MAPCHANGE | TimerFlags.REPEAT);
                }
                else
                    pawn!.CameraServices!.ViewEntity.Raw = playerInfo.PlayerCameraRaw;

                Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
            });
        }

        private static CDynamicProp? CreateCameraProp(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return null;

            Vector? cameraVector = GetNewCameraPosition(player);
            if (cameraVector == null) return null;

            Vector diffVector = SkillUtils.GetForwardVector(playerPawn.V_angle) * 10;
            Vector finalPos = new(cameraVector.X + diffVector.X, cameraVector.Y + diffVector.Y, cameraVector.Z);

            if (!CheckCameraPosition(finalPos, player))
                return null;

            var camera = EntityManager.CreateTrackedPropOverride(player.Index);
            if (camera == null || !camera.IsValid) return null;

            camera.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
            camera.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(camera.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            camera.Entity!.Name = camera.Globalname = $"CypherCamera_{Server.TickCount}_{player.Index}";

            camera.SetModel(cameraPropModel);
            camera.Teleport(cameraVector, new QAngle(0, playerPawn.V_angle.Y + 180, 0));
            camera.DispatchSpawn();

            return camera;
        }

        private static CDynamicProp? CreateCameraView(CDynamicProp? cameraProp)
        {
            if (cameraProp == null || !cameraProp.IsValid || cameraProp.AbsRotation == null || cameraProp.AbsOrigin == null)
                return null;

            Vector diffVector = SkillUtils.GetForwardVector(cameraProp.AbsRotation) * 25;
            Vector finalPos = cameraProp.AbsOrigin + diffVector;

            uint ownerIndex = EntityManager.SystemOwnerIndex;
            if (!string.IsNullOrEmpty(cameraProp.Globalname))
            {
                var parts = cameraProp.Globalname.Split('_');
                if (parts.Length >= 3 && uint.TryParse(parts[^1], out var idx))
                    ownerIndex = idx;
            }

            var camera = EntityManager.CreateTrackedDynamicProp(ownerIndex);
            if (camera == null || !camera.IsValid) return null;

            Server.NextFrame(() =>
            {
                if (camera == null || !camera.IsValid) return;
                camera.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(camera.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
                camera.SetModel(cameraViewModel);
                camera.Render = Color.FromArgb(1, 255, 255, 255);

                camera.Teleport(finalPos, cameraProp.AbsRotation);
                camera.DispatchSpawn();
                EntityManager.RegisterExisting(camera, ownerIndex, "prop_dynamic");
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
            _ = uint.TryParse(nameParams, out uint userIndex);
            if (userIndex == 0) return;

            var player = Utilities.GetPlayerFromIndex((int)userIndex);
            if (player == null) return;

            if (playersInfo.TryGetValue(player.Index, out var playerSkill))
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

        private static Vector? GetNewCameraPosition(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return null;

            Vector eyePos = new(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + playerPawn.ViewOffset.Z);
            Vector endPos = eyePos + SkillUtils.GetForwardVector(playerPawn.V_angle) * 4096f;

            ulong mask = (ulong)(InteractionLayers.Solid | InteractionLayers.Window | InteractionLayers.PassBullets);
            var result = RayTrace.TraceShape(player, eyePos, endPos, mask);

            if (result.HasValue && result.Value.HitWorld(out _))
            {
                Vector3 hitPost = result.Value.EndPos;
                Vector3 normal = result.Value.Normal;

                float offset = 8;
                Vector finalPos = new(
                        hitPost.X + (normal.X * offset),
                        hitPost.Y + (normal.Y * offset),
                        hitPost.Z + (normal.Z * offset)
                    );

                return finalPos;
            }

            return null;
        }

        private static bool CheckCameraPosition(Vector cameraVector, CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return false;

            Vector endPos = cameraVector + SkillUtils.GetForwardVector(new QAngle(0, playerPawn.V_angle.Y + 180, 0)) * -5;
            cameraVector += SkillUtils.GetForwardVector(new QAngle(0, playerPawn.V_angle.Y + 180, 0)) * 5;

            ulong mask = (ulong)(InteractionLayers.Solid | InteractionLayers.Window | InteractionLayers.PassBullets);
            var result = RayTrace.TraceShape(player, cameraVector, endPos, mask);

            if (result.HasValue && result.Value.HitWorld(out _))
                return true;

            return false;
        }

        private static void UpdateHUD(PlayerSkill playerSkill)
        {
            if (playerSkill == null) return;

            var player = Utilities.GetPlayerFromIndex((int)playerSkill.Player);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            float ticksLeft = playerSkill.NextCamera - Server.TickCount;
            int secondsLeft = (int)Math.Ceiling(Math.Max(ticksLeft / 64.0f, 0));

            if (playerSkill.NoSpace >= Server.TickCount)
                playerInfo.PrintHTML = $"<font color='#FF0000'>{player.GetTranslation("cypher_nospace")}</font>";
            else if (secondsLeft > 0 && playerSkill.CameraProp == null)
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{secondsLeft}</font>")}";
            else
                playerInfo.PrintHTML = null;
        }

        public class PlayerSkill
        {
            public required uint Player { get; set; }
            public uint? CameraProp { get; set; }
            public uint? CameraView { get; set; }
            public uint PlayerCameraRaw { get; set; }
            public bool CameraActive { get; set; }
            public float NextCamera { get; set; }
            public float NoSpace { get; set; }
            public required QAngle LastPlayerAngle { get; set; }
            public required QAngle LastCameraAngle { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#34ebd5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float cooldown = 30) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
        }
    }
}