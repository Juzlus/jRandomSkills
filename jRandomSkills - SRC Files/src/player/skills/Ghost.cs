using System.Collections.Immutable;
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

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
        private static readonly ConcurrentDictionary<ulong, ConcurrentBag<uint>> invisibleEntities = [];

        public static void LoadSkill()
        {
            if (SkillsInfo.LoadedConfig.FirstOrDefault(s => s.Name == skillName.ToString())?.Active != true)
                return;

            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
                SetWeaponAttack(player, false);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);

            if (playerInfo?.Skill != skillName) return;
            SetWeaponVisibility(player!, false);
            SetWeaponAttack(player!, true);
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);

            if (playerInfo?.Skill != skillName) return;
            SetWeaponVisibility(player!, false);
            SetWeaponAttack(player!, true);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var (info, player) in infoList)
            {
                if (player == null) continue;
                foreach ((var playerId, var itemList) in invisibleEntities)
                    if (player.SteamID !=  playerId)
                        foreach (var entityIndex in itemList)
                        {
                            var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)entityIndex);
                            if (entity == null || !entity.IsValid) continue;
                            info.TransmitEntities.Remove(entity.Index);
                        }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();
            SetPlayerVisibility(player, false);
            SetWeaponVisibility(player, false);
            SetWeaponAttack(player, true);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.ResetPrintHTML(player);
            SetPlayerVisibility(player, true);
            SetWeaponVisibility(player, true);
            SetWeaponAttack(player, false);
            invisibleEntities.TryRemove(player.SteamID, out _);
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    UpdateHUD(player);
                if (!player.PawnIsAlive)
                    if (invisibleEntities.ContainsKey(player.SteamID))
                        invisibleEntities.TryRemove(player.SteamID, out _);
            }
        }

        private static void SetPlayerVisibility(CCSPlayerController player, bool visible)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn != null)
            {
                var color = visible ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(0, 255, 255, 255);
                var shadowStrength = visible ? 1.0f : 0.0f;

                playerPawn.Render = color;
                playerPawn.ShadowStrength = shadowStrength;
                Utilities.SetStateChanged(playerPawn, "CBaseModelEntity", "m_clrRender");
            }
        }

        private static void SetWeaponVisibility(CCSPlayerController player, bool visible)
        {
            if (!Instance.IsPlayerValid(player)) return;
            var playerPawn = player.PlayerPawn.Value!;
            if (playerPawn.WeaponServices == null) return;

            invisibleEntities.TryRemove(player.SteamID, out _);
            foreach (var weapon in playerPawn.WeaponServices.MyWeapons)
            {
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
                {
                    if (!visible)
                    {
                        if (invisibleEntities.TryGetValue(player.SteamID, out var items))
                        {
                            if (!items.Contains(weapon.Index))
                                items.Add(weapon.Index);
                        }
                        else
                            invisibleEntities.TryAdd(player.SteamID, [weapon.Index]);
                    }
                }
            }

            if (visible)
                invisibleEntities.TryRemove(player.SteamID, out _);
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#FFFFFF", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}