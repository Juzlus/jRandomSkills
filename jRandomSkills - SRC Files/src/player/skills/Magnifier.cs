using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Magnifier : ISkill
    {
        private const Skills skillName = Skills.Magnifier;
        private static readonly ConcurrentDictionary<uint, uint> playersFOV = [];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var playerIndex in playersFOV.Keys)
            {
                var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                if (player == null || !player.IsValid) continue;
                DisableSkill(player);
            }
                
            playersFOV.Clear();
            playersToTarget.Clear();

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
                var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();

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
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("areareaper_used_info")}" );
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

            playersFOV.AddOrUpdate(enemy.Index, enemy.DesiredFOV, (k, v) => enemy.DesiredFOV);
            playersToTarget[player.Index] = enemy.Index;

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            enemyEvent.DesiredFOV = SkillsInfo.GetValue<uint>(skillName, "customFOV");
            Utilities.SetStateChanged(enemyEvent, "CBasePlayerController", "m_iDesiredFOV");
            playerInfo.SkillUsed = true;

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("magnifier_player_info", enemy.PlayerName));
            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("magnifier_enemy_info"));
        }

        public static void BotTakeover(EventBotTakeover @event)
        {
            var bot = PlayerManager.GetPlayerEvent(@event.Botid);
            if (bot == null || !bot.IsValid) return;

            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            if (!playersFOV.ContainsKey(bot.Index)) return;
        
            player.DesiredFOV = SkillsInfo.GetValue<uint>(skillName, "customFOV");
            Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var enemies = Utilities.GetPlayers().Where(p => p != null && p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p => p != null && p.IsValid && p.Team != player.Team && p.PlayerPawn?.Value != null && p.PlayerPawn.Value.IsValid && p.PlayerPawn.Value.Health > 0 && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();
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

            if (playersToTarget.TryRemove(player.Index, out uint targetIndex))
            {
                var target = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)targetIndex));
                if (target != null && target.IsValid)
                {
                    if (playersFOV.TryGetValue(targetIndex, out uint fov))
                    {
                        target.DesiredFOV = fov;
                        Utilities.SetStateChanged(target, "CBasePlayerController", "m_iDesiredFOV");
                    }

                    if (target.PawnIsAlive && !SkillUtils.IsFreezeTime())
                        target.PrintToChat($" {ChatColors.Green}" + target.GetTranslation("magnifier_disable_info"));
                }
                playersFOV.TryRemove(targetIndex, out _);
            }

            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            player.DesiredFOV = 0;
            Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
            SkillUtils.ResetPrintHTML(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#9ba882", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, uint customFOV = 50) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public uint CustomFOV { get; set; } = customFOV;
        }
    }
}