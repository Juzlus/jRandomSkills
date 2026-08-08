using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Nemesis : ISkill
    {
        private const Skills skillName = Skills.Nemesis;

        private static readonly ConcurrentDictionary<uint, byte> markedPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> ownerToTarget = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            markedPlayers.Clear();
            ownerToTarget.Clear();

            foreach (var player in PlayerManager.GetTickPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            markedPlayers.TryRemove(playerIndex, out _);
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
                markedPlayers.TryRemove(targetIndex, out _);

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

            markedPlayers[enemy.Index] = 0;
            ownerToTarget[player.Index] = enemy.Index;
            playerInfo.SkillUsed = true;

            playerEvent.PrintToChat($" {ChatColors.Green}{playerEvent.GetTranslation("nemesis_player_info", enemy.PlayerName)}");

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent != null && enemyEvent.IsValid)
                enemyEvent.PrintToChat($" {ChatColors.Red}{enemyEvent.GetTranslation("nemesis_enemy_info")}");
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            if (markedPlayers.IsEmpty) return;

            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);
            if (param == null || !param.IsValid || param2 == null) return;

            var victimPawn = param.As<CCSPlayerPawn>();
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.DesignerName != "player") return;

            var victimController = victimPawn.Controller.Value;
            if (victimController == null || !victimController.IsValid) return;

            var victim = victimController.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid) return;

            uint victimIndex = PlayerManager.GetPlayerEvent(victim)?.Index ?? victim.Index;
            if (!markedPlayers.ContainsKey(victimIndex)) return;

            param2.Damage *= 1f + SkillsInfo.GetValue<float>(skillName, "extraDamagePercent");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#b8003a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Rare, float extraDamagePercent = .25f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float ExtraDamagePercent { get; set; } = extraDamagePercent;
        }
    }
}
