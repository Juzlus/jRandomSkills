using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Earthquake : ISkill
    {
        private const Skills skillName = Skills.Earthquake;
        private static readonly ConcurrentDictionary<ulong, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly object setLock = new();

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

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill == skillName)
                SkillPlayerInfo.TryRemove(player.SteamID, out _);
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

            if (cooldown == 0)
                return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            string remainingLine = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
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
                    MakeShake(player);
                }
            }
        }

        private static void MakeShake(CCSPlayerController player)
        {
            foreach (var enemy in Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.PawnIsAlive))
                CreateShake(player);
        }

        private static void CreateShake(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            var shake = Utilities.CreateEntityByName<CEnvShake>("env_shake");
            if (shake == null) return;

            shake.DispatchSpawn();
            if (!shake.IsValid) return;

            shake.Amplitude = SkillsInfo.GetValue<float>(skillName, "amplitude");
            shake.Frequency = SkillsInfo.GetValue<float>(skillName, "frequency");
            shake.Duration = SkillsInfo.GetValue<float>(skillName, "duration");
            shake.Radius = SkillsInfo.GetValue<float>(skillName, "radius");

            shake.Teleport(new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z));
            shake.AcceptInput("SetParent", pawn, pawn, "!activator");
            shake.AcceptInput("StartShake");
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = false, string color = "#42f59b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float cooldown = 16f, float amplitude = 15f, float frequency = 500f, float duration = 8f, float radius = 50f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Cooldown { get; set; } = cooldown;
            public float Amplitude { get; set; } = amplitude;
            public float Frequency { get; set; } = frequency;
            public float Duration { get; set; } = duration;
            public float Radius { get; set; } = radius;
        }
    }
}