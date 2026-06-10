using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Drawing;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Ghost : ISkill
    {
        private const Skills skillName = Skills.Ghost;
        private static readonly string[] allowedWeapons = [
            "weapon_molotov", "weapon_incgrenade", "weapon_flashbang", "weapon_smokegrenade", "weapon_decoy", "weapon_hegrenade", "weapon_knife", "weapon_bayonet"
        ];
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
            foreach (var player in Utilities.GetPlayers())
                SetWeaponAttack(player, false);
            invisiblePlayers.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);

            if (playerInfo?.Skill != skillName) return;
            SetWeaponAttack(player!, true);
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);

            if (playerInfo?.Skill != skillName) return;
            SetWeaponAttack(player!, true);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid || player.Team == CsTeam.Spectator) continue;

                var targetHandle = player.Pawn.Value?.ObserverServices?.ObserverTarget.Value?.Handle ?? nint.Zero;
                bool isObservingGhost = false;

                if (targetHandle != nint.Zero)
                {
                    var target = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == targetHandle);
                    var targetInfo = PlayerManager.GetPlayerByIndex(target?.Index);
                    if (targetInfo?.Skill == skillName) isObservingGhost = true;
                }

                foreach (var playerIndex in invisiblePlayers.Keys)
                {
                    var playerController = Utilities.GetPlayerFromIndex((int)playerIndex);
                    if (playerController == null || !playerController.IsValid || playerController.Index == player.Index)
                        continue;

                    if (player.Team == playerController.Team)
                        continue;

                    if (!isObservingGhost)
                    {
                        var playerPawn = playerController.PlayerPawn.Value;
                        if (playerPawn == null || !playerPawn.IsValid) continue;

                        var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)playerPawn.Index);
                        if (entity == null || !entity.IsValid) continue;

                        if (info.TransmitEntities.Contains(entity.Index))
                            info.TransmitEntities.Remove(entity.Index);

                        var bombIndex = GetBombIndex(playerController);
                        if (bombIndex == null) continue;

                        var bombEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)bombIndex);
                        if (bombEntity == null || !bombEntity.IsValid) continue;

                        if (info.TransmitEntities.Contains(bombEntity.Index))
                            info.TransmitEntities.Remove(bombEntity.Index);
                    }
                }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();

            SetWeaponAttack(player, true);
            SkillUtils.SetPlayerInvisibility(player, .5f);
            invisiblePlayers.TryAdd(player.Index, 0);

            if (EntityManager.GetPlayerEntities(player.Index, "empty_prop").Count == 0)
                CreatePlayerPosProp(player);

            SkillUtils.ForceFullUpdateToAll();
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

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.ResetPrintHTML(player);
            SetWeaponAttack(player, false);
            SkillUtils.SetPlayerInvisibility(player, 0);

            invisiblePlayers.TryRemove(player.Index, out _);
            EntityManager.DestroyPlayerEntities(player.Index);

            SkillUtils.ForceFullUpdateToAll();
        }

        public static void OnTick()
        {
            if (Server.TickCount % 2 != 0) return;

            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill == skillName)
                    UpdateHUD(player);

                if (player.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                    invisiblePlayers.TryRemove(player.Index, out _);

                if (!invisiblePlayers.ContainsKey(player.Index)) continue;

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

        private static void SetWeaponAttack(CCSPlayerController player, bool disableWeapon)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player?.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            foreach (var weapon in pawn.WeaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid && !string.IsNullOrEmpty(weapon.Value.DesignerName))
                {
                    string weaponName = weapon.Value.DesignerName;
                    if (!allowedWeapons.Contains(weaponName))
                    {
                        weapon.Value.NextPrimaryAttackTick = disableWeapon ? int.MaxValue : Server.TickCount;
                        weapon.Value.NextSecondaryAttackTick = disableWeapon ? int.MaxValue : Server.TickCount;

                        Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                        Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
                    }
                }
        }

        private static void UpdateHUD(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            var weapon = pawn.WeaponServices.ActiveWeapon.Value;
            if (weapon == null || !weapon.IsValid || allowedWeapons.Contains(weapon.DesignerName))
            {
                playerInfo.PrintHTML = null;
                return;
            }

            playerInfo.PrintHTML = $"<font color='#FF0000'>{player.GetTranslation("disabled_weapon")}</font>";
        }

        private static uint? GetBombIndex(CCSPlayerController player)
        {
            var bombEntities = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").ToList();
            if (bombEntities.Count == 0) return null;

            var bomb = bombEntities.FirstOrDefault();
            if (bomb == null) return null;

            if (bomb.OwnerEntity.Index != player.Index) return null;
            return bomb.Index;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#FFFFFF", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Epic) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}