using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class JumpCurse : ISkill
    {
        private const Skills skillName = Skills.JumpCurse;
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

        public static void PlayerJump(EventPlayerJump @event)
        {
            if (cursedPlayers.IsEmpty) return;

            var jumper = PlayerManager.GetPlayerEvent(@event.Userid);
            if (jumper == null || !jumper.IsValid) return;
            if (jumper.Team != CsTeam.Terrorist && jumper.Team != CsTeam.CounterTerrorist) return;

            var jumperPawn = jumper.PlayerPawn?.Value;
            if (jumperPawn == null || !jumperPawn.IsValid) return;
            if (jumperPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || jumperPawn.Health <= 0) return;

            float jumpVelocity = SkillsInfo.GetValue<float>(skillName, "jumpVelocity");

            foreach (var cursedIndex in cursedPlayers.Keys)
            {
                if (cursedIndex == jumper.Index) continue;

                var cursed = PlayerManager.GetPlayerEvent(Utilities.GetPlayerFromIndex((int)cursedIndex));
                if (cursed == null || !cursed.IsValid) continue;
                if (cursed.Index == jumper.Index || cursed.Team != jumper.Team) continue;

                var pawn = cursed.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid) continue;
                if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || pawn.Health <= 0) continue;
                if ((pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) == 0) continue;

                pawn.AbsVelocity.Z = jumpVelocity;
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

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("jumpcurse_player_info", enemy.PlayerName));
            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("jumpcurse_enemy_info"));
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
                    target.PrintToChat($" {ChatColors.Green}" + target.GetTranslation("jumpcurse_disable_info"));
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7ad1c4", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float jumpVelocity = 301f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float JumpVelocity { get; set; } = jumpVelocity;
        }
    }
}
