using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Ninja : ISkill
    {
        private const Skills skillName = Skills.Ninja;
        private static readonly ConcurrentDictionary<nint, float> invisibilityChanged = [];
        private static readonly ConcurrentDictionary<uint, byte> invisiblePlayers = [];
        private const string bloodParticle = "particles/blood_impact/blood_impact_high.vpcf";
        private const string cameraViewModel = "models/sprays/spray_plane.vmdl";
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(bloodParticle);
            Instance.AddToManifest(cameraViewModel);
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                invisibilityChanged.Clear();
                invisiblePlayers.Clear();
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));

            if (playerInfo?.Skill != skillName) return;
            UpdateNinja(player);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));

            if (playerInfo?.Skill != skillName) return;
            UpdateNinja(player);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            if (invisiblePlayers.IsEmpty) return;

            var bomb = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").FirstOrDefault();
            uint? bombOwnerIndex = bomb != null && bomb.IsValid ? bomb.OwnerEntity?.Index : null;

            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid || player.Team == CsTeam.Spectator) continue;

                var targetHandle = player.Pawn.Value?.ObserverServices?.ObserverTarget.Value?.Handle ?? nint.Zero;

                foreach (var playerIndex in invisiblePlayers.Keys)
                {
                    var playerController = PlayerManager.GetPlayerEvent(Utilities.GetPlayerFromIndex((int)playerIndex));
                    if (playerController == null || !playerController.IsValid || playerController.Index == player.Index)
                        continue;

                    if (player.Team == playerController.Team)
                        continue;

                    var playerPawn = playerController.PlayerPawn.Value;
                    if (playerPawn == null || !playerPawn.IsValid) continue;

                    // Only the actively spectated pawn stays transmitted; hiding it breaks the camera.
                    if (targetHandle != nint.Zero && playerPawn.Handle == targetHandle)
                        continue;

                    var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)playerPawn.Index);
                    if (entity == null || !entity.IsValid) continue;

                    if (info.TransmitEntities.Contains(entity.Index))
                        info.TransmitEntities.Remove(entity.Index);

                    if (bomb == null || bombOwnerIndex != playerController.Index) continue;

                    if (info.TransmitEntities.Contains(bomb.Index))
                        info.TransmitEntities.Remove(bomb.Index);
                }
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 2 != 0) return;

            foreach (var player in Utilities.GetPlayers())
            {
                if (player.PlayerPawn?.Value == null || player.PlayerPawn?.Value?.Health <= 0)
                    invisiblePlayers.TryRemove(player.Index, out _);

                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill != skillName) continue;

                UpdateNinja(PlayerManager.GetPlayerFromEvent(player));

                var props = EntityManager.GetPlayerEntities((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index), "empty_prop");
                if (props.Count == 0) continue;

                var prop = Utilities.GetEntityFromIndex<CDynamicProp>((int)props[0]);
                if (prop == null || !prop.IsValid) continue;

                var pawn = player.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) continue;

                Vector newPos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + 30);
                QAngle newAngle = new(0, pawn.V_angle.Y, 0);
                prop.Teleport(newPos, newAngle, null);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();

            if (EntityManager.GetPlayerEntities(player.Index, "empty_prop").Count == 0)
                CreatePlayerPosProp(player);

            SkillUtils.ForceFullUpdateToAll();
        }
        
        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.SetPlayerInvisibility(player, 0);
            invisiblePlayers.TryRemove(player.Index, out _);
            EntityManager.DestroyPlayerEntities(player.Index);
        }

        private static void CreatePlayerPosProp(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var emptyProp = EntityManager.CreateTrackedDynamicProp(player.Index);
            if (emptyProp == null || !emptyProp.IsValid) return;

            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null) return;

            emptyProp.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(emptyProp.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            emptyProp.SetModel(cameraViewModel);
            emptyProp.Render = Color.FromArgb(1, 255, 255, 255);

            Vector newPos = new(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + 30);
            QAngle newAngle = new(playerPawn.AbsRotation.X, playerPawn.AbsRotation.Y, playerPawn.AbsRotation.Z);

            emptyProp.Teleport(newPos, newAngle);
            emptyProp.DispatchSpawn();

            Utilities.SetStateChanged(emptyProp, "CBaseEntity", "m_CBodyComponent");

            EntityManager.RegisterExisting(emptyProp, player.Index, "empty_prop");
        }

        private static void UpdateNinja(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return;

            if (player.PlayerPawn?.Value?.Health <= 0)
                return;

            var playerEvent = PlayerManager.GetPlayerEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var pawn = playerEvent.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            
            var flags = (PlayerFlags)pawn.Flags;
            var buttons = player.Buttons;

            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null) return;

            var activeWeapon = weaponServices.ActiveWeapon.Value;
            float percentInvisibility = 0;

            if (buttons.HasFlag(PlayerButtons.Duck))
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "duckPercentInvisibility");
            if (activeWeapon != null && (activeWeapon.DesignerName == "weapon_knife" || activeWeapon.DesignerName == "weapon_bayonet"))
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "knifePercentInvisibility");
            if (!buttons.HasFlag(PlayerButtons.Moveleft) && !buttons.HasFlag(PlayerButtons.Moveright) && !buttons.HasFlag(PlayerButtons.Forward) && !buttons.HasFlag(PlayerButtons.Back) && flags.HasFlag(PlayerFlags.FL_ONGROUND))
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "idlePercentInvisibility");

            if (invisibilityChanged.TryGetValue(playerEvent.Handle, out float oldInvisibility))
                if (percentInvisibility == oldInvisibility)
                    return;

            invisibilityChanged.AddOrUpdate(playerEvent.Handle, percentInvisibility, (_, _) => percentInvisibility);

            if (percentInvisibility > .9)
                invisiblePlayers.TryAdd(playerEvent.Index, 0);
            else
            {
                SkillUtils.SetPlayerInvisibility(playerEvent, percentInvisibility);
                invisiblePlayers.TryRemove(playerEvent.Index, out _);
            }
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;
            if (!invisiblePlayers.ContainsKey(player.Index)) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return;

            var particle = EntityManager.CreateTrackedParticleSystem(player.Index, bloodParticle, autoDestroySeconds: 3f);
            if (particle == null) return;

            Vector pos = new(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + 50);
            particle.Teleport(pos);
            particle.AcceptInput("Start");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#dedede", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float idlePercentInvisibility = .3f, float duckPercentInvisibility = .3f, float knifePercentInvisibility = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float IdlePercentInvisibility { get; set; } = idlePercentInvisibility;
            public float DuckPercentInvisibility { get; set; } = duckPercentInvisibility;
            public float KnifePercentInvisibility { get; set; } = knifePercentInvisibility;
        }
    }
}