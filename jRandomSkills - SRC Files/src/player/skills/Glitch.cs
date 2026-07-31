using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Glitch : ISkill
    {
        private const Skills skillName = Skills.Glitch;
        private static readonly ConcurrentDictionary<uint, byte> glitchedPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lock (setLock)
            {
                glitchedPlayers.TryRemove(playerIndex, out _);
                playersToTarget.TryRemove(playerIndex, out _);

                foreach (var kvp in playersToTarget)
                    if (kvp.Value == playerIndex)
                        playersToTarget.TryRemove(kvp.Key, out _);
            }
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var player in PlayerManager.GetTickPlayers())
                {
                    DisableSkill(player);
                    SkillUtils.CloseMenu(player);
                }

                glitchedPlayers.Clear();
                playersToTarget.Clear();
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);

                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                if (!SkillUtils.HasMenu(player)) continue;

                var enemies = SkillUtils.GetSelectableEnemies(player, true);

                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Index.ToString())));
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

            if (!uint.TryParse(enemyId, out uint enemyIndex))
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);

            if (enemy == null)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            glitchedPlayers.TryAdd(enemy.Index, 0);
            playersToTarget[player.Index] = enemy.Index;

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            enemyEvent.ReplicateConVar("sv_disable_radar", "1");
            playerInfo.SkillUsed = true;

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("glitch_player_info", enemy.PlayerName));
            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("glitch_enemy_info"));
        }

        public static void BotTakeover(EventBotTakeover @event)
        {
            var bot = PlayerManager.GetPlayerEvent(@event.Botid);
            if (bot == null || !bot.IsValid) return;

            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            if (!glitchedPlayers.ContainsKey(bot.Index)) return;

            player.ReplicateConVar("sv_disable_radar", "1");
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var enemies = SkillUtils.GetSelectableEnemies(player, true);
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
                var target = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)targetIndex));
                if (target != null && target.IsValid)
                {
                    target.ReplicateConVar("sv_disable_radar", "0");
                    if (target.PawnIsAlive && !SkillUtils.IsFreezeTime())
                        target.PrintToChat($" {ChatColors.Green}" + target.GetTranslation("glitch_disable_info"));
                }
                glitchedPlayers.TryRemove(targetIndex, out _);
            }

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            player.ReplicateConVar("sv_disable_radar", "0");
            glitchedPlayers.TryRemove(player.Index, out _);
            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#f542ef", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
