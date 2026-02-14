using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class SoundMaker : ISkill
    {
        private const Skills skillName = Skills.SoundMaker;
        private static readonly ConcurrentDictionary<ulong, byte> SkillPlayerInfo = [];
        private static readonly object setLock = new();

        private const string soundEventName = "Hostage.Pain";
        private const uint soundEventHash = 1876781570;

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
            SkillPlayerInfo.TryAdd(player.SteamID, 0);
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

        public static void PlayerMakeSound(UserMessage um)
        {
            var soundevent = um.ReadUInt("soundevent_hash");
            if (soundevent != soundEventHash) return;

            var userIndex = um.ReadUInt("source_entity_index");
            if (userIndex == 0) return;

            var sourcePlayer = Utilities.GetPlayers().FirstOrDefault(p => p.Pawn?.Value != null && p.Pawn.Value.IsValid && p.Pawn.Value.Index == userIndex);
            if (sourcePlayer == null || !sourcePlayer.IsValid) return;

            var toRemove = um.Recipients.Where(r =>
            {
                if (r.Team == sourcePlayer.Team) return true;
                if (SkillPlayerInfo.ContainsKey(r.SteamID)) return false;
                return true;
            }).ToList();

            foreach (var player in toRemove)
                um.Recipients.Remove(player);
        }

        public static void OnTick()
        {
            if (Server.TickCount % (60 * SkillsInfo.GetValue<int>(skillName, "cooldown")) != 0) return;

            foreach (var player in Utilities.GetPlayers()
                .Where(p => p != null && p.IsValid && p.PawnIsAlive && p.PlayerPawn.Value != null && p.PlayerPawn.Value.IsValid))
                    player.PlayerPawn.Value!.EmitSound(soundEventName, volume: 1f);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e3ed8c", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int cooldown = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int Cooldown { get; set; } = cooldown;
        }
    }
}