using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class WeaponsSwap : ISkill
    {
        private const Skills skillName = Skills.WeaponsSwap;
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly object setLock = new();

        private static readonly HashSet<string> weapons = [ "weapon_deagle", "weapon_revolver", "weapon_glock", "weapon_usp_silencer",
        "weapon_cz75a", "weapon_fiveseven", "weapon_p250", "weapon_tec9", "weapon_elite", "weapon_hkp2000",
        "weapon_mp9", "weapon_mac10", "weapon_bizon", "weapon_mp7", "weapon_ump45", "weapon_p90", "weapon_sg556",
        "weapon_mp5sd", "weapon_famas", "weapon_galilar", "weapon_m4a4", "weapon_m4a1_silencer", "weapon_ak47",
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

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.Index, new PlayerSkillInfo
            {
                SteamID = player.Index,
                CanUse = true,
                Cooldown = DateTime.MinValue,
                LastClick = DateTime.MinValue,
                FindedEnemy = true,
                HaveWeapon = true,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void OnTick()
        {
            var players = Utilities.GetPlayers().ToArray();
            foreach (var player in players)
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill != skillName) continue;

                if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
                {
                    bool recentClick = skillInfo.LastClick.AddSeconds(4) >= DateTime.Now;
                    UpdateHUD(player, skillInfo, recentClick);
                }
            }
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo, bool showInfo)
        {
            float cooldown = 0;
            if (skillInfo != null)
            {
                float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
                cooldown = Math.Max(time, 0);

                if (cooldown == 0 && skillInfo?.CanUse == false)
                    skillInfo.CanUse = true;
            }

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            if (cooldown == 0)
            {
                if (showInfo)
                    playerInfo.PrintHTML =
                        skillInfo != null && !skillInfo.FindedEnemy
                            ? $"<font color='#FF0000'>{player.GetTranslation("hud_info_no_enemy")}</font>"
                            : skillInfo != null && !skillInfo.HaveWeapon ? $"<font color='#FF0000'>{player.GetTranslation("weaponsswap_hud_info2")}</font>" : null;
                else
                    SkillUtils.ResetPrintHTML(player);
                return;
            }

            playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
            {
                if (!player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
                if (skillInfo.CanUse)
                {
                    CCSPlayerController? enemy = GetRandomEnemy(player);
                    if (enemy == null)
                    {
                        skillInfo.FindedEnemy = false;
                        skillInfo.LastClick = DateTime.Now;
                        return;
                    }

                    (WeaponInfo[]? playerWeapon, bool playerC4) = GetWeapons(player);
                    (WeaponInfo[]? enemyWeapon, bool enemyC4) = GetWeapons(enemy);

                    if (playerWeapon == null || !playerWeapon.Any(w => weapons.Contains(w.Name)))
                    {
                        skillInfo.FindedEnemy = true;
                        skillInfo.HaveWeapon = false;
                        skillInfo.LastClick = DateTime.Now;
                        return;
                    }

                    skillInfo.HaveWeapon = true;
                    skillInfo.FindedEnemy = true;
                    skillInfo.CanUse = false;
                    skillInfo.Cooldown = DateTime.Now;

                    RemoveC4(player);
                    RemoveC4(enemy);

                    Server.NextFrame(() =>
                    {
                        if (player == null || !player.IsValid) return;
                        if (enemy == null || !enemy.IsValid) return;
                        player.RemoveWeapons();
                        enemy.RemoveWeapons();
                        GiveWeapons(player, enemyWeapon, playerC4);
                        GiveWeapons(enemy, playerWeapon, (enemyWeapon != null && enemyC4));
                    });
                }
                else
                    skillInfo.LastClick = DateTime.Now;
            }
        }

        private static (WeaponInfo[]?, bool) GetWeapons(CCSPlayerController player)
        {
            bool haveC4 = false;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return (null, haveC4);
            
            if (pawn.WeaponServices == null) return (null, haveC4);

            var list = new List<WeaponInfo>();
            foreach (var weapon in pawn.WeaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid && !string.IsNullOrEmpty(weapon.Value.DesignerName))
                {
                    if (weapon.Value.DesignerName == "weapon_c4")
                        haveC4 = true;
                    else
                    {
                        list.Add(
                            new WeaponInfo
                            {
                                Name = SkillUtils.GetDesignerName(weapon.Value),
                                Clip1 = weapon.Value.Clip1,
                                Clip2 = weapon.Value.Clip2,
                                Reserve = weapon.Value.ReserveAmmo.Length >= 1 ? weapon.Value.ReserveAmmo[0] : 0,
                            });
                    }
                }

            return (list.Count == 0 ? null : [.. list], haveC4);
        }

        private static void GiveWeapons(CCSPlayerController player, WeaponInfo[]? weapons, bool addC4)
        {
            if (weapons == null) return;
            uint index = player.Index;

            foreach (var weapon in weapons)
                player.GiveNamedItem(weapon.Name);
                
            if (addC4)
                player.GiveNamedItem("weapon_c4");

            Server.NextFrame(() =>
            {
                var player = Utilities.GetPlayerFromIndex((int)index);
                if (player == null || !player.IsValid) return;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) return;

                if (pawn.WeaponServices == null) return;

                foreach (var item in pawn.WeaponServices.MyWeapons)
                    if (item != null && item.IsValid)
                    {
                        var weapon = item.Value;
                        if (weapon == null || !weapon.IsValid || string.IsNullOrEmpty(weapon.DesignerName)) continue;

                        var enemyWeapon = weapons.FirstOrDefault(w => w.Name == SkillUtils.GetDesignerName(weapon));
                        if (enemyWeapon == null) continue;

                        weapon.Clip1 = enemyWeapon.Clip1;
                        weapon.Clip2 = enemyWeapon.Clip2;
                        weapon.ReserveAmmo.Fill(enemyWeapon.Reserve);

                        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
                        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip2");
                        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_pReserveAmmo");
                    }
            });
        }

        private static CCSPlayerController? GetRandomEnemy(CCSPlayerController player)
        {
            CCSPlayerController[] enemies = [.. Utilities.GetPlayers().FindAll(e => e.Team != player.Team && e.PawnIsAlive)];
            if (enemies.Length == 0) return null;
            return enemies[Instance.Random.Next(enemies.Length)];
        }

        private static void RemoveC4(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            foreach (var item in pawn.WeaponServices.MyWeapons)
                if (item != null && item.IsValid && item.Value != null && item.Value.IsValid && item.Value.DesignerName == "weapon_c4")
                    SkillUtils.SafeKillEntity<CBasePlayerWeapon>((uint)item.Value.Index);
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
            public DateTime LastClick { get; set; }
            public bool FindedEnemy { get; set; }
            public bool HaveWeapon { get; set; }
        }

        public class WeaponInfo
        {
            public required string Name { get; set; }
            public required int Clip1 { get; set; }
            public required int Clip2 { get; set; }
            public required int Reserve { get; set; }

        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#c7e03a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float cooldown = 30f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
        }
    }
}