using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Mute : ISkill
    {
        private const Skills skillName = Skills.Mute;
        // victim index -> (curser index, voice flags before the mute)
        private static readonly ConcurrentDictionary<uint, (uint Curser, VoiceFlags Previous)> mutedPlayers = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            UnmuteAll();
            foreach (var player in PlayerManager.GetTickPlayers())
                if (player != null && player.IsValid)
                    SkillUtils.CloseMenu(player);
        }

        public static void RoundEnd()
        {
            UnmuteAll();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            mutedPlayers.TryRemove(playerIndex, out _);
            foreach (var entry in mutedPlayers.Where(m => m.Value.Curser == playerIndex).ToArray())
                Unmute(entry.Key);
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                if (!SkillUtils.HasMenu(player)) continue;

                ConcurrentBag<(string, string)> menuItems = [.. SkillUtils.GetSelectableEnemies(player, true).Select(e => ($"‪{e.PlayerName}‬", e.Index.ToString()))];
                if (!menuItems.IsEmpty)
                    SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo?.Skill != skillName) return;

            playerInfo.SkillUsed = false;

            var enemies = SkillUtils.GetSelectableEnemies(player, true);
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => ($"‪{e.PlayerName}‬", e.Index.ToString()))];
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || commands.Length < 1) return;

            string option = commands[0];
            if (string.IsNullOrEmpty(option)) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playerInfo.SkillUsed)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_used_info")}");
                return;
            }

            if (uint.TryParse(option, out uint enemyIndex))
            {
                var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);
                if (enemy != null && enemy.IsValid && enemy.PlayerPawn?.Value?.Health > 0 && enemy.Team != player.Team)
                {
                    MutePlayer(enemy, player.Index);
                    playerInfo.SkillUsed = true;
                    SkillUtils.CloseMenu(player);
                    playerEvent.PrintToChat($" {ChatColors.Lime}{playerEvent.GetTranslation("mute_player_info", enemy.PlayerName)}");

                    var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
                    if (enemyEvent != null && enemyEvent.IsValid)
                        enemyEvent.PrintToChat($" {ChatColors.Red}{enemyEvent.GetTranslation("mute_enemy_info")}");
                    return;
                }
            }
            playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillUtils.CloseMenu(player);

            foreach (var entry in mutedPlayers.Where(m => m.Value.Curser == player.Index).ToArray())
                Unmute(entry.Key);
        }

        private static void MutePlayer(CCSPlayerController enemy, uint curserIndex)
        {
            var previous = mutedPlayers.TryGetValue(enemy.Index, out var existing) ? existing.Previous : enemy.VoiceFlags;
            mutedPlayers[enemy.Index] = (curserIndex, previous);
            enemy.VoiceFlags = previous | VoiceFlags.Muted;
        }

        private static void Unmute(uint victimIndex)
        {
            if (!mutedPlayers.TryRemove(victimIndex, out var entry)) return;

            var victim = Utilities.GetPlayerFromIndex((int)victimIndex);
            if (victim == null || !victim.IsValid) return;

            victim.VoiceFlags = entry.Previous;

            var victimEvent = PlayerManager.GetPlayerFromEvent(victim);
            if (victimEvent != null && victimEvent.IsValid && victimEvent.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                victimEvent.PrintToChat($" {ChatColors.Green}{victimEvent.GetTranslation("mute_disable_info")}");
        }

        private static void UnmuteAll()
        {
            foreach (var victimIndex in mutedPlayers.Keys.ToArray())
                Unmute(victimIndex);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#2fc468", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
