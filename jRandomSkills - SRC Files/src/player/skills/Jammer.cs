using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Jammer : ISkill
    {
        private const Skills skillName = Skills.Jammer;
        private const string ownerTag = "Jammer";
        private static readonly ConcurrentDictionary<uint, byte> jammedPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> jammerToTarget = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lock (setLock)
            {
                jammedPlayers.TryRemove(playerIndex, out _);
                jammerToTarget.TryRemove(playerIndex, out _);

                foreach (var kvp in jammerToTarget)
                    if (kvp.Value == playerIndex)
                        jammerToTarget.TryRemove(kvp.Key, out _);
            }
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var playerIndex in jammedPlayers.Keys)
                {
                    var player = PlayerManager.GetController(playerIndex);
                    if (player == null || !player.IsValid) continue;

                    var playerEvent = PlayerManager.GetPlayerFromEvent(PlayerManager.GetController(playerIndex));
                    if (playerEvent == null || !playerEvent.IsValid) continue;

                    SetCrosshair(player, true);
                    SetCrosshair(playerEvent, true);
                }
                jammedPlayers.Clear();
                jammerToTarget.Clear();
            }

            foreach (var player in PlayerManager.GetTickPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                var playerInfo = PlayerManager.GetPlayer(player);

                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                if (!SkillUtils.HasMenu(player)) continue;

                var enemies = SkillUtils.GetSelectableEnemies(player, true);

                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Key().ToString())));
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            var playerInfo = PlayerManager.GetPlayer(player);
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

            var enemy = PlayerManager.GetController(enemyIndex);

            if (enemy == null)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            jammedPlayers.TryAdd(enemy.Key(), 0);
            jammerToTarget[player.Key()] = enemy.Key();

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            SetCrosshair(enemy, false);
            SetCrosshair(enemyEvent, false);
            playerInfo.SkillUsed = true;

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("jammer_player_info", enemy.PlayerName));
            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("jammer_enemy_info"));
        }

        private static void SetCrosshair(CCSPlayerController player, bool enabled)
        {
            SkillUtils.SetCrosshairHidden(player, ownerTag, !enabled);
        }

        public static void BotTakeover(EventBotTakeover @event)
        {
            var bot = PlayerManager.GetPlayerEvent(@event.Botid);
            if (bot == null || !bot.IsValid) return;

            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            if (!jammedPlayers.ContainsKey(bot.Key())) return;

            SetCrosshair(bot, false);
            SetCrosshair(player, false);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var enemies = SkillUtils.GetSelectableEnemies(player, true);
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Key().ToString())));
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (jammerToTarget.TryRemove(player.Key(), out uint targetIndex))
            {
                var target1 = PlayerManager.GetPlayerFromEvent(PlayerManager.GetController(targetIndex));
                var target2 = PlayerManager.GetController(targetIndex);
                if (target1 != null && target1.IsValid && target2 != null && target2.IsValid)
                {
                    SetCrosshair(target1, true);
                    SetCrosshair(target2, true);

                    if (target1.PawnIsAlive)
                        target1.PrintToChat($" {ChatColors.Green}" + target1.GetTranslation("jammer_disable_info"));
                }
                jammedPlayers.TryRemove(targetIndex, out _);
            }

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var bot = PlayerManager.GetPlayerEvent(@event.Userid);
            if (bot == null || !bot.IsValid) return;

            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            SetCrosshair(bot, true);
            SetCrosshair(player, true);

            jammedPlayers.TryRemove(player.Key(), out _);
            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#42f5a7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
