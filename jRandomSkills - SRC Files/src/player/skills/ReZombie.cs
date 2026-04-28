using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills
{
    public class ReZombie : ISkill
    {
        private const Skills skillName = Skills.ReZombie;
        private static readonly ConcurrentDictionary<uint, byte> zombies = [];
        private static readonly ConcurrentDictionary<ulong, Timer> timers = [];
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
            int team = player.TeamNum;
            if (team != 2 && team != 3) return;

            player.Respawn();

            Server.NextFrame(() => {
                lock (setLock)
                {
                    var player = Utilities.GetPlayerFromSteamId(steamID);
                    if (player == null || !player.IsValid || !player.PlayerPawn.IsValid) return;

                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) return;

                    var zombieHealth = SkillsInfo.GetValue<int>(skillName, "zombieHealth");

                    bool isBlock = team != player.TeamNum || player.TeamChanged;

                    player.Respawn();
                    zombies.TryAdd(player.Index, 0);

                    if (isBlock)
                    {
                        pawn.Flags |= (uint)Flags_t.FL_FROZEN;
                        pawn.Teleport(new Vector(0, 0, -1000), new QAngle(90, 0, 0));

                        bool isFreezeTime = Instance.GameRules != null && Instance.GameRules.FreezePeriod == true;

                        if (!isFreezeTime)
                        {
                            Server.NextFrame(() =>
                            {
                                if (pawn == null || !pawn.IsValid) return;
                                pawn.CommitSuicide(false, true);
                            });
                            return;
                        }

                        ulong steamId = player.SteamID;

                        if (!timers.ContainsKey(player.SteamID))
                        {
                            var timer = Instance.AddTimer(1f, () =>
                            {
                                if (player == null || !player.IsValid || pawn == null || !pawn.IsValid)
                                {
                                    if (timers.TryRemove(steamId, out var t))
                                        t.Kill();
                                    return;
                                }

                                bool isFreezeTime = Instance.GameRules != null && Instance.GameRules.FreezePeriod == true;

                                if (!isFreezeTime)
                                {
                                    if (timers.TryRemove(steamId, out var t))
                                        t.Kill();

                                    pawn.CommitSuicide(false, true);
                                    return;
                                }

                            }, TimerFlags.STOP_ON_MAPCHANGE | TimerFlags.REPEAT);

                            timers.TryAdd(player.SteamID, timer);
                        }

                        return;
                    }

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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff5C0A", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, int zombieHealth = 500) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public int ZombieHealth { get; set; } = zombieHealth;
        }
    }
}