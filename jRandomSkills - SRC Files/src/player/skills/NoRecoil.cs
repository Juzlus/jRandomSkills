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

        private sealed class SpreadPatch(CCSWeaponBaseVData vdata)
        {
            public readonly CCSWeaponBaseVData VData = vdata;
            public readonly CFiringModeFloat[] Fields =
            [
                vdata.Spread, vdata.InaccuracyCrouch, vdata.InaccuracyStand, vdata.InaccuracyJump,
                vdata.InaccuracyLand, vdata.InaccuracyLadder, vdata.InaccuracyFire, vdata.InaccuracyMove
            ];
            public readonly float[] Values = new float[16];
            public int Count;
            public float JumpInitial;
            public float JumpApex;
            public float Reload;
            public bool Active;
        }

        private static readonly Dictionary<nint, SpreadPatch> spreadPatches = [];
        private static readonly List<SpreadPatch> activePatches = [];
        private static readonly Dictionary<nint, CCSWeaponBaseVData> wantedVData = [];
        private static readonly HashSet<nint> blockedVData = [];

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

        public static void OnTick()
        {
            if (holders.IsEmpty) return;

            wantedVData.Clear();
            blockedVData.Clear();

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid || !player.PawnIsAlive) continue;

                var vdata = GetActiveWeaponVData(player);
                if (vdata == null) continue;

                bool isHolder = holders.ContainsKey(player.Index) && PlayerManager.GetPlayerByIndex(player.Index)?.Skill == skillName;
                if (isHolder)
                    wantedVData[vdata.Handle] = vdata;
                else
                    blockedVData.Add(vdata.Handle);
            }

            foreach (var (handle, vdata) in wantedVData)
            {
                if (!blockedVData.Contains(handle))
                    PatchSpread(vdata);
            }
        }

        private static CCSWeaponBaseVData? GetActiveWeaponVData(CCSPlayerController player)
        {
            var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
            if (weapon == null || !weapon.IsValid) return null;

            var vdata = weapon.As<CCSWeaponBase>().VData;
            return vdata == null || vdata.Handle == IntPtr.Zero ? null : vdata;
        }

        private static void PatchSpread(CCSWeaponBaseVData vdata)
        {
            if (!spreadPatches.TryGetValue(vdata.Handle, out var patch))
            {
                patch = new SpreadPatch(vdata);
                spreadPatches[vdata.Handle] = patch;
            }

            if (patch.Active) return;

            int index = 0;
            foreach (var field in patch.Fields)
            {
                var values = field.Values;
                for (int i = 0; i < values.Length && index < patch.Values.Length; i++)
                {
                    patch.Values[index++] = values[i];
                    values[i] = 0f;
                }
            }

            patch.Count = index;
            patch.JumpInitial = vdata.InaccuracyJumpInitial;
            patch.JumpApex = vdata.InaccuracyJumpApex;
            patch.Reload = vdata.InaccuracyReload;

            vdata.InaccuracyJumpInitial = 0f;
            vdata.InaccuracyJumpApex = 0f;
            vdata.InaccuracyReload = 0f;

            patch.Active = true;
            activePatches.Add(patch);
        }

        public static void RestoreSpread()
        {
            if (activePatches.Count == 0) return;

            foreach (var patch in activePatches)
            {
                int index = 0;
                foreach (var field in patch.Fields)
                {
                    var values = field.Values;
                    for (int i = 0; i < values.Length && index < patch.Count; i++)
                        values[i] = patch.Values[index++];
                }

                patch.VData.InaccuracyJumpInitial = patch.JumpInitial;
                patch.VData.InaccuracyJumpApex = patch.JumpApex;
                patch.VData.InaccuracyReload = patch.Reload;
                patch.Active = false;
            }

            activePatches.Clear();
        }

        public static void ForgetSpread()
        {
            activePatches.Clear();
            spreadPatches.Clear();
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