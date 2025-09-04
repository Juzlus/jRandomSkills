﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.player;
using static jRandomSkills.jRandomSkills;

namespace jRandomSkills
{
    public class FastReload : ISkill
    {
        private const Skills skillName = Skills.FastReload;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, Config.GetValue<string>(skillName, "color"));
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;
            if (!player.IsValid || !player.PawnIsAlive) return;

            InstaReload(playerPawn);
        }

        private static void InstaReload(CCSPlayerPawn pawn)
        {
            if (pawn == null || !pawn.IsValid) return;
            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null || weaponServices.ActiveWeapon == null || !weaponServices.ActiveWeapon.IsValid) return;

            var activeWeapon = weaponServices.ActiveWeapon.Value;
            if (activeWeapon == null || !activeWeapon.IsValid || activeWeapon.VData == null) return;

            activeWeapon.Clip1 = activeWeapon.VData.MaxClip1;
            Utilities.SetStateChanged(activeWeapon, "CBasePlayerWeapon", "m_iClip1");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffc061", CsTeam onlyTeam = CsTeam.None, bool needsTeammates = false) : Config.DefaultSkillInfo(skill, active, color, onlyTeam, needsTeammates)
        {
        }
    }
}