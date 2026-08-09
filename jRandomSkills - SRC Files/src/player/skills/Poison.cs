using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Poison : ISkill
    {
        private const Skills skillName = Skills.Poison;
        private const string poisonSound = "Player.DamageBody.Victim";
        private static readonly ConcurrentDictionary<uint, byte> poisonedPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];
        private static readonly ConcurrentDictionary<uint, uint> targetToPlayer = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lock (setLock)
            {
                poisonedPlayers.TryRemove(playerIndex, out _);
                targetToPlayer.TryRemove(playerIndex, out _);

                if (playersToTarget.TryRemove(playerIndex, out uint ownTarget))
                    targetToPlayer.TryRemove(ownTarget, out _);

                foreach (var kvp in playersToTarget)
                    if (kvp.Value == playerIndex)
                        playersToTarget.TryRemove(kvp.Key, out _);
            }
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                poisonedPlayers.Clear();
                playersToTarget.Clear();
                targetToPlayer.Clear();
            }
        }

        public static void OnTick()
        {
            int cooldown = Math.Max(1, (int)(64 * SkillsInfo.GetValue<float>(skillName, "Cooldown")));

            if (Server.TickCount % cooldown == 0)
            {
                int cooldown2 = cooldown * 2;

                foreach (var playerIndex in poisonedPlayers.Keys)
                {
                    var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                    if (player == null || !player.IsValid || player.PlayerPawn == null) continue;

                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) continue;

                    if (Jester.IsActiveJester(playerIndex)) continue;

                    if (pawn.Health <= SkillsInfo.GetValue<int>(skillName, "MinHealth")) continue;

                    SkillUtils.TakeHealth(pawn, SkillsInfo.GetValue<int>(skillName, "Damage"), GetSkillOwner(playerIndex), KillfeedIcons.Spray);

                    if (Server.TickCount % cooldown2 == 0)
                        SkillUtils.EmitSoundToPlayer(player, poisonSound, SkillsInfo.GetValue<float>(skillName, "soundVolume"));
                }
            }

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

            poisonedPlayers.TryAdd(enemy.Index, 0);
            playersToTarget[player.Index] = enemy.Index;
            targetToPlayer[enemy.Index] = player.Index;
            playerInfo.SkillUsed = true;

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("poison_player_info", enemy.PlayerName));
            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("poison_enemy_info"));
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
                playerEvent.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        private static CCSPlayerController? GetSkillOwner(uint targetIndex)
        {
            if (!targetToPlayer.TryGetValue(targetIndex, out uint ownerIndex)) return null;

            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex);
            return owner != null && owner.IsValid ? owner : null;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            targetToPlayer.TryRemove(player.Index, out _);

            if (playersToTarget.TryRemove(player.Index, out uint targetIndex))
            {
                poisonedPlayers.TryRemove(targetIndex, out _);
                targetToPlayer.TryRemove(targetIndex, out _);

                var target = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)targetIndex));
                if (target != null && target.IsValid && target.PawnIsAlive && !SkillUtils.IsFreezeTime())
                    target.PrintToChat($" {ChatColors.Green}" + target.GetTranslation("poison_disable_info"));
            }

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            poisonedPlayers.TryRemove(player.Index, out _);
            targetToPlayer.TryRemove(player.Index, out _);

            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#902eff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Common, float cooldown = .85f, int damage = 1, int minHealth = 30, float soundVolume = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float SoundVolume { get; set; } = soundVolume;
            public int Damage { get; set; } = damage;
            public float Cooldown { get; set; } = cooldown;
            public int MinHealth { get; set; } = minHealth;
        }
    }
}
