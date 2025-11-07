using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Ninja : ISkill
    {
        private const Skills skillName = Skills.Ninja;
        private static readonly ConcurrentDictionary<nint, float> invisibilityChanged = [];
        private static readonly ConcurrentDictionary<ulong, ConcurrentBag<uint>> invisibleEntities = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
                invisibilityChanged.Clear();
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);

            if (playerInfo?.Skill != skillName) return;
            UpdateNinja(player);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);

            if (playerInfo?.Skill != skillName) return;
            UpdateNinja(player);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var (info, player) in infoList)
            {
                if (player == null) continue;
                foreach ((var playerId, var itemList) in invisibleEntities)
                    if (player.SteamID != playerId)
                        foreach (var entityIndex in itemList)
                        {
                            var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)entityIndex);
                            if (entity == null || !entity.IsValid) continue;
                            info.TransmitEntities.Remove(entity.Index);
                        }
            }
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    UpdateNinja(player);
                if (!player.PawnIsAlive)
                    if (invisibleEntities.ContainsKey(player.SteamID))
                        invisibleEntities.TryRemove(player.SteamID, out _);
            }
        }

        public static void EnableSkill(CCSPlayerController _)
        {
            Event.EnableTransmit();
        }
        
        public static void DisableSkill(CCSPlayerController player)
        {
            SetPlayerVisibility(player, 0);
            SetWeaponVisibility(player, 0);
            invisibleEntities.TryRemove(player.SteamID, out _);
        }

        private static void UpdateNinja(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            
            var flags = (PlayerFlags)pawn.Flags;
            var buttons = player.Buttons;

            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null) return;

            var activeWeapon = weaponServices.ActiveWeapon.Value;
            float percentInvisibility = 0;

            if (buttons.HasFlag(PlayerButtons.Duck))
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "duckPercentInvisibility");
            if (activeWeapon != null && activeWeapon.DesignerName == "weapon_knife")
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "knifePercentInvisibility");
            if (!buttons.HasFlag(PlayerButtons.Moveleft) && !buttons.HasFlag(PlayerButtons.Moveright) && !buttons.HasFlag(PlayerButtons.Forward) && !buttons.HasFlag(PlayerButtons.Back) && flags.HasFlag(PlayerFlags.FL_ONGROUND))
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "idlePercentInvisibility");

            SetWeaponVisibility(player, percentInvisibility);
            if (invisibilityChanged.TryGetValue(player.Handle, out float oldInvisibility))
                if (percentInvisibility == oldInvisibility)
                    return;

            invisibilityChanged.TryAdd(player.Handle, percentInvisibility);
            SetPlayerVisibility(player, percentInvisibility);
        }

        private static void SetPlayerVisibility(CCSPlayerController player, float percentInvisibility)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn != null)
            {
                var color = Color.FromArgb(Math.Max(255 - (int)(255 * percentInvisibility), 0), 255, 255, 255);
                playerPawn.Render = color;
                Utilities.SetStateChanged(playerPawn, "CBaseModelEntity", "m_clrRender");
            }
        }

        private static void SetWeaponVisibility(CCSPlayerController player, float percentInvisibility)
        {
            if (!Instance.IsPlayerValid(player)) return;
            var playerPawn = player.PlayerPawn.Value!;
            if (playerPawn.WeaponServices == null) return;

            var color = Color.FromArgb(Math.Max(255 - (int)(255 * percentInvisibility * 2), 0), 255, 255, 255);

            invisibleEntities.TryRemove(player.SteamID, out _);
            if (color.A != 0) return;

            foreach (var weapon in playerPawn.WeaponServices.MyWeapons)
            {
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
                {
                    if (invisibleEntities.TryGetValue(player.SteamID, out var items))
                    {
                        if (!items.Contains(weapon.Index))
                            items.Add(weapon.Index);
                    }
                    else
                        invisibleEntities.TryAdd(player.SteamID, [weapon.Index]);
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#dedede", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float idlePercentInvisibility = .3f, float duckPercentInvisibility = .3f, float knifePercentInvisibility = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float IdlePercentInvisibility { get; set; } = idlePercentInvisibility;
            public float DuckPercentInvisibility { get; set; } = duckPercentInvisibility;
            public float KnifePercentInvisibility { get; set; } = knifePercentInvisibility;
        }
    }
}