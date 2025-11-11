using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class ReZombie : ISkill
    {
        private const Skills skillName = Skills.ReZombie;
        private static readonly ConcurrentDictionary<CCSPlayerController, byte> zombies = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            zombies.TryRemove(player, out _);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            zombies.TryRemove(player, out _);
            SetPlayerColor(player.PlayerPawn.Value, true);
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid;
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;
            if (!zombies.ContainsKey(player) || weapon == "c4") return;
            player.ExecuteClientCommand("slot3");
        }

        public static void NewRound()
        {
            foreach (var player in zombies.Keys)
                DisableSkill(player);
            zombies.Clear();
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid || zombies.ContainsKey(player)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn.AbsOrigin == null) return;
            Vector deadPosition = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
            QAngle deadRotation = new(pawn.EyeAngles.X, pawn.EyeAngles.Y, pawn.EyeAngles.Z);

            player.Respawn();
            Instance.AddTimer(.2f, () => {
                lock (setLock)
                {
                    var zombieHealth = SkillsInfo.GetValue<int>(skillName, "zombieHealth");
                    player.Respawn();
                    zombies.TryAdd(player, 0);
                    SetPlayerColor(pawn, false);
                    SkillUtils.AddHealth(pawn, zombieHealth - 100, zombieHealth);
                    pawn.Teleport(deadPosition, deadRotation);
                    player.ExecuteClientCommand("slot3");
                    Instance.AddTimer(1, () => player.ExecuteClientCommand("slot3"));
                }
            });
        }

        private static void SetPlayerColor(CCSPlayerPawn pawn, bool normal)
        {
            var color = normal ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 255, 0, 0);
            pawn.Render = color;
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff5C0A", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int zombieHealth = 250) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int ZombieHealth { get; set; } = zombieHealth;
        }
    }
}