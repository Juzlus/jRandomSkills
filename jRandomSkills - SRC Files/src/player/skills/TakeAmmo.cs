using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class TakeAmmo : ISkill
    {
        private const Skills skillName = Skills.TakeAmmo;
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly object setLock = new();
        private static readonly ConcurrentDictionary<string, (int, string)> OriginalWeaponMaxAmmo = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                SkillPlayerInfo.Clear();
            }

            foreach (var kvp in OriginalWeaponMaxAmmo)
            {
                try
                {
                    var vdata = VirtualFunctions.GetCSWeaponDataFromKey(-1, kvp.Value.Item2);
                    if (vdata == null || vdata.Handle == nint.Zero) continue;
                    vdata.PrimaryReserveAmmoMax = kvp.Value.Item1;
                }
                catch (Exception ex)
                {
                    Server.PrintToConsole($"[jRandomSkills] TakeAmmo could not restore ammo for {kvp.Value.Item2}: {ex.Message}");
                }
            }
            OriginalWeaponMaxAmmo.Clear();
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
                EnemyHaveAmmo = true,
                MaxWeaponAmmo = new ConcurrentDictionary<string, int>()
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (SkillPlayerInfo.TryRemove(player.Index, out var _))
                SkillUtils.ResetPrintHTML(player);

            var weaponServices = player.PlayerPawn?.Value?.WeaponServices;
            if (weaponServices == null) return;

            foreach (var _weapon in weaponServices.MyWeapons.ToArray())
            {
                if (_weapon == null || !_weapon.IsValid) continue;

                var weapon = _weapon?.Value;
                if (weapon == null || string.IsNullOrEmpty(weapon.DesignerName)) continue;

                OriginalWeaponMaxAmmo.TryGetValue(weapon.DesignerName, out var items);

                weapon.ReserveAmmo.Fill(items.Item1);
                Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_pReserveAmmo");
            }
        }

        public static void OnTick()
        {
            var players = PlayerManager.GetTickPlayers().ToArray();
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
                            ? $"<font color='#FF0000'>{player.GetTranslation("takeammo_hud_info1")}</font>"
                            : skillInfo != null && !skillInfo.EnemyHaveAmmo ? $"<font color='#FF0000'>{player.GetTranslation("takeammo_hud_info2")}</font>" : null;
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
                if (!player.IsValid || playerPawn.Health <= 0) return;
                if (skillInfo.CanUse)
                {
                    CCSPlayerController? enemy = GetRandomEnemy(player);
                    if (enemy == null)
                    {
                        skillInfo.FindedEnemy = false;
                        skillInfo.LastClick = DateTime.Now;
                        return;
                    }

                    int playerMagazines = GetMagazinesCount(player);
                    int enemyMagazines = GetMagazinesCount(enemy);

                    if (enemyMagazines == 0)
                    {
                        skillInfo.FindedEnemy = true;
                        skillInfo.EnemyHaveAmmo = false;
                        skillInfo.LastClick = DateTime.Now;
                        return;
                    }

                    skillInfo.EnemyHaveAmmo = true;
                    skillInfo.FindedEnemy = true;
                    skillInfo.CanUse = false;
                    skillInfo.Cooldown = DateTime.Now;

                    Server.NextFrame(() =>
                    {
                        if (player == null || !player.IsValid) return;
                        if (enemy == null || !enemy.IsValid) return;

                        SetMagazines(player, playerMagazines + enemyMagazines);
                        SetMagazines(enemy, 0);

                        PlayerManager.GetPlayerFromEvent(enemy)?.ExecuteClientCommand($"play sounds/weapons/g3sg1/g3sg1_clipout");

                        var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
                        if (enemyEvent == null || !enemyEvent.IsValid) return;
                        enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("takeammo_enemy_info"));
                    });
                }
                else
                    skillInfo.LastClick = DateTime.Now;
            }
        }

        private static int GetMagazinesCount(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.Health <= 0) return 0;

            var ws = pawn.WeaponServices;
            if (ws == null) return 0;

            var activeWeapon = ws.ActiveWeapon?.Value;
            if (activeWeapon == null || !activeWeapon.IsValid || activeWeapon.VData == null || string.IsNullOrEmpty(activeWeapon.DesignerName)) return 0;

            if (activeWeapon.VData.PrimaryAmmoType == 7 && activeWeapon.ReserveAmmo.Length >= 1)
                return (int)(activeWeapon.ReserveAmmo[0] / activeWeapon.VData.MaxClip1);

            int magazines = activeWeapon.VData.ReserveAmmoAsClips && activeWeapon.ReserveAmmo.Length >= 1 ? activeWeapon.ReserveAmmo[0] : 0;
            return magazines;
        }

        private static void SetMagazines(CCSPlayerController player, int magazines)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.Health <= 0) return;

            var ws = pawn.WeaponServices;
            if (ws == null) return;

            var activeWeapon = ws.ActiveWeapon?.Value;
            if (activeWeapon == null || !activeWeapon.IsValid || activeWeapon.VData == null || string.IsNullOrEmpty(activeWeapon.DesignerName)) return;

            if (activeWeapon.VData.PrimaryAmmoType == 7)
                magazines = (int)(magazines * activeWeapon.VData.MaxClip1);

            activeWeapon.ReserveAmmo.Fill(magazines);
            Utilities.SetStateChanged(activeWeapon, "CBasePlayerWeapon", "m_pReserveAmmo");

            string weaponName = activeWeapon.DesignerName;
            string weaponIndex = activeWeapon.AttributeManager.Item.ItemDefinitionIndex.ToString();

            if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
            {
                if (skillInfo.MaxWeaponAmmo.TryGetValue(weaponName, out int currentMax))
                    skillInfo.MaxWeaponAmmo[weaponName] = Math.Max(currentMax, magazines);
                else
                    skillInfo.MaxWeaponAmmo.TryAdd(weaponName, magazines);
            }

            var vdata = VirtualFunctions.GetCSWeaponDataFromKey(-1, weaponIndex);
            if (vdata == null) return;

            if (!OriginalWeaponMaxAmmo.ContainsKey(weaponName))
                OriginalWeaponMaxAmmo.TryAdd(weaponName, (vdata.PrimaryReserveAmmoMax, weaponIndex));

            vdata.PrimaryReserveAmmoMax = Math.Max(vdata.PrimaryReserveAmmoMax, magazines);
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            string weaponName = @event.Item;
            if (string.IsNullOrEmpty(weaponName)) return;

            if (OriginalWeaponMaxAmmo.TryGetValue("weapon_" + weaponName, out var items))
            {
                var pawn = player.PlayerPawn?.Value;
                if (pawn == null) return;

                var ws = pawn.WeaponServices;
                if (ws == null) return;

                var activeWeapon = ws.ActiveWeapon?.Value;
                if (activeWeapon == null || !activeWeapon.IsValid || string.IsNullOrEmpty(activeWeapon.DesignerName)) return;

                int ammoLimit = items.Item1;

                if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
                {
                    if (skillInfo.MaxWeaponAmmo.TryGetValue("weapon_" + weaponName, out int personalMax))
                        ammoLimit = Math.Max(ammoLimit, personalMax);
                }

                if (activeWeapon.ReserveAmmo.Length > 0 && activeWeapon.ReserveAmmo[0] > ammoLimit)
                {
                    activeWeapon.ReserveAmmo.Fill(ammoLimit);
                    Utilities.SetStateChanged(activeWeapon, "CBasePlayerWeapon", "m_pReserveAmmo");
                }
            }
        }

        private static CCSPlayerController? GetRandomEnemy(CCSPlayerController player)
        {
            CCSPlayerController[] enemies = [.. PlayerManager.GetTickPlayers().FindAll(e =>
                e.Team != player.Team &&
                e.PlayerPawn?.Value?.Health > 0 &&
                e.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon?.Value?.ReserveAmmo.Length > 0
            )];
            if (enemies.Length == 0) return null;
            return enemies[Instance.Random.Next(enemies.Length)];
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
            public DateTime LastClick { get; set; }
            public bool FindedEnemy { get; set; }
            public bool EnemyHaveAmmo { get; set; }
            public ConcurrentDictionary<string, int> MaxWeaponAmmo { get; set; } = new();
        }

        public class WeaponInfo
        {
            public required string Name { get; set; }
            public required int Clip1 { get; set; }
            public required int Clip2 { get; set; }
            public required int Reserve { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5eb8b0", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float cooldown = 30f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
        }
    }
}