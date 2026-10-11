using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    // Pick an enemy: for the rest of the round, or until you die, every movement key of theirs moves them the
    // opposite way. Done on the server each tick: the velocity their keys ask for is applied reversed.
    public class ForgotControls : ISkill
    {
        private const Skills skillName = Skills.ForgotControls;
        private static readonly ConcurrentDictionary<uint, uint> targets = []; // holder -> victim
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                targets.Clear();
                foreach (var player in PlayerManager.GetTickPlayers())
                    SkillUtils.CloseMenu(player);
            }
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lock (setLock)
            {
                targets.TryRemove(playerIndex, out _);
                foreach (var kvp in targets)
                    if (kvp.Value == playerIndex)
                        targets.TryRemove(kvp.Key, out _);
            }
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
            lock (setLock)
            {
                Release(player.Index, notify: true);
                SkillUtils.CloseMenu(player);
            }
        }

        // The curse ends with the holder's death.
        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;
            lock (setLock) Release(victim.Index, notify: true);
        }

        private static void Release(uint holderIndex, bool notify)
        {
            if (!targets.TryRemove(holderIndex, out uint victimIndex) || !notify) return;
            var victim = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)victimIndex));
            if (victim != null && victim.IsValid && victim.PawnIsAlive)
                victim.PrintToChat($" {ChatColors.Green}{victim.GetTranslation("forgotcontrols_disable_info")}");
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 == 0)
            {
                foreach (var player in PlayerManager.GetTickPlayers())
                {
                    var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                    if (playerInfo == null || playerInfo.HasSkill(skillName) == false) continue;
                    if (!SkillUtils.HasMenu(player)) continue;

                    var enemies = SkillUtils.GetSelectableEnemies(player, true);
                    ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                    SkillUtils.UpdateMenu(player, menuItems);
                }
            }

            if (targets.IsEmpty) return;

            float speed = SkillsInfo.GetValue<float>(skillName, "moveSpeed");
            foreach (var (holderIndex, victimIndex) in targets)
            {
                var victim = Utilities.GetPlayerFromIndex((int)victimIndex);
                if (victim == null || !victim.IsValid) continue;
                var pawn = victim.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || pawn.AbsVelocity == null) continue;
                if ((pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) == 0) continue;
                if (pawn.MoveType is MoveType_t.MOVETYPE_LADDER or MoveType_t.MOVETYPE_NOCLIP or MoveType_t.MOVETYPE_NONE) continue;

                var buttons = victim.Buttons;
                float f = (buttons.HasFlag(PlayerButtons.Forward) ? 1f : 0f) - (buttons.HasFlag(PlayerButtons.Back) ? 1f : 0f);
                float r = (buttons.HasFlag(PlayerButtons.Moveright) ? 1f : 0f) - (buttons.HasFlag(PlayerButtons.Moveleft) ? 1f : 0f);
                if (f == 0f && r == 0f) continue;

                float yaw = pawn.V_angle.Y * MathF.PI / 180f;
                (float sy, float cy) = MathF.SinCos(yaw);
                // Forward (cy, sy), right (sy, -cy): the wish direction from the keys, applied reversed.
                float wx = cy * f + sy * r, wy = sy * f - cy * r;
                float len = MathF.Sqrt(wx * wx + wy * wy);
                if (len < 0.01f) continue;

                float s = speed * Math.Max(pawn.VelocityModifier, 0.1f) * (buttons.HasFlag(PlayerButtons.Duck) || buttons.HasFlag(PlayerButtons.Speed) ? 0.5f : 1f);
                pawn.AbsVelocity.X = -wx / len * s;
                pawn.AbsVelocity.Y = -wy / len * s;
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.HasSkill(skillName) != true) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (playerInfo.SkillUsed)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("areareaper_used_info")}");
                return;
            }

            if (commands.Length == 0 || !uint.TryParse(commands[0], out uint enemyIndex))
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);
            if (enemy == null || !enemy.IsValid || enemy.Team == player.Team)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            lock (setLock)
            {
                Release(player.Index, notify: true);
                targets[player.Index] = enemy.Index;
            }

            playerInfo.SkillUsed = true;
            SkillUtils.CloseMenu(player);

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("forgotcontrols_player_info", enemy.PlayerName));
            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent != null && enemyEvent.IsValid)
                enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("forgotcontrols_enemy_info"));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#C71585", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, float moveSpeed = 250f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float MoveSpeed { get; set; } = moveSpeed;
        }
    }
}
