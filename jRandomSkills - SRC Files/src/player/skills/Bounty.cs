using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Bounty : ISkill
    {
        private const Skills skillName = Skills.Bounty;

        private static readonly ConcurrentDictionary<uint, byte> bountyTargets = [];
        private static readonly ConcurrentDictionary<uint, uint> ownerToTarget = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            bountyTargets.Clear();
            ownerToTarget.Clear();

            foreach (var player in PlayerManager.GetTickPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            bountyTargets.TryRemove(playerIndex, out _);
            ownerToTarget.TryRemove(playerIndex, out _);

            foreach (var kvp in ownerToTarget)
                if (kvp.Value == playerIndex)
                    ownerToTarget.TryRemove(kvp.Key, out _);
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
            if (player == null || !player.IsValid) return;

            if (ownerToTarget.TryRemove(player.Index, out uint targetIndex))
                bountyTargets.TryRemove(targetIndex, out _);

            SkillUtils.CloseMenu(player);
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

            if (commands.Length == 0 || !uint.TryParse(commands[0], out uint enemyIndex))
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
                return;
            }

            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);
            if (enemy == null || !enemy.IsValid)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
                return;
            }

            bountyTargets[enemy.Index] = 0;
            ownerToTarget[player.Index] = enemy.Index;
            playerInfo.SkillUsed = true;

            int reward = SkillsInfo.GetValue<int>(skillName, "reward");

            playerEvent.PrintToChat($" {ChatColors.Green}{playerEvent.GetTranslation("bounty_player_info", enemy.PlayerName, reward)}");

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent != null && enemyEvent.IsValid)
                enemyEvent.PrintToChat($" {ChatColors.Red}{enemyEvent.GetTranslation("bounty_enemy_info", reward)}");
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            if (bountyTargets.IsEmpty) return;

            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            if (!bountyTargets.TryRemove(victim.Index, out _)) return;

            foreach (var kvp in ownerToTarget)
                if (kvp.Value == victim.Index)
                    ownerToTarget.TryRemove(kvp.Key, out _);

            var killer = PlayerManager.GetPlayerEvent(@event.Attacker);
            if (killer == null || !killer.IsValid) return;
            if (killer.Index == victim.Index) return;
            if (killer.Team == victim.Team) return;

            int reward = SkillsInfo.GetValue<int>(skillName, "reward");
            if (!GiveMoney(killer, reward)) return;

            var killerEvent = PlayerManager.GetPlayerFromEvent(killer);
            if (killerEvent != null && killerEvent.IsValid)
                killerEvent.PrintToChat($" {ChatColors.Green}{killerEvent.GetTranslation("bounty_claimed_info", victim.PlayerName, reward)}");
        }

        private static int GetMaxMoney() => ConVar.Find("mp_maxmoney")?.GetPrimitiveValue<int>() ?? 16000;

        private static bool GiveMoney(CCSPlayerController player, int amount)
        {
            var moneyServices = player.InGameMoneyServices;
            if (moneyServices == null) return false;

            int maxMoney = GetMaxMoney();
            if (moneyServices.Account >= maxMoney) return false;

            moneyServices.Account = Math.Clamp(moneyServices.Account + amount, 0, maxMoney);
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
            return true;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#f2c14e", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int reward = 300) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int Reward { get; set; } = reward;
        }
    }
}
