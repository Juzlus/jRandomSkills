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
                    var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                    if (player == null || !player.IsValid) continue;

                    var playerEvent = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)playerIndex));
                    if (playerEvent == null || !playerEvent.IsValid) continue;

                    SetCrosshair(player, true);
                    SetCrosshair(playerEvent, true);
                }
                jammedPlayers.Clear();
                jammerToTarget.Clear();
            }

            foreach (var player in Utilities.GetPlayers())
                SkillUtils.CloseMenu(player);
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

            jammedPlayers.TryAdd(enemy.Index, 0);
            jammerToTarget[player.Index] = enemy.Index;

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            SetCrosshair(enemy, false);
            SetCrosshair(enemyEvent, false);
            playerInfo.SkillUsed = true;

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("jammer_player_info", player.PlayerName));
            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("jammer_enemy_info"));
        }

        private static void SetCrosshair(CCSPlayerController player, bool enabled)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            pawn.HideHUD = (uint)(enabled
                ? (pawn.HideHUD & ~(1 << 8))
                : (pawn.HideHUD | (1 << 8)));

            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_iHideHUD");
        }

        public static void BotTakeover(EventBotTakeover @event)
        {
            var bot = PlayerManager.GetPlayerEvent(@event.Botid);
            if (bot == null || !bot.IsValid) return;

            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            if (!jammedPlayers.ContainsKey(bot.Index)) return;

            SetCrosshair(bot, false);
            SetCrosshair(player, false);
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
            if (jammerToTarget.TryRemove(player.Index, out uint targetIndex))
            {
                var target1 = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)targetIndex));
                var target2 = Utilities.GetPlayerFromIndex((int)targetIndex);
                if (target1 != null && target1.IsValid && target2 != null && target2.IsValid)
                {
                    SetCrosshair(target1, true);
                    SetCrosshair(target2, true);

                    if (target1.PawnIsAlive && !SkillUtils.IsFreezeTime())
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

            jammedPlayers.TryRemove(player.Index, out _);
            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#42f5a7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}