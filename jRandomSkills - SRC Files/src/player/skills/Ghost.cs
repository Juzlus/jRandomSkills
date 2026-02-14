using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Ghost : ISkill
    {
        private const Skills skillName = Skills.Ghost;
        private static readonly string[] disabledWeapons =
        [
            "weapon_deagle", "weapon_revolver", "weapon_glock", "weapon_usp_silencer",
            "weapon_cz75a", "weapon_fiveseven", "weapon_p250", "weapon_tec9",
            "weapon_elite", "weapon_hkp2000", "weapon_ak47", "weapon_m4a1",
            "weapon_m4a4", "weapon_m4a1_silencer", "weapon_famas", "weapon_galilar",
            "weapon_aug", "weapon_sg553", "weapon_mp9", "weapon_mac10",
            "weapon_bizon", "weapon_mp7", "weapon_ump45", "weapon_p90",
            "weapon_mp5sd", "weapon_ssg08", "weapon_awp", "weapon_scar20",
            "weapon_g3sg1", "weapon_nova", "weapon_xm1014", "weapon_mag7",
            "weapon_sawedoff", "weapon_m249", "weapon_negev"
        ];
        private static readonly ConcurrentDictionary<CCSPlayerController, byte> invisiblePlayers = [];
        private const string bloodParticle = "particles/blood_impact/blood_impact_high.vpcf";

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(bloodParticle);
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
                SetWeaponAttack(player, false);
            invisiblePlayers.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);

            if (playerInfo?.Skill != skillName) return;
            SetWeaponAttack(player!, true);
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);

            if (playerInfo?.Skill != skillName) return;
            SetWeaponAttack(player!, true);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;

                var targetHandle = player.Pawn.Value?.ObserverServices?.ObserverTarget.Value?.Handle ?? nint.Zero;
                bool isObservingGhost = false;

                if (targetHandle != nint.Zero)
                {
                    var target = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == targetHandle);
                    var targetInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == target?.SteamID);
                    if (targetInfo?.Skill == skillName) isObservingGhost = true;
                }

                foreach (var _player in invisiblePlayers.Keys)
                    if (player.SteamID != _player.SteamID && !isObservingGhost)
                    {
                        var playerPawn = _player.PlayerPawn.Value;
                        if (playerPawn == null || !playerPawn.IsValid) continue;

                        var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)playerPawn.Index);
                        if (entity == null || !entity.IsValid) continue;
                        info.TransmitEntities.Remove(entity.Index);

                        var bombIndex = GetBombIndex(_player);
                        if (bombIndex == null) continue;
                        var bombEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)bombIndex);
                        if (bombEntity == null || !bombEntity.IsValid) continue;
                        info.TransmitEntities.Remove(bombEntity.Index);
                    }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();
            SetWeaponAttack(player, true);
            invisiblePlayers.TryAdd(player, 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.ResetPrintHTML(player);
            SetWeaponAttack(player, false);
            invisiblePlayers.TryRemove(player, out _);
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    UpdateHUD(player);
                if (!player.PawnIsAlive)
                    invisiblePlayers.TryRemove(player, out _);
            }
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            if (!invisiblePlayers.ContainsKey(player)) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return;

            CParticleSystem particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system")!;
            if (particle == null) return;

            particle.EffectName = bloodParticle;
            particle.StartActive = true;

            Vector pos = new(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + 50);
            particle.Teleport(pos);
            particle.DispatchSpawn();

            particle.AcceptInput("Start");
        }

        private static void SetWeaponAttack(CCSPlayerController player, bool disableWeapon)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player?.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            foreach (var weapon in pawn.WeaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
                    if (disabledWeapons.Contains(weapon.Value.DesignerName))
                    {
                        weapon.Value.NextPrimaryAttackTick = disableWeapon ? int.MaxValue : Server.TickCount;
                        weapon.Value.NextSecondaryAttackTick = disableWeapon ? int.MaxValue : Server.TickCount;

                        Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                        Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
                    }
        }

        private static void UpdateHUD(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            var weapon = pawn.WeaponServices.ActiveWeapon.Value;
            if (weapon == null || !weapon.IsValid || !disabledWeapons.Contains(weapon.DesignerName))
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#FFFFFF", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}