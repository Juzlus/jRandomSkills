using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class RandomWeapon : ISkill
    {
        private const Skills skillName = Skills.RandomWeapon;
        private static readonly ConcurrentDictionary<ulong, PlayerSkillInfo> SkillPlayerInfo = new();
        private static readonly object setLock = new();

        private static readonly HashSet<string> pistols = new(StringComparer.Ordinal)
        {
            "weapon_deagle", "weapon_revolver", "weapon_glock", "weapon_usp_silencer",
            "weapon_cz75a", "weapon_fiveseven", "weapon_p250", "weapon_tec9", "weapon_elite", "weapon_hkp2000"
        };

        private static readonly HashSet<string> rifles = new(StringComparer.Ordinal)
        {
            "weapon_mp9", "weapon_mac10", "weapon_bizon", "weapon_mp7", "weapon_ump45", "weapon_p90",
            "weapon_mp5sd", "weapon_famas", "weapon_galilar", "weapon_m4a1", "weapon_m4a1_silencer", "weapon_ak47",
            "weapon_aug", "weapon_sg553", "weapon_ssg08", "weapon_awp", "weapon_scar20", "weapon_g3sg1",
            "weapon_nova", "weapon_xm1014", "weapon_mag7", "weapon_sawedoff", "weapon_m249", "weapon_negev"
        };

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
                SkillPlayerInfo.Clear();
        }

        public static void OnTick()
        {
            var players = Utilities.GetPlayers().ToArray();
            foreach (var player in players)
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName && SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
                    UpdateHUD(player, skillInfo);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.SteamID, new PlayerSkillInfo
            {
                SteamID = player.SteamID,
                CanUse = true,
                Cooldown = DateTime.MinValue,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryRemove(player.SteamID, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            float cooldown = 0;
            if (skillInfo != null)
            {
                float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
                cooldown = Math.Max(time, 0);

                if (cooldown == 0 && skillInfo.CanUse == false)
                    skillInfo.CanUse = true;
            }

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            playerInfo.PrintHTML = cooldown == 0
                ? null
                : $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn?.CBodyComponent == null) return;

            if (!SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo)) return;
            if (!player.IsValid || !player.PawnIsAlive) return;

            if (!skillInfo.CanUse)
            {
                return;
            }

            skillInfo.CanUse = false;
            skillInfo.Cooldown = DateTime.Now;
            RemoveAndGiveWeapon(player);
        }

        private static void RemoveAndGiveWeapon(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            var playerWeapons = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in pawn.WeaponServices.MyWeapons)
                if (item != null && item.IsValid && item.Value != null && item.Value.IsValid && !string.IsNullOrEmpty(item.Value.DesignerName))
                    playerWeapons.Add(item.Value.DesignerName);

            if (playerWeapons.Count == 0)
                return;

            var available = pistols.Concat(rifles).Where(w => !playerWeapons.Contains(w)).ToArray();
            if (available.Length == 0)
                return;

            string newWeapon = available[Instance.Random.Next(available.Length)];
            bool isPistol = pistols.Contains(newWeapon);

            string? weaponToRemove = null;
            foreach (var item in pawn.WeaponServices.MyWeapons)
            {
                if (item == null || !item.IsValid || item.Value == null || !item.Value.IsValid) continue;
                var name = item.Value.DesignerName;
                if (string.IsNullOrEmpty(name)) continue;
                if ((isPistol && pistols.Contains(name)) || (!isPistol && rifles.Contains(name)))
                {
                    weaponToRemove = name;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(weaponToRemove))
            {
                foreach (var item in pawn.WeaponServices.MyWeapons)
                {
                    if (item == null || !item.IsValid || item.Value == null || !item.Value.IsValid) continue;
                    if (item.Value.DesignerName == weaponToRemove)
                        SkillUtils.SafeKillEntity<CBasePlayerWeapon>((uint)item.Value.Index);
                }
            }

            ulong steamId = player.SteamID;
            Instance.AddTimer(.1f, () =>
            {
                var pl = Utilities.GetPlayerFromSteamId(steamId);
                if (pl == null || !pl.IsValid) return;
                pl.GiveNamedItem(newWeapon);
            });
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e0873a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float cooldown = 15f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
        }
    }
}