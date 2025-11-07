using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class SniperElite : ISkill
    {
        private const Skills skillName = Skills.SniperElite;
        private static readonly ConcurrentDictionary<ulong, List<CEntityInstance>> playerAWPs = [];
        private static readonly ConcurrentDictionary<ulong, string> sniperElites = [];
        private static readonly object setLock = new();

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
            {
                sniperElites.Clear();
                playerAWPs.Clear();
            }
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill == skillName)
                DisableSkill(player);
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill == skillName)
                DeleteDroppedAWP(player);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            sniperElites.TryAdd(player.SteamID, "weapon_awp");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            sniperElites.TryRemove(player.SteamID, out _);
            playerAWPs.TryRemove(player.SteamID, out _);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (sniperElites.ContainsKey(player.SteamID))
                RemoveAndGiveWeapon(player);
        }

        private static void RemoveAndGiveWeapon(CCSPlayerController player)
        {
            string holdedWeapon = "weapon_awp";
            if (sniperElites.TryGetValue(player.SteamID, out string? weapon))
                if (!string.IsNullOrEmpty(weapon))
                    holdedWeapon = weapon;

            try
            {
                CBasePlayerWeapon? activeRifle = GetActiveRifle(player);
                if (activeRifle != null && activeRifle.IsValid)
                    RemoveWeapon(player, activeRifle.DesignerName);

                sniperElites.TryAdd(player.SteamID, (activeRifle != null && activeRifle.IsValid) ? SkillUtils.GetDesignerName(activeRifle) : "weapon_awp");
                Server.NextFrame(() => {
                    string weapon = holdedWeapon ?? "weapon_awp";
                    if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
                    var createdWeapon = player.PlayerPawn.Value?.ItemServices?.As<CCSPlayer_ItemServices>().GiveNamedItem<CEntityInstance>(weapon);

                    if (createdWeapon != null && createdWeapon.IsValid && createdWeapon.DesignerName == "weapon_awp")
                    {
                        lock (setLock)
                        {
                            if (playerAWPs.TryGetValue(player.SteamID, out var list))
                                list.Add(createdWeapon);
                            else
                                playerAWPs.TryAdd(player.SteamID, [createdWeapon]);
                        }
                    }
                    DeleteDroppedAWP(player);
                });
            }
            catch { }
        }

        private static void DeleteDroppedAWP(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;
            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null) return;

            ConcurrentBag<uint> playerWeapons = [.. weaponServices.MyWeapons.Where(w => w != null && w.IsValid && w.Value != null && w.Value.IsValid)
                                                                .Select(w => w.Value!.Index)];

            lock (setLock)
            {
                if (playerAWPs.TryGetValue(player.SteamID, out var AWPs))
                    foreach (var awp in AWPs.ToList())
                    {
                        if (awp != null && awp.IsValid)
                        {
                            if (!playerWeapons.Contains(awp.Index))
                            {
                                awp.AcceptInput("Kill");
                                AWPs.Remove(awp);
                            }
                        }
                        else
                            AWPs.Remove(awp!);
                    }
            }
        }

        private static CBasePlayerWeapon? GetActiveRifle(CCSPlayerController player)
        {
            CBasePlayerWeapon? rifle = null;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return rifle;
            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null) return rifle;

            foreach (var weapon in weaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
                    if (rifles.Contains(weapon.Value.DesignerName))
                        rifle = weapon.Value;
            return rifle;
        }
        
        private static void RemoveWeapon(CCSPlayerController player, string designerName)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            foreach (var item in pawn.WeaponServices.MyWeapons)
                if (item != null && item.IsValid && item.Value != null && item.Value.IsValid && item.Value.DesignerName == designerName)
                    item.Value.AcceptInput("Kill");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e0873a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}