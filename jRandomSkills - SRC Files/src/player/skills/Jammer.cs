using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Jammer : ISkill
    {
        private const Skills skillName = Skills.Jammer;
        private static readonly ConcurrentDictionary<uint, byte> jammedPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> jammerToTarget = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var playerIndex in jammedPlayers.Keys)
                {
                    var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                    if (player == null || !player.IsValid) continue;
                    SetCrosshair(player, true);
                }
                jammedPlayers.Clear();
                jammerToTarget.Clear();
            }

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
                var enemies = Utilities.GetPlayers().Where(p =>p != null &&p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p =>p != null &&p.IsValid &&p.Team != player.Team &&p.PlayerPawn?.Value != null &&p.PlayerPawn.Value.IsValid &&p.PlayerPawn.Value.Health > 0 &&!p.IsHLTV &&p.Team != CsTeam.Spectator&& p.Team != CsTeam.None).ToArray();

                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Index.ToString())));
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playerInfo.SkillUsed)
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("areareaper_used_info")}");
                return;
            }

            string enemyId = commands[0];
            if (!uint.TryParse(enemyId, out uint enemyIndex)) { player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index")); return; }
            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);

            if (enemy == null)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            jammedPlayers.TryAdd(enemy.Index, 0);
            jammerToTarget[player.Index] = enemy.Index;

            SetCrosshair(enemy, false);
            playerInfo.SkillUsed = true;

            player.PrintToChat($" {ChatColors.Green}" + player.GetTranslation("jammer_player_info", enemy.PlayerName));
            enemy.PrintToChat($" {ChatColors.Red}" + enemy.GetTranslation("jammer_enemy_info"));
        }

        private static void SetCrosshair(CCSPlayerController player, bool enabled)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;
            pawn.HideHUD = (uint)(enabled
                ? (pawn.HideHUD & ~(1 << 8))
                : (pawn.HideHUD | (1 << 8)));
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_iHideHUD");
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var enemies = Utilities.GetPlayers().Where(p =>p != null &&p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p =>p != null &&p.IsValid &&p.Team != player.Team &&p.PlayerPawn?.Value != null &&p.PlayerPawn.Value.IsValid &&p.PlayerPawn.Value.Health > 0 &&!p.IsHLTV &&p.Team != CsTeam.Spectator&& p.Team != CsTeam.None).ToArray();
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
            if (jammerToTarget.TryRemove(player.Index, out uint targetIndex))
            {
                var target = Utilities.GetPlayerFromIndex((int)targetIndex);
                if (target != null && target.IsValid)
                {
                    SetCrosshair(target, true);
                    if (target.PawnIsAlive && !SkillUtils.IsFreezeTime())
                        target.PrintToChat($" {ChatColors.Green}" + target.GetTranslation("jammer_disable_info"));
                }
                jammedPlayers.TryRemove(targetIndex, out _);
            }

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            SetCrosshair(player, true);
            jammedPlayers.TryRemove(player.Index, out _);
            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#42f5a7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}