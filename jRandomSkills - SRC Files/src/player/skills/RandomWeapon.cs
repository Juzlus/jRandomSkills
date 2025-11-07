using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Immutable;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class RandomWeapon : ISkill
    {
        private const Skills skillName = Skills.RandomWeapon;
        private static readonly ConcurrentDictionary<ulong, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly object setLock = new();

        private static readonly string[] pistols = [ "weapon_deagle", "weapon_revolver", "weapon_glock", "weapon_usp_silencer",
        "weapon_cz75a", "weapon_fiveseven", "weapon_p250", "weapon_tec9", "weapon_elite", "weapon_hkp2000" ];

        private static readonly string[] rifles = [ "weapon_mp9", "weapon_mac10", "weapon_bizon", "weapon_mp7", "weapon_ump45", "weapon_p90",
        "weapon_mp5sd", "weapon_famas", "weapon_galilar", "weapon_m4a1", "weapon_m4a1_silencer", "weapon_ak47",
        "weapon_aug", "weapon_sg553", "weapon_ssg08", "weapon_awp", "weapon_scar20", "weapon_g3sg1",
        "weapon_nova", "weapon_xm1014", "weapon_mag7", "weapon_sawedoff", "weapon_m249", "weapon_negev" ];

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
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
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

                if (cooldown == 0 && skillInfo?.CanUse == false)
                    skillInfo.CanUse = true;
            }

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
            {
                if (!player.IsValid || !player.PawnIsAlive) return;
                if (skillInfo.CanUse)
                {
                    skillInfo.CanUse = false;
                    skillInfo.Cooldown = DateTime.Now;
                    RemoveAndGiveWeapon(player);
                }
            }
        }

        private static void RemoveAndGiveWeapon(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            ConcurrentBag<string> playerWeapons = [];
            foreach (var item in pawn.WeaponServices.MyWeapons)
                if (item != null && item.IsValid && item.Value != null && item.Value.IsValid && !string.IsNullOrEmpty(item.Value.DesignerName))
                    playerWeapons.Add(item.Value.DesignerName);

            if (playerWeapons.IsEmpty)
                return;

            ConcurrentBag<string> weaponList = [.. pistols.Concat(rifles).Where(w => !playerWeapons.Contains(w))];

            if (weaponList.Count == 0)
                return;

            string weapon = weaponList.ToArray()[Instance.Random.Next(weaponList.Count)];
            bool isPistol = pistols.Contains(weapon);

            string? weaponToRemove = playerWeapons.FirstOrDefault(itemName =>
                (isPistol && pistols.Contains(itemName)) || (!isPistol && rifles.Contains(itemName)));

            if (!string.IsNullOrEmpty(weaponToRemove))
            {
                foreach (var item in pawn.WeaponServices.MyWeapons)
                {
                    if (item != null && item.IsValid && item.Value != null && item.Value.IsValid && item.Value.DesignerName == weaponToRemove)
                        item.Value.AcceptInput("Kill");
                }
            }

            Instance.AddTimer(.1f, () =>
            {
                player.GiveNamedItem(weapon);
            });
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e0873a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float cooldown = 15f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Cooldown { get; set; } = cooldown;
        }
    }
}