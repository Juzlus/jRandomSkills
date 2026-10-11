using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Giant : ISkill
    {
        private const Skills skillName = Skills.Giant;
        private static readonly ConcurrentDictionary<uint, uint> enlargedEnemies = [];
        // The size each holder rolled. Kept here, not in the shared SkillChance slot: a second held skill
        // (Armored, Phoenix, Dwarf...) writes its own number there and the giant would shrink instead.
        private static readonly ConcurrentDictionary<uint, float> rolledScale = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var targetIndex in enlargedEnemies.Values.ToArray())
                    ResetScale(targetIndex);

                enlargedEnemies.Clear();

                foreach (var player in PlayerManager.GetTickPlayers())
                    SkillUtils.CloseMenu(player);
            }
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lock (setLock)
            {
                rolledScale.TryRemove(playerIndex, out _);
                if (enlargedEnemies.TryRemove(playerIndex, out uint targetIndex))
                    ResetScale(targetIndex);

                foreach (var kvp in enlargedEnemies)
                    if (kvp.Value == playerIndex)
                        enlargedEnemies.TryRemove(kvp.Key, out _);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            float minScale = SkillsInfo.GetValue<float>(skillName, "minScale");
            float maxScale = SkillsInfo.GetValue<float>(skillName, "maxScale");
            if (maxScale < minScale) (minScale, maxScale) = (maxScale, minScale);
            float scale = (float)Math.Round((float)Instance.Random.NextDouble() * (maxScale - minScale) + minScale, 2);
            scale = Math.Max(1.01f, scale); // a giant is never smaller than normal, whatever the config says
            rolledScale[player.Index] = scale;
            playerInfo.SkillChance = scale;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var enemies = GetSelectableEnemies(player);
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;

            ReapplyScales();

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo == null || playerInfo.HasSkill(skillName) == false) continue;
                if (!SkillUtils.HasMenu(player)) continue;


                var enemies = GetSelectableEnemies(player);
                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                SkillUtils.UpdateMenu(player, menuItems);
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

            if (PlayerManager.GetPlayerByIndex(enemy.Index)?.Skill == Skills.Chicken)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            float newSize = rolledScale.TryGetValue(player.Index, out float rolled) ? rolled : Math.Max(1.01f, playerInfo.SkillChance ?? 1.01f);

            lock (setLock)
            {
                if (enlargedEnemies.TryRemove(player.Index, out uint previousTarget))
                    ResetScale(previousTarget, notify: true);

                SkillUtils.ChangePlayerScale(enemy, newSize);
                enlargedEnemies[player.Index] = enemy.Index;
            }

            playerInfo.SkillUsed = true;
            SkillUtils.CloseMenu(player);

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("giant_player_info", enemy.PlayerName, newSize));

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("giant_enemy_info", newSize));
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            lock (setLock)
            {
                if (enlargedEnemies.TryRemove(player.Index, out uint targetIndex))
                    ResetScale(targetIndex, notify: true);

                rolledScale.TryRemove(player.Index, out _);
                SkillUtils.CloseMenu(player);
            }
        }

        private static void ReapplyScales()
        {
            lock (setLock)
            {
                foreach (var kvp in enlargedEnemies)
                {
                    if (!rolledScale.TryGetValue(kvp.Key, out float expected) || expected <= 0f) continue;

                    var target = Utilities.GetPlayerFromIndex((int)kvp.Value);
                    if (target == null || !target.IsValid) continue;

                    var pawn = target.PlayerPawn?.Value;
                    if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                    var skeleton = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance();
                    if (skeleton == null || Math.Abs(skeleton.Scale - expected) < 0.01f) continue;

                    SkillUtils.ChangePlayerScale(target, expected);
                }
            }
        }

        private static void ResetScale(uint targetIndex, bool notify = false)
        {
            var target = Utilities.GetPlayerFromIndex((int)targetIndex);
            if (target == null || !target.IsValid) return;

            var pawn = target.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.CBodyComponent == null) return;

            SkillUtils.ChangePlayerScale(target, 1);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_CBodyComponent");

            if (!notify) return;

            var targetEvent = PlayerManager.GetPlayerFromEvent(target);
            if (targetEvent != null && targetEvent.IsValid && targetEvent.PawnIsAlive)
                targetEvent.PrintToChat($" {ChatColors.Green}" + targetEvent.GetTranslation("giant_disable_info"));
        }

        private static CCSPlayerController[] GetSelectableEnemies(CCSPlayerController player)
        {
            return [.. SkillUtils.GetSelectableEnemies(player, true)
                .Where(p => PlayerManager.GetPlayerByIndex(p.Index)?.Skill != Skills.Chicken)];
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8ad3ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float minScale = 1.1f, float maxScale = 1.4f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float MinScale { get; set; } = minScale;
            public float MaxScale { get; set; } = maxScale;
        }
    }
}
