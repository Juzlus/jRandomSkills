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
        private static readonly ConcurrentDictionary<ulong, byte> targetPlayers = [];
        private static readonly ConcurrentDictionary<ulong, bool> lastShot = [];
        private static readonly ConcurrentDictionary<ulong, int> hitPlayer = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                targetPlayers.Clear();
                lastShot.Clear();
                hitPlayer.Clear();
            }
            foreach (var player in Utilities.GetPlayers())
            {
                DisableSkill(player);
                SkillUtils.CloseMenu(player);
            }
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null) return;
            DisableSkill(player);
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;
            foreach (var player in Utilities.GetPlayers())
            {
                if (!SkillUtils.HasMenu(player)) continue;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);

                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();

                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
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

            if (enemy == null)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            targetPlayers.TryAdd(enemy.SteamID, 0);
            playerInfo.SkillUsed = true;
            player.PrintToChat($" {ChatColors.Green}" + player.GetTranslation("carefulbullets_player_info", enemy.PlayerName));
            enemy.PrintToChat($" {ChatColors.Red}" + enemy.GetTranslation("carefulbullets_enemy_info"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void BulletImpact(EventBulletImpact @event)
        {
            var player = @event.Userid;
            if (player == null ||  !player.IsValid) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;

            if (!targetPlayers.ContainsKey(player.SteamID) || !player.PawnIsAlive)
                return;

            ulong steamID = player.SteamID;

            Server.NextFrame(() =>
            {
                if (player == null || !player.IsValid) return;
                if (playerPawn == null || !playerPawn.IsValid) return;

                bool ishitPlayer = hitPlayer.TryGetValue(steamID, out int tick) && tick + 10 >= Server.TickCount;

                if (!lastShot.ContainsKey(steamID))
                {
                    lastShot.TryAdd(steamID, ishitPlayer);

                    Instance.AddTickTimer(1, () =>
                    {
                        if (lastShot.TryRemove(steamID, out bool didHit) && !didHit)
                            SkillUtils.TakeHealth(playerPawn, SkillsInfo.GetValue<int>(skillName, "damageAfterMiss"));
                        lastShot.TryRemove(steamID, out _);
                    });
                }
                else if (ishitPlayer)
                    lastShot.TryUpdate(steamID, true, false);
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

            CCSPlayerController attacker = attackerPawn.Controller.Value.As<CCSPlayerController>();
            if (!targetPlayers.ContainsKey(attacker.SteamID)) return;

            hitPlayer.AddOrUpdate(attacker.SteamID, Server.TickCount, (_, _) => Server.TickCount);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            targetPlayers.TryRemove(player.SteamID, out _);
            lastShot.TryRemove(player.SteamID, out _);
            hitPlayer.TryRemove(player.SteamID, out _);
            SkillUtils.CloseMenu(player);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#db6c35", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int damageAfterMiss = 5) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int DamageAfterMiss { get; set; } = damageAfterMiss;
        }
    }
}