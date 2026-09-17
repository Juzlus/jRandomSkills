using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class NoRecoil : ISkill
    {
        private const Skills skillName = Skills.NoRecoil;

        private const string NoSpreadConVar = "weapon_accuracy_nospread";

        private static readonly ConcurrentDictionary<uint, byte> holders = [];
        private static readonly ConcurrentDictionary<uint, string> originalNoSpreadValues = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid) continue;
                if (!holders.ContainsKey(player.Index)) continue;
                RestoreClientNoSpread(player);
            }

            holders.Clear();
            originalNoSpreadValues.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (!holders.TryAdd(player.Index, 0)) return;

            try
            {
                var currentValue = player.GetConVarValue(NoSpreadConVar);
                originalNoSpreadValues[player.Index] = string.IsNullOrWhiteSpace(currentValue) ? "0" : currentValue;
            }
            catch
            {
                originalNoSpreadValues[player.Index] = "0";
            }

            try
            {
                player.ReplicateConVar(NoSpreadConVar, "1");
            }
            catch { }

            ResetWeaponState(player);
            ResetViewPunch(player);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            holders.TryRemove(player.Index, out _);

            RestoreClientNoSpread(player);
            ResetWeaponState(player);
            ResetViewPunch(player);
        }

        private static void RestoreClientNoSpread(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            var value = originalNoSpreadValues.TryGetValue(player.Index, out var originalValue) 
                ? originalValue : "0";

            try {
                player.ReplicateConVar(NoSpreadConVar, value);
                originalNoSpreadValues.TryRemove(player.Index, out _);
            }
            catch { }
        }

        public static void WeaponFire(EventWeaponFire @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;
            if (!holders.ContainsKey(player.Index)) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo?.Skill != skillName) return;

            ResetWeaponState(player);
            ResetViewPunch(player);
        }

        private static void ResetWeaponState(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            if (pawn.ShotsFired != 0)
            {
                pawn.ShotsFired = 0;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_iShotsFired");
            }

            var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
            if (weapon == null || !weapon.IsValid) return;

            var csWeapon = weapon.As<CCSWeaponBase>();
            if (csWeapon == null || !csWeapon.IsValid) return;

            if (csWeapon.AccuracyPenalty != 0f)
            {
                csWeapon.AccuracyPenalty = 0f;
                Utilities.SetStateChanged(weapon, "CCSWeaponBase", "m_fAccuracyPenalty");
            }

            if (csWeapon.FlRecoilIndex != 0f)
            {
                csWeapon.FlRecoilIndex = 0f;
                Utilities.SetStateChanged(weapon, "CCSWeaponBase", "m_flRecoilIndex");
            }

            if (csWeapon.IRecoilIndex != 0)
            {
                csWeapon.IRecoilIndex = 0;
                Utilities.SetStateChanged(weapon, "CCSWeaponBase", "m_iRecoilIndex");
            }

            if (csWeapon.TurningInaccuracy != 0f)
            {
                csWeapon.TurningInaccuracy = 0f;
                Utilities.SetStateChanged(weapon, "CCSWeaponBase", "m_flTurningInaccuracy");
            }
        }

        private static void ResetViewPunch(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            var aim = pawn.AimPunchServices;
            if (aim != null)
            {
                Zero(aim.PredictableBaseAngle);
                Zero(aim.PredictableBaseAngleVel);
                Zero(aim.UnpredictableBaseAngle);
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_pAimPunchServices");
            }

            var camera = pawn.CameraServices;
            if (camera != null)
            {
                Zero(camera.CsViewPunchAngle);
                Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
            }
        }

        private static void Zero(QAngle? angle)
        {
            if (angle == null) return;
            angle.X = 0f;
            angle.Y = 0f;
            angle.Z = 0f;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8a42f5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}