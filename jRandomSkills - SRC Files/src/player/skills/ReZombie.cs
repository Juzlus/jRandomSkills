using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class ReZombie : ISkill
    {
        private const Skills skillName = Skills.ReZombie;
        private static readonly ConcurrentDictionary<uint, byte> zombies = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            zombies.TryRemove(player.Index, out _);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            zombies.TryRemove(player.Index, out _);
            SetPlayerColor(player.PlayerPawn.Value, true);
            ResetHealth(player);
        }

        public static void ResetHealth(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null) return;

            pawn.MaxHealth = 100;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            pawn.Health = Math.Min(pawn.Health, 100);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid;
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;
            if (!zombies.ContainsKey(player.Index) || weapon == "c4") return;
            player.ExecuteClientCommand("slot3");
        }

        public static void NewRound()
        {
            foreach (var playerIndex in zombies.Keys)
            {
                var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                if (player == null || !player.IsValid) continue;

                DisableSkill(player);
            }
            zombies.Clear();
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid || zombies.ContainsKey(player.Index)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn.AbsOrigin == null) return;

            Vector deadPosition = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
            QAngle angle = new(pawn.V_angle.X, pawn.V_angle.Y, 0);

            ulong steamID = player.SteamID;
            player.Respawn();

            Instance.AddTimer(.2f, () => {
                lock (setLock)
                {
                    var player = Utilities.GetPlayerFromSteamId(steamID);
                    if (player == null || !player.IsValid || !player.PlayerPawn.IsValid) return;

                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) return;

                    var zombieHealth = SkillsInfo.GetValue<int>(skillName, "zombieHealth");
                    
                    player.Respawn();
                    zombies.TryAdd(player.Index, 0);
                    
                    SetPlayerColor(pawn, false);
                    SkillUtils.AddHealth(pawn, zombieHealth - 100, zombieHealth);
                    
                    pawn.Teleport(deadPosition);
                    pawn.Look(angle);
                    player.ExecuteClientCommand("slot3");
                    
                    Instance.AddTimer(1, () => {
                        var player = Utilities.GetPlayerFromSteamId(steamID);
                        if (player == null || !player.IsValid || !player.PlayerPawn.IsValid) return;

                        player.ExecuteClientCommand("slot3");
                    });
                }
            });
        }

        public static bool OnWeaponCanAcquire(DynamicHook hook, CCSPlayerController player, CEconItemView econItem, CCSWeaponBaseVData vdata)
        {
            string weaponName = vdata.Name;
            if (string.IsNullOrEmpty(weaponName)) return false;

            if (!zombies.ContainsKey(player.Index))
                return false;

            if (weaponName.Contains("knife") || weaponName.Contains("bayonet"))
                return false;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return false;

            hook.SetReturn(AcquireResult.InvalidItem);
            return true;
        }

        private static void SetPlayerColor(CCSPlayerPawn pawn, bool normal)
        {
            var color = normal ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 255, 0, 0);
            pawn.Render = color;
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff5C0A", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int zombieHealth = 500) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int ZombieHealth { get; set; } = zombieHealth;
        }
    }
}