using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Marked : ISkill
    {
        private const Skills skillName = Skills.Marked;
        // victim index -> curser index
        private static readonly ConcurrentDictionary<uint, uint> markedPlayers = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            markedPlayers.Clear();
            foreach (var player in PlayerManager.GetTickPlayers())
                if (player != null && player.IsValid)
                    SkillUtils.CloseMenu(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            markedPlayers.TryRemove(playerIndex, out _);
            foreach (var entry in markedPlayers.Where(m => m.Value == playerIndex).ToArray())
                markedPlayers.TryRemove(entry.Key, out _);
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
                    markedPlayers[enemy.Index] = player.Index;
                    playerInfo.SkillUsed = true;
                    SkillUtils.CloseMenu(player);
                    playerEvent.PrintToChat($" {ChatColors.Lime}{playerEvent.GetTranslation("marked_player_info", enemy.PlayerName)}");

                    var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
                    if (enemyEvent != null && enemyEvent.IsValid)
                        enemyEvent.PrintToChat($" {ChatColors.Red}{enemyEvent.GetTranslation("marked_enemy_info")}");
                    return;
                }
            }
            playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillUtils.CloseMenu(player);

            foreach (var entry in markedPlayers.Where(m => m.Value == player.Index).ToArray())
                markedPlayers.TryRemove(entry.Key, out _);
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null || markedPlayers.IsEmpty) return;
            if (damagedEntity.DesignerName != "player") return;

            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);
            var victim = victimPawn.Controller?.Value;
            if (victim == null || !victim.IsValid || !markedPlayers.ContainsKey(victim.Index)) return;

            // Only damage from the marked player's enemies is amplified.
            var attackerEnt = damageInfo.Attacker?.Value;
            if (attackerEnt == null || !attackerEnt.IsValid || attackerEnt.DesignerName != "player") return;
            if (attackerEnt.TeamNum == victimPawn.TeamNum) return;

            damageInfo.Damage *= SkillsInfo.GetValue<float>(skillName, "damageMultiplier");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#c73a5b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float damageMultiplier = 1.35f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float DamageMultiplier { get; set; } = damageMultiplier;
        }
    }
}
