using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class FastReload : ISkill
    {
        private const Skills skillName = Skills.FastReload;
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            SkillPlayerInfo.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            SkillPlayerInfo[player.Index] = new PlayerSkillInfo
            {
                SteamID = player.Index,
                CanUse = true,
                Cooldown = DateTime.MinValue,
            };
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            SkillPlayerInfo.TryRemove(playerIndex, out _);
        }

        public static void OnTick()
        {
            if (SkillPlayerInfo.IsEmpty) return;
            if (!SkillUtils.IsHudFrame()) return;
            if (SkillUtils.IsFreezeTime()) return;

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill != skillName) continue;
                if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) continue;

                UpdateHUD(player, skillInfo);
            }
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
            float cooldown = Math.Max(time, 0);

            if (cooldown == 0 && !skillInfo.CanUse)
                skillInfo.CanUse = true;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            playerInfo.PrintHTML = cooldown != 0
                ? $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}"
                : null;
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;
            if (!player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) return;
            if (!skillInfo.CanUse) return;
            if (skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) > DateTime.Now) return;

            if (!InstaReload(playerPawn)) return;

            skillInfo.CanUse = false;
            skillInfo.Cooldown = DateTime.Now;
        }

        private static bool InstaReload(CCSPlayerPawn pawn)
        {
            if (pawn == null || !pawn.IsValid) return false;
            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null || weaponServices.ActiveWeapon == null || !weaponServices.ActiveWeapon.IsValid) return false;

            var activeWeapon = weaponServices.ActiveWeapon.Value;
            if (activeWeapon == null || !activeWeapon.IsValid || activeWeapon.VData == null) return false;

            activeWeapon.Clip1 = activeWeapon.VData.MaxClip1;
            Utilities.SetStateChanged(activeWeapon, "CBasePlayerWeapon", "m_iClip1");
            return true;
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffc061", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float cooldown = 5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
        }
    }
}
