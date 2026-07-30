using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class ExpensiveAmmo : ISkill
    {
        private const Skills skillName = Skills.ExpensiveAmmo;
        private static readonly ConcurrentDictionary<uint, byte> cursedPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            cursedPlayers.TryRemove(playerIndex, out _);
            playersToTarget.TryRemove(playerIndex, out _);

            foreach (var kvp in playersToTarget)
                if (kvp.Value == playerIndex)
                    playersToTarget.TryRemove(kvp.Key, out _);
        }

        public static void NewRound()
        {
            cursedPlayers.Clear();
            playersToTarget.Clear();

            foreach (var player in PlayerManager.GetTickPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void WeaponFire(EventWeaponFire @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;
            if (!cursedPlayers.ContainsKey(player.Index)) return;
            if (!SkillUtils.FiresBullets(@event.Weapon)) return;

            var moneyServices = player.InGameMoneyServices;
            if (moneyServices == null) return;

            int cost = SkillsInfo.GetValue<int>(skillName, "moneyPerShot");
            if (cost <= 0) return;

            moneyServices.Account = Math.Max(moneyServices.Account - cost, 0);
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
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

            if (!uint.TryParse(enemyId, out uint enemyIndex))
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);

            if (enemy == null || !enemy.IsValid || enemy.PlayerPawn.Value == null || !enemy.PlayerPawn.Value.IsValid)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            cursedPlayers.TryAdd(enemy.Index, 0);
            playersToTarget[player.Index] = enemy.Index;
            playerInfo.SkillUsed = true;

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("expensiveammo_player_info", enemy.PlayerName));
            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("expensiveammo_enemy_info", SkillsInfo.GetValue<int>(skillName, "moneyPerShot")));
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
                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            if (playersToTarget.TryRemove(player.Index, out uint targetIndex))
            {
                cursedPlayers.TryRemove(targetIndex, out _);

                var target = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)targetIndex));
                if (target != null && target.IsValid && target.PawnIsAlive && !SkillUtils.IsFreezeTime())
                    target.PrintToChat($" {ChatColors.Green}" + target.GetTranslation("expensiveammo_disable_info"));
            }

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            cursedPlayers.TryRemove(player.Index, out _);
            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e0c341", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int moneyPerShot = 50) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int MoneyPerShot { get; set; } = moneyPerShot;
        }
    }
}
