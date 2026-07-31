using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class JetKick : ISkill
    {
        private const Skills skillName = Skills.JetKick;
        private static readonly ConcurrentDictionary<uint, byte> targetedPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
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

                targetedPlayers.Clear();
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

                var enemies = PlayerManager.GetTickPlayers().Where(p => p != null && p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p => p != null && p.IsValid && p.Team != player.Team && p.PlayerPawn?.Value != null && p.PlayerPawn.Value.IsValid && p.PlayerPawn.Value.Health > 0 && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();

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

            targetedPlayers.TryAdd(enemy.Index, 0);
            playersToTarget[player.Index] = enemy.Index;

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            playerInfo.SkillUsed = true;

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("jetkick_player_info", enemy.PlayerName));
            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("jetkick_enemy_info"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var enemies = PlayerManager.GetTickPlayers().Where(p => p != null && p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p => p != null && p.IsValid && p.Team != player.Team && p.PlayerPawn?.Value != null && p.PlayerPawn.Value.IsValid && p.PlayerPawn.Value.Health > 0 && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();

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
                    if (target.PawnIsAlive && !SkillUtils.IsFreezeTime())
                        target.PrintToChat($" {ChatColors.Green}" + target.GetTranslation("jetkick_disable_info"));
                }
                targetedPlayers.TryRemove(targetIndex, out _);
            }

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            targetedPlayers.TryRemove(player.Index, out _);
            SkillUtils.CloseMenu(player);
        }

        public static void WeaponFire(EventWeaponFire @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            if (!targetedPlayers.ContainsKey(player.Index)) return;
            if (!SkillUtils.FiresBullets(@event.Weapon)) return;

            PushPlayer(player);
        }

        private static void PushPlayer(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.Health <= 0) return;

            Vector newVelocity = -SkillUtils.GetForwardVector(playerPawn.V_angle) * SkillsInfo.GetValue<float>(skillName, "pushVelocity");
            playerPawn.Teleport(null, null, newVelocity);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5a4fd1", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float pushVelocity = 400f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float PushVelocity { get; set; } = pushVelocity;
        }
    }
}
