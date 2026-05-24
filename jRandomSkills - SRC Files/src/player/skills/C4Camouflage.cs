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

            if (weapon == "c4")
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

            foreach (var player in Utilities.GetPlayers())
            {
                if (player.LifeState != (byte)LifeState_t.LIFE_ALIVE && invisiblePlayers.ContainsKey(player.Index))
                {
                    invisiblePlayers.TryRemove(player.Index, out _);
                    SkillUtils.SetPlayerInvisibility(player, 0);
                }

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
            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid || player.Team == CsTeam.Spectator) continue;

                var targetHandle = player.Pawn.Value?.ObserverServices?.ObserverTarget.Value?.Handle ?? nint.Zero;
                bool isObservingC4Camouflage = false;

                if (targetHandle != nint.Zero)
                {
                    var target = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == targetHandle);
                    var targetInfo = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(target)?.Index);
                    if (targetInfo?.Skill == skillName) isObservingC4Camouflage = true;
                }

                foreach (var playerIndex in invisiblePlayers.Keys)
                {
                    var playerController = PlayerManager.GetPlayerEvent(Utilities.GetPlayerFromIndex((int)playerIndex));
                    if (playerController == null || !playerController.IsValid || playerController.Index == player.Index)
                        continue;

                    if (player.Team == playerController.Team)
                        continue;

                    if (!isObservingC4Camouflage)
                    {
                        var playerPawn = playerController.PlayerPawn.Value;
                        if (playerPawn == null || !playerPawn.IsValid) continue;

                        var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)playerPawn.Index);
                        if (entity == null || !entity.IsValid) continue;
                        info.TransmitEntities.Remove(entity.Index);

                        var bombIndex = GetBombIndex();
                        if (bombIndex == null) continue;

                        var bombEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)bombIndex);
                        if (bombEntity == null || !bombEntity.IsValid) continue;
                        info.TransmitEntities.Remove(bombEntity.Index);
                    }
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

        private static uint? GetBombIndex()
        {
            var bombEntities = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").ToList();
            if (bombEntities.Count == 0) return null;

            var bomb = bombEntities.FirstOrDefault();
            if (bomb == null) return null;

            return bomb.Index;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#00911f", CsTeam onlyTeam = CsTeam.Terrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Uncommon) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}