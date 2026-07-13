using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class CarefulBullets : ISkill
    {
        private const Skills skillName = Skills.CarefulBullets;
        private static readonly ConcurrentDictionary<uint, byte> targetPlayers = [];
        private static readonly ConcurrentDictionary<uint, bool> lastShot = [];
        private static readonly ConcurrentDictionary<uint, int> hitPlayer = [];
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
                foreach (var player in Utilities.GetPlayers())
                {
                    DisableSkill(player);
                    SkillUtils.CloseMenu(player);
                }

                targetPlayers.Clear();
                lastShot.Clear();
                hitPlayer.Clear();
                playersToTarget.Clear();
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

            targetPlayers.TryAdd(enemy.Index, 0);
            playersToTarget[player.Index] = enemy.Index;
            playerInfo.SkillUsed = true;

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("carefulbullets_player_info", enemy.PlayerName));
            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("carefulbullets_enemy_info"));
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
                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void BulletImpact(EventBulletImpact @event)
        {
            var eventPlayer = @event.Userid;
            if (eventPlayer == null || !eventPlayer.IsValid) return;

            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null ||  !player.IsValid) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;

            if (!targetPlayers.ContainsKey(player.Index) || player.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                return;

            uint playerIndex = player.Index;

            Server.NextFrame(() =>
            {
                if (player == null || !player.IsValid) return;
                if (playerPawn == null || !playerPawn.IsValid) return;

                bool ishitPlayer = hitPlayer.TryGetValue(playerIndex, out int tick) && tick + 10 >= Server.TickCount;

                if (!lastShot.ContainsKey(playerIndex))
                {
                    lastShot.TryAdd(playerIndex, ishitPlayer);

                    Instance.AddTickTimer(1, () =>
                    {
                        if (lastShot.TryRemove(playerIndex, out bool didHit) && !didHit)
                        {
                            eventPlayer.ExecuteClientCommand($"play player/player_damagebody_0{Instance.Random.Next(4,8)}");
                            SkillUtils.TakeHealth(playerPawn, SkillsInfo.GetValue<int>(skillName, "damageAfterMiss"));
                        }
                        lastShot.TryRemove(playerIndex, out _);
                    });
                }
                else if (ishitPlayer)
                    lastShot.TryUpdate(playerIndex, true, false);
            });
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null || param2.Attacker == null || param2.Attacker.Value == null)
                return;

            CCSPlayerPawn attackerPawn = new(param2.Attacker.Value.Handle);
            CCSPlayerPawn victimPawn = new(param.Handle);

            if (attackerPawn.DesignerName != "player" || victimPawn.DesignerName != "player")
                return;

            if (attackerPawn == null || attackerPawn.Controller?.Value == null || victimPawn == null || victimPawn.Controller?.Value == null)
                return;

            CCSPlayerController attacker = PlayerManager.GetPlayerEvent(attackerPawn.Controller.Value.As<CCSPlayerController>())!;

            if (attacker == null || !attacker.IsValid) return;
            if (!targetPlayers.ContainsKey(attacker.Index)) return;

            hitPlayer.AddOrUpdate(attacker.Index, Server.TickCount, (_, _) => Server.TickCount);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            targetPlayers.TryRemove(player.Index, out _);
            lastShot.TryRemove(player.Index, out _);
            hitPlayer.TryRemove(player.Index, out _);
            
            if (playersToTarget.TryRemove(player.Index, out uint targetIndex))
            {
                targetPlayers.TryRemove(targetIndex, out _);
                lastShot.TryRemove(targetIndex, out _);
                hitPlayer.TryRemove(targetIndex, out _);

                var target = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)targetIndex));
                if (target != null && target.IsValid && target.PawnIsAlive && !SkillUtils.IsFreezeTime())
                    target.PrintToChat($" {ChatColors.Green}" + target.GetTranslation("carefulbullets_disable_info"));
            }

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            targetPlayers.TryRemove(player.Index, out _);
            lastShot.TryRemove(player.Index, out _);
            hitPlayer.TryRemove(player.Index, out _);

            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#db6c35", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int damageAfterMiss = 5) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int DamageAfterMiss { get; set; } = damageAfterMiss;
        }
    }
}