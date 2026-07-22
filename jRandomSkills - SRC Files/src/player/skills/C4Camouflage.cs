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
    public class C4Camouflage : ISkill
    {
        private const Skills skillName = Skills.C4Camouflage;
        private static readonly ConcurrentDictionary<uint, byte> invisiblePlayers = [];
        private const string bloodParticle = "particles/blood_impact/blood_impact_high.vpcf";
        private const string cameraViewModel = "models/sprays/spray_plane.vmdl";

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(bloodParticle);
            Instance.AddToManifest(cameraViewModel);
        }

        public static void NewRound()
        {
            invisiblePlayers.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;
            
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            Event.EnableTransmit();
            if (EntityManager.GetPlayerEntities(player.Index, "empty_prop").Count == 0)
                CreatePlayerPosProp(player);
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;

            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (weapon == "c4" && player.PlayerPawn?.Value?.Health > 0)
            {
                invisiblePlayers.TryAdd(player.Index, 0);
                SkillUtils.SetPlayerInvisibility(player, .5f);
            }
            else
            {
                invisiblePlayers.TryRemove(player.Index, out _);
                SkillUtils.SetPlayerInvisibility(player, 0);
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 2 != 0) return;

            var bomb = invisiblePlayers.IsEmpty
                ? null
                : Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").FirstOrDefault();

            foreach (var player in Utilities.GetPlayers())
            {
                if (player.PlayerPawn?.Value?.Health <= 0 && invisiblePlayers.ContainsKey(player.Index))
                {
                    invisiblePlayers.TryRemove(player.Index, out _);
                    SkillUtils.SetPlayerInvisibility(player, 0);
                }

                // CheckTransmit hides the model but the radar blip comes from spotted state, so clear it every tick.
                if (invisiblePlayers.ContainsKey(player.Index))
                    ClearSpottedState(player, bomb);

                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill != skillName) continue;

                var props = EntityManager.GetPlayerEntities(player.Index, "empty_prop");
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

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            if (invisiblePlayers.IsEmpty) return;

            var bomb = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").FirstOrDefault();
            if (bomb != null && !bomb.IsValid) bomb = null;

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

                    if (bomb == null) continue;

                    if (info.TransmitEntities.Contains(bomb.Index))
                        info.TransmitEntities.Remove(bomb.Index);
                }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();

            if (player == null || !player.IsValid) return;
            var playerPawn = player.PlayerPawn.Value;

            if (playerPawn == null || !playerPawn.IsValid) return;

            if (EntityManager.GetPlayerEntities(player.Index, "empty_prop").Count == 0)
                CreatePlayerPosProp(player);

            if (playerPawn.WeaponServices == null || playerPawn.WeaponServices.ActiveWeapon == null || !playerPawn.WeaponServices.ActiveWeapon.IsValid) return;
            if (playerPawn.WeaponServices.ActiveWeapon.Value == null || !playerPawn.WeaponServices.ActiveWeapon.Value.IsValid) return;

            var activeWeapon = playerPawn.WeaponServices.ActiveWeapon.Value;
            if (activeWeapon.DesignerName != "weapon_c4") return;

            invisiblePlayers.TryAdd(player.Index, 0);
            SkillUtils.SetPlayerInvisibility(player, .5f);

            SkillUtils.ForceFullUpdateToAll();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            invisiblePlayers.TryRemove(player.Index, out _);
            SkillUtils.SetPlayerInvisibility(player, 0);
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

        // Wipe spotted state (pawn + carried bomb) so a disguised carrier produces no radar blip.
        private static void ClearSpottedState(CCSPlayerController player, CC4? bomb)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn != null && pawn.IsValid)
            {
                pawn.EntitySpottedState.Spotted = false;
                pawn.EntitySpottedState.SpottedByMask[0] = 0;
                pawn.EntitySpottedState.SpottedByMask[1] = 0;
            }

            if (bomb != null && bomb.IsValid && bomb.OwnerEntity?.Index == player.Index)
            {
                bomb.EntitySpottedState.Spotted = false;
                bomb.EntitySpottedState.SpottedByMask[0] = 0;
                bomb.EntitySpottedState.SpottedByMask[1] = 0;
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#00911f", CsTeam onlyTeam = CsTeam.Terrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}