using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Chicken : ISkill
    {
        private const Skills skillName = Skills.Chicken;
        private static readonly HashSet<string> disabledWeapons =
        [
            "weapon_ak47", "weapon_m4a4", "weapon_m4a1", "weapon_m4a1_silencer",
            "weapon_famas", "weapon_galilar", "weapon_aug", "weapon_sg553", 
            "weapon_mp9", "weapon_mac10", "weapon_bizon", "weapon_mp7",
            "weapon_ump45", "weapon_p90", "weapon_mp5sd", "weapon_ssg08",
            "weapon_awp", "weapon_scar20", "weapon_g3sg1", "weapon_nova",
            "weapon_xm1014", "weapon_mag7", "weapon_sawedoff", "weapon_m249",
            "weapon_negev", "weapon_sg556"
        ];
        private static readonly ConcurrentDictionary<uint, uint> chickens = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
                SetWeaponAttack(player, false);

            var chickenIndices = chickens.Values.ToArray();
            foreach (var idx in chickenIndices)
                SkillUtils.SafeKillEntity<CBaseModelEntity>(idx);

            chickens.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            SetWeaponAttack(player, true);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn != null && playerPawn.IsValid)
            {
                Event.EnableTransmit();
                playerPawn.VelocityModifier = 1.1f;

                playerPawn.Health = 50;
                Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");

                SkillUtils.ChangePlayerScale(player, .2f);

                playerPawn.Render = Color.FromArgb(0, 255, 255, 255);
                playerPawn.ShadowStrength = 0f;
                Utilities.SetStateChanged(playerPawn, "CBaseModelEntity", "m_clrRender");

                SetWeaponAttack(player, true);
                CreateChicken(player);
            }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.ResetPrintHTML(player);
            var playerPawn = player.PlayerPawn?.Value;

            if (playerPawn != null)
            {
                playerPawn.VelocityModifier = 1f;

                if (playerPawn.Health > 0)
                {
                    playerPawn.Health = Math.Min(playerPawn.Health + 50, 100);
                    Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");
                }

                SkillUtils.ChangePlayerScale(player, 1);

                playerPawn.Render = Color.FromArgb(255, 255, 255, 255);
                playerPawn.ShadowStrength = 1.0f;
                Utilities.SetStateChanged(playerPawn, "CBaseModelEntity", "m_clrRender");

                SetWeaponAttack(player, false);
            }

            if (chickens.TryRemove(player.Index, out var chickenIndex))
                SkillUtils.SafeKillEntity<CBaseModelEntity>(chickenIndex);
        }

        private static void SetWeaponAttack(CCSPlayerController player, bool disableWeapon)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player?.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null || pawn.WeaponServices.MyWeapons == null) return;

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

        private static void CreateChicken(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;
            var chickenModel = EntityManager.CreateTrackedDynamicProp(player.Index);
            if (chickenModel == null)
                return;
            
            chickenModel.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(chickenModel.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            chickenModel.SetModel("models/chicken/chicken.vmdl");
            chickenModel.Render = Color.FromArgb(255, 255, 255, 255);
            chickenModel.Teleport(playerPawn.AbsOrigin, playerPawn.AbsRotation, null);
            chickenModel.DispatchSpawn();
            chickenModel.AcceptInput("InitializeSpawnFromWorld", playerPawn, playerPawn, "");
            Utilities.SetStateChanged(chickenModel, "CBaseEntity", "m_CBodyComponent");

            chickenModel.CBodyComponent.SceneNode.GetSkeletonInstance().Scale = 1;
            Utilities.SetStateChanged(chickenModel, "CBaseEntity", "m_CBodyComponent");
            
            Server.NextFrame(() => {
                if (chickenModel == null || !chickenModel.IsValid) return;
                chickenModel.AcceptInput("SetScale", chickenModel, chickenModel, "1");
            });

            chickenModel.AcceptInput("SetParent", playerPawn, playerPawn, "!activator");
            chickens.TryAdd(player.Index, chickenModel.Index);
        }

        public static void OnTick()
        {
            var pairs = chickens.ToArray();
            foreach (var valuePair in pairs)
            {
                var playerIndex = valuePair.Key;
                var chickenIndex = valuePair.Value;

                var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                if (player == null || !player.IsValid) continue;

                var chicken = Utilities.GetEntityFromIndex<CBaseModelEntity>((int)chickenIndex);
                if (chicken == null || !chicken.IsValid) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || chicken.AbsOrigin == null) continue;

                pawn.VelocityModifier = 1.1f;
                UpdateHUD(player);
            }
        }

        private static void UpdateHUD(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            var pawn = player.PlayerPawn.Value;
            if (pawn.WeaponServices == null || pawn.WeaponServices.ActiveWeapon == null || !pawn.WeaponServices.ActiveWeapon.IsValid || pawn.WeaponServices.ActiveWeapon.Value == null || !pawn.WeaponServices.ActiveWeapon.Value.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            var weapon = pawn.WeaponServices.ActiveWeapon.Value;
            if (weapon == null || !disabledWeapons.Contains(weapon.DesignerName))
            {
                playerInfo.PrintHTML = null;
                return;
            }

            playerInfo.PrintHTML = $"<font color='#FF0000'>{player.GetTranslation("disabled_weapon")}</font>";
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#FF8B42", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}