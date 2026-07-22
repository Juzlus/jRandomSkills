using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Deaf : ISkill
    {
        private const Skills skillName = Skills.Deaf;
        private static readonly ConcurrentDictionary<uint, byte> deafPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            deafPlayers.TryRemove(playerIndex, out _);
            playersToTarget.TryRemove(playerIndex, out _);

            foreach (var kvp in playersToTarget)
                if (kvp.Value == playerIndex)
                    playersToTarget.TryRemove(kvp.Key, out _);
        }

        public static void NewRound()
        {
            foreach (var playerIndex in deafPlayers.Keys)
            {
                var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                if (player == null || !player.IsValid) continue;
                DisableSkill(player);
            }

            deafPlayers.Clear();
            playersToTarget.Clear();

            foreach (var player in Utilities.GetPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void PlayerMakeSound(UserMessage um)
        {
            foreach (var playerIndex in deafPlayers.Keys)
            {
                var player = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)playerIndex));
                if (player == null || !player.IsValid) continue;
                um.Recipients.Remove(player);
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;
            foreach (var player in Utilities.GetPlayers())
            {
                if (!SkillUtils.HasMenu(player)) continue;
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);

                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                var enemies = Utilities.GetPlayers().Where(p =>p != null &&p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p =>p != null &&p.IsValid &&p.Team != player.Team &&p.PlayerPawn?.Value != null &&p.PlayerPawn.Value.IsValid &&p.PlayerPawn.Value.Health > 0 &&!p.IsHLTV &&p.Team != CsTeam.Spectator&& p.Team != CsTeam.None).ToArray();

                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (playerInfo.SkillUsed)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("areareaper_used_info")}");
                return;
            }

            string enemyId = commands[0];

            if (!uint.TryParse(enemyId, out uint enemyIndex)) {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);

            if (enemy == null)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            deafPlayers.TryAdd(enemy.Index, 0);
            playersToTarget[player.Index] = enemy.Index;
            playerInfo.SkillUsed = true;

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("deaf_player_info", enemy.PlayerName));
            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("deaf_enemy_info"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var enemies = Utilities.GetPlayers().Where(p =>p != null &&p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p =>p != null &&p.IsValid &&p.Team != player.Team &&p.PlayerPawn?.Value != null &&p.PlayerPawn.Value.IsValid &&p.PlayerPawn.Value.Health > 0 &&!p.IsHLTV &&p.Team != CsTeam.Spectator&& p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Index.ToString())));
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (playersToTarget.TryRemove(player.Index, out uint targetIndex))
            {
                deafPlayers.TryRemove(targetIndex, out _);

                var target = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)targetIndex));
                if (target != null && target.IsValid && target.PawnIsAlive && !SkillUtils.IsFreezeTime())
                    target.PrintToChat($" {ChatColors.Green}" + target.GetTranslation("deaf_disable_info"));
            }

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            deafPlayers.TryRemove(player.Index, out _);
            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#dae01f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}