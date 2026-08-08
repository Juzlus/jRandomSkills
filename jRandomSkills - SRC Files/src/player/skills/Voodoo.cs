using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Voodoo : ISkill
    {
        private const Skills skillName = Skills.Voodoo;

        private static readonly ConcurrentDictionary<uint, uint> ownerToVictim = [];
        private static readonly ConcurrentDictionary<uint, byte> reflecting = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            ownerToVictim.Clear();
            reflecting.Clear();

            foreach (var player in PlayerManager.GetTickPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            ownerToVictim.TryRemove(playerIndex, out _);
            reflecting.TryRemove(playerIndex, out _);

            foreach (var kvp in ownerToVictim)
                if (kvp.Value == playerIndex)
                    ownerToVictim.TryRemove(kvp.Key, out _);
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

            ownerToVictim.TryRemove(player.Index, out _);
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

            ownerToVictim[player.Index] = enemy.Index;
            playerInfo.SkillUsed = true;

            playerEvent.PrintToChat($" {ChatColors.Green}{playerEvent.GetTranslation("voodoo_player_info", enemy.PlayerName)}");
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            if (ownerToVictim.IsEmpty) return;

            var owner = PlayerManager.GetPlayerEvent(@event.Userid);
            if (owner == null || !owner.IsValid) return;

            if (!ownerToVictim.TryGetValue(owner.Index, out uint victimIndex)) return;
            if (@event.DmgHealth <= 0) return;

            if (!reflecting.TryAdd(owner.Index, 0)) return;

            try
            {
                var victim = Utilities.GetPlayerFromIndex((int)victimIndex);
                if (victim == null || !victim.IsValid) return;

                var victimPawn = victim.PlayerPawn?.Value;
                if (victimPawn == null || !victimPawn.IsValid || victimPawn.Health <= 0) return;
                if (victimPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

                int reflected = (int)MathF.Round(@event.DmgHealth * SkillsInfo.GetValue<float>(skillName, "reflectPercent"));
                if (reflected < 1) return;

                SkillUtils.TakeHealth(victimPawn, reflected, owner, KillfeedIcons.Fist);
            }
            finally
            {
                reflecting.TryRemove(owner.Index, out _);
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7b2d8b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Rare, float reflectPercent = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float ReflectPercent { get; set; } = reflectPercent;
        }
    }
}
