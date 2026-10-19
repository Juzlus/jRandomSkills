using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Bowlegs : ISkill
    {
        private const Skills skillName = Skills.Bowlegs;
        private const float DuckSpeedScale = .34f;
        private const float WalkSpeedScale = .52f;
        private const float TickInterval = 1f / 64f;

        private static float accelerate = 5.5f;
        private static float friction = 5.2f;
        private static float stopSpeed = 80f;

        private static readonly ConcurrentDictionary<uint, uint> ownerToTarget = [];
        private static readonly ConcurrentDictionary<uint, byte> targets = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            accelerate = SkillUtils.CvarValue("sv_accelerate", 5.5f);
            friction = SkillUtils.CvarValue("sv_friction", 5.2f);
            stopSpeed = SkillUtils.CvarValue("sv_stopspeed", 80f);

            ownerToTarget.Clear();
            targets.Clear();

            foreach (var player in PlayerManager.GetTickPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            if (ownerToTarget.TryRemove(playerIndex, out uint target))
                ReleaseTarget(target);

            foreach (var kvp in ownerToTarget)
                if (kvp.Value == playerIndex)
                    ownerToTarget.TryRemove(kvp.Key, out _);

            targets.TryRemove(playerIndex, out _);
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
            if (player == null || !player.IsValid) return;

            if (ownerToTarget.TryRemove(player.Key(), out uint targetIndex))
            {
                ReleaseTarget(targetIndex);

                var target = PlayerManager.GetPlayerFromEvent(PlayerManager.GetController(targetIndex));
                if (target != null && target.IsValid && target.PawnIsAlive)
                    target.PrintToChat($" {ChatColors.Green}{target.GetTranslation("bowlegs_disable_info")}");
            }

            SkillUtils.CloseMenu(player);
        }

        private static void ReleaseTarget(uint targetIndex)
        {
            foreach (var kvp in ownerToTarget)
                if (kvp.Value == targetIndex)
                    return;

            targets.TryRemove(targetIndex, out _);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            targets.TryRemove(victim.Key(), out _);
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

            if (commands.Length == 0 || !uint.TryParse(commands[0], out uint enemyIndex))
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
                return;
            }

            var enemy = PlayerManager.GetController(enemyIndex);
            if (enemy == null || !enemy.IsValid || enemy.Team == player.Team)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
                return;
            }

            ownerToTarget[player.Key()] = enemy.Key();
            targets[enemy.Key()] = 0;
            playerInfo.SkillUsed = true;
            SkillUtils.CloseMenu(player);

            playerEvent.PrintToChat($" {ChatColors.Green}{playerEvent.GetTranslation("bowlegs_player_info", enemy.PlayerName)}");

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent != null && enemyEvent.IsValid)
                enemyEvent.PrintToChat($" {ChatColors.Red}{enemyEvent.GetTranslation("bowlegs_enemy_info")}");
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 == 0)
                RefreshMenus();

            if (targets.IsEmpty) return;

            foreach (var targetKey in targets.Keys)
            {
                var target = PlayerManager.GetController(targetKey);
                if (target == null || !target.IsValid) continue;

                var pawn = target.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;
                if (pawn.MoveType != MoveType_t.MOVETYPE_WALK) continue;
                if (!((PlayerFlags)pawn.Flags).HasFlag(PlayerFlags.FL_ONGROUND)) continue;

                var velocity = pawn.AbsVelocity;
                if (velocity == null) continue;

                var buttons = (PlayerManager.GetPlayerFromEvent(target) ?? target).Buttons;
                float forward = (buttons.HasFlag(PlayerButtons.Forward) ? 1f : 0f) - (buttons.HasFlag(PlayerButtons.Back) ? 1f : 0f);
                float right = (buttons.HasFlag(PlayerButtons.Moveright) ? 1f : 0f) - (buttons.HasFlag(PlayerButtons.Moveleft) ? 1f : 0f);
                if (forward == 0f && right == 0f) continue;

                float yaw = pawn.EyeAngles.Y * MathF.PI / 180f;
                float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
                float wishX = cos * forward + sin * right;
                float wishY = sin * forward - cos * right;
                float length = MathF.Sqrt(wishX * wishX + wishY * wishY);
                if (length < .001f) continue;

                float speed = MaxSpeed(pawn) * Math.Max(pawn.VelocityModifier, .1f);
                if (buttons.HasFlag(PlayerButtons.Duck)) speed *= DuckSpeedScale;
                else if (buttons.HasFlag(PlayerButtons.Speed)) speed *= WalkSpeedScale;

                float forwardX = wishX / length, forwardY = wishY / length;
                float vx = velocity.X, vy = velocity.Y;

                float current = MathF.Sqrt(vx * vx + vy * vy);
                if (current > .1f)
                {
                    float slowed = Math.Max(current - Math.Max(current, stopSpeed) * friction * TickInterval, 0f);
                    vx *= slowed / current;
                    vy *= slowed / current;
                }

                float push = accelerate * speed * TickInterval;
                float add = speed - (vx * -forwardX + vy * -forwardY);
                if (add > 0f)
                {
                    float gain = Math.Min(push, add);
                    vx -= forwardX * gain;
                    vy -= forwardY * gain;
                }

                float ux = vx - forwardX * push, uy = vy - forwardY * push;
                float u = MathF.Sqrt(ux * ux + uy * uy);
                if (u < .1f)
                {
                    velocity.X = ux;
                    velocity.Y = uy;
                    continue;
                }

                float frictionDrop = friction * TickInterval;
                float set = u / (1f - frictionDrop) >= stopSpeed ? u / (1f - frictionDrop) : u + stopSpeed * frictionDrop;
                velocity.X = ux / u * set;
                velocity.Y = uy / u * set;
            }
        }

        private static float MaxSpeed(CCSPlayerPawn pawn)
        {
            float speed = pawn.MovementServices?.Maxspeed ?? 0f;
            return speed > 1f ? speed : 250f;
        }

        private static void RefreshMenus()
        {
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#c2a04a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Rare) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
