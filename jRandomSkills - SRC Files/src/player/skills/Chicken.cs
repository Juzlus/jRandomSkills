using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Chicken : ISkill
    {
        private const Skills skillName = Skills.Chicken;
        private static readonly string[] disabledWeapons =
        [
            "weapon_ak47", "weapon_m4a4", "weapon_m4a1", "weapon_m4a1_silencer",
            "weapon_famas", "weapon_galilar", "weapon_aug", "weapon_sg553", 
            "weapon_mp9", "weapon_mac10", "weapon_bizon", "weapon_mp7",
            "weapon_ump45", "weapon_p90", "weapon_mp5sd", "weapon_ssg08",
            "weapon_awp", "weapon_scar20", "weapon_g3sg1", "weapon_nova",
            "weapon_xm1014", "weapon_mag7", "weapon_sawedoff", "weapon_m249",
            "weapon_negev"
        ];
        private static readonly ConcurrentDictionary<CCSPlayerController, CBaseModelEntity> chickens = [];
        private static readonly string defaultCTModel = "characters/models/ctm_sas/ctm_sas.vmdl";
        private static readonly string defaultTModel = "characters/models/tm_phoenix/tm_phoenix.vmdl";
        private static readonly ConcurrentDictionary<ulong, string> originalModels = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
                SetWeaponAttack(player, false);
            foreach (var valuePair in chickens)
                if (valuePair.Value != null && valuePair.Value.IsValid)
                    valuePair.Value.AcceptInput("Kill");
            chickens.Clear();
            originalModels.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
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

                if (playerPawn.CBodyComponent != null && playerPawn.CBodyComponent.SceneNode != null)
                    originalModels.TryAdd(player.SteamID, playerPawn.CBodyComponent.SceneNode.GetSkeletonInstance().ModelState.ModelName);

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

                playerPawn.Health = Math.Min(playerPawn.Health + 50, 100);
                Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");

                SkillUtils.ChangePlayerScale(player, 1);

                playerPawn.Render = Color.FromArgb(255, 255, 255, 255);
                playerPawn.ShadowStrength = 1.0f;
                Utilities.SetStateChanged(playerPawn, "CBaseModelEntity", "m_clrRender");

                SetWeaponAttack(player, false);
            }

            if (chickens.TryGetValue(player, out var chicken))
            {
                if (chicken != null && chicken.IsValid)
                    chicken.AcceptInput("Kill");
                chickens.TryRemove(player, out _);
            }

            if (originalModels.TryGetValue(player.SteamID, out var model))
            {
                var pawn = player.PlayerPawn?.Value;
                if (pawn == null) return;

                Server.NextFrame(() =>
                {
                    if (string.IsNullOrEmpty(model))
                        model = player.Team == CsTeam.Terrorist ? defaultTModel : defaultCTModel;
                    
                    pawn.SetModel(model);
                    var originalRender = pawn.Render;
                    pawn.Render = Color.FromArgb(255, originalRender.R, originalRender.G, originalRender.B);
                    originalModels.TryRemove(player.SteamID, out _);
                });
            }
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
            var chickenModel = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
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
            Server.NextFrame(() => chickenModel.AcceptInput("SetScale", chickenModel, chickenModel, "1"));

            chickenModel.AcceptInput("SetParent", playerPawn, playerPawn, "!activator");
            chickens.TryAdd(player, chickenModel);
        }

        public static void OnTick()
        {
            foreach (var valuePair in chickens)
            {
                var player = valuePair.Key;
                var chicken = valuePair.Value;
                if (player == null || !player.IsValid) continue;
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

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            var weapon = pawn.WeaponServices.ActiveWeapon.Value;
            if (weapon == null || !disabledWeapons.Contains(weapon.DesignerName))
            {
                playerInfo.PrintHTML = null;
                return;
            }

            playerInfo.PrintHTML = $"<font color='#FF0000'>{player.GetTranslation("disabled_weapon")}</font>";
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#FF8B42", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}