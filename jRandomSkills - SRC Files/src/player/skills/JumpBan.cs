using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class JumpBan : ISkill
    {
        private const Skills skillName = Skills.JumpBan;
        public static readonly ConcurrentDictionary<CCSPlayerPawn, int> bannedPlayers = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            bannedPlayers.Clear();
            foreach (var player in Utilities.GetPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void PlayerJump(EventPlayerJump @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            if (!bannedPlayers.TryGetValue(player.PlayerPawn.Value, out _)) return;
            bannedPlayers.AddOrUpdate(player.PlayerPawn.Value, Server.TickCount + 10, (k, v) => Server.TickCount + 10);
        }

        public static void OnTick()
        {
            foreach (var item in bannedPlayers)
            {
                var pawn = item.Key;
                var time = item.Value;
                if (time > Server.TickCount)
                    if (pawn != null && pawn.IsValid)
                        pawn.AbsVelocity.Z = -100;
            }

            if (Server.TickCount % 32 != 0) return;
            foreach (var player in Utilities.GetPlayers())
            {
                if (!SkillUtils.HasMenu(player)) continue;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);

                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();

                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Index.ToString())));
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            if (playerInfo.SkillUsed)
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("areareaper_used_info")}");
                return;
            }

            string enemyId = commands[0];
            var enemy = Utilities.GetPlayers().FirstOrDefault(p => p.Team != player.Team && p.Index.ToString() == enemyId);

            if (enemy == null || !enemy.IsValid || enemy.PlayerPawn.Value == null || !enemy.PlayerPawn.Value.IsValid)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            bannedPlayers.AddOrUpdate(enemy.PlayerPawn.Value, 0, (k, v) => 0);
            playerInfo.SkillUsed = true;
            player.PrintToChat($" {ChatColors.Green}" + player.GetTranslation("jumpban_player_info", enemy.PlayerName));
            enemy.PrintToChat($" {ChatColors.Red}" + enemy.GetTranslation("jumpban_enemy_info"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Index.ToString())));
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            bannedPlayers.TryRemove(player.PlayerPawn.Value, out _);
            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#b01e5d", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}