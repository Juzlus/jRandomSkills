using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class SniperElite : ISkill
    {
        private const Skills skillName = Skills.SniperElite;
        private static readonly ConcurrentDictionary<ulong, List<uint>> playerAWPIndexes = [];
        private static readonly ConcurrentDictionary<ulong, string> savedWeapons = [];
        private static readonly ConcurrentDictionary<ulong, byte> isProcessing = [];
        private static readonly object setLock = new();

        private const string weapon_awp = "weapon_awp";
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
                savedWeapons.Clear();
                playerAWPIndexes.Clear();
                isProcessing.Clear();
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
            {
                if (rifles.Contains(@event.Item) && @event.Item != weapon_awp)
                    savedWeapons.AddOrUpdate(player.SteamID, string.Empty, (_, _) => string.Empty);
                DeleteDroppedAWP(player);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            savedWeapons.TryAdd(player.SteamID, string.Empty);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            player.ExecuteClientCommand("slot3");

            if (playerAWPIndexes.TryGetValue(player.SteamID, out var AWPs))
                foreach (var index in AWPs.ToList())
                {
                    var ent = Utilities.GetEntityFromIndex<CBaseEntity>((int)index);
                    if (ent != null && ent.IsValid)
                        ent.AcceptInput("Kill");
                }

            if (savedWeapons.TryGetValue(player.SteamID, out string? savedWeapon) && !string.IsNullOrWhiteSpace(savedWeapon))    
                Server.NextFrame(() => 
                    player.PlayerPawn.Value?.ItemServices?.As<CCSPlayer_ItemServices>().GiveNamedItem<CEntityInstance>(savedWeapon));

            Server.NextFrame(() => player.ExecuteClientCommand("lastinv"));

            savedWeapons.TryRemove(player.SteamID, out _);
            playerAWPIndexes.TryRemove(player.SteamID, out _);
            isProcessing.TryRemove(player.SteamID, out _);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid || isProcessing.ContainsKey(player.SteamID)) return;
            if (savedWeapons.ContainsKey(player.SteamID))
                RemoveAndGiveWeapon(player);
        }

        private static void RemoveAndGiveWeapon(CCSPlayerController player)
        {
            ulong steamID = player.SteamID;
            try
            {
                if (!isProcessing.TryAdd(steamID, 0)) return;

                CBasePlayerWeapon? activeRifle = GetActiveRifle(player);
                string weaponToGive = weapon_awp;

                if (activeRifle != null && activeRifle.IsValid)
                {
                    string currentWeaponName = SkillUtils.GetDesignerName(activeRifle);
                    bool isScriptAWP = false;

                    lock (setLock)
                        isScriptAWP = playerAWPIndexes.TryGetValue(steamID, out var indexes) && indexes.Contains(activeRifle.Index);

                    if (isScriptAWP)
                    {
                        if (savedWeapons.TryGetValue(steamID, out var savedWeapon) && !string.IsNullOrEmpty(savedWeapon))
                        {
                            weaponToGive = savedWeapon;
                            savedWeapons.AddOrUpdate(steamID, string.Empty, (_, _) => string.Empty);
                        }
                        else
                        {
                            isProcessing.TryRemove(steamID, out _);
                            return;
                        }
                    }
                    else
                    {
                        savedWeapons.AddOrUpdate(steamID, currentWeaponName, (_, _) => currentWeaponName);
                        weaponToGive = weapon_awp + "_script";
                    }

                    activeRifle.AcceptInput("Kill");
                }
                else
                {
                    if (savedWeapons.TryGetValue(steamID, out string? savedWeapon) && !string.IsNullOrEmpty(savedWeapon))
                    {
                        weaponToGive = savedWeapon;
                        savedWeapons.AddOrUpdate(steamID, string.Empty, (_, _) => string.Empty);
                    }
                    else
                        weaponToGive = weapon_awp + "_script";
                }

                Server.NextFrame(() =>
                {
                    if (player != null && player.IsValid && player.PlayerPawn.Value != null && player.PlayerPawn.Value.IsValid)
                    {
                        var createdWeapon = player.PlayerPawn.Value?.ItemServices?.As<CCSPlayer_ItemServices>().GiveNamedItem<CEntityInstance>(weaponToGive.Replace("_script", ""));

                        if (createdWeapon != null && createdWeapon.IsValid && weaponToGive.EndsWith("_script"))
                        {
                            CBasePlayerWeapon weapon = createdWeapon.As<CBasePlayerWeapon>();
                            if (weapon?.AttributeManager.Item.NetworkedDynamicAttributes != null)
                                weapon.AttributeManager.Item.CustomName = player.GetTranslation("sniperelite_customname");

                            lock (setLock)
                            {
                                if (!playerAWPIndexes.ContainsKey(steamID))
                                    playerAWPIndexes.TryAdd(steamID, []);
                                
                                if (playerAWPIndexes.TryGetValue(steamID, out var list))
                                    list.Add(createdWeapon.Index);
                            }
                        }
                    }

                    DeleteDroppedAWP(player);
                    isProcessing.TryRemove(steamID, out _);
                });
            }
            catch {
                isProcessing.TryRemove(steamID, out _);
            }
        }

        private static void DeleteDroppedAWP(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            var myWeapons = pawn.WeaponServices.MyWeapons;
            ConcurrentBag<uint> myWeaponsIndexes = [.. myWeapons
                .Where(w => w != null && w.IsValid && w.Value != null && w.Value.IsValid)
                .Select(w => w.Value!.Index)];

            lock (setLock)
            {
                if (playerAWPIndexes.TryGetValue(player.SteamID, out var AWPs))
                    foreach (var index in AWPs.ToList())
                        if (!myWeaponsIndexes.Contains(index))
                        {
                            var ent = Utilities.GetEntityFromIndex<CBaseEntity>((int)index);
                            if (ent != null && ent.IsValid)
                                ent.AcceptInput("Kill");

                            AWPs.Remove(index);
                        }
            }
        }

        private static CBasePlayerWeapon? GetActiveRifle(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return null;
         
            var activeWeapon = pawn.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon != null && activeWeapon.IsValid && rifles.Contains(activeWeapon.DesignerName))
                return activeWeapon;
            return null;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e0873a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}