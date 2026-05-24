using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Thief : ISkill
    {
        private const Skills skillName = Skills.Thief;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;
            
            foreach (var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid && SkillUtils.HasMenu(p)))
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill != skillName) continue;

                var enemies = Utilities.GetPlayers().Where(p =>
                    p != null &&
                    p.IsValid)
                .Select(p => PlayerManager.GetPlayerEvent(p))
                .Where(p =>
                    p != null &&
                    p.IsValid &&
                    p.Team != player.Team &&
                    p.PlayerPawn?.Value != null &&
                    p.PlayerPawn.Value.IsValid &&
                    p.PlayerPawn.Value.Health > 0 &&
                    !p.IsHLTV &&
                    p.Team != CsTeam.Spectator
                    && p.Team != CsTeam.None
                ).ToArray();

                ConcurrentBag<(string, string)> menuItems = [];
                foreach (var enemy in enemies)
                {
                    var enemyInfo = PlayerManager.GetPlayerByIndex(enemy.Index);
                    if (enemyInfo == null) continue;
                    
                    var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == enemyInfo.Skill);
                    if (skillData == null) continue;
                    
                    menuItems.Add(($"{enemy.PlayerName} : {player.GetSkillName(skillData.Skill)}", enemy.Index.ToString()));
                }
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid))
                SkillUtils.CloseMenu(player);
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;
            if (player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            if (commands == null || commands.Length == 0)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            string enemyId = commands[0];
            if (!uint.TryParse(enemyId, out uint enemyIndex))
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);
            if (enemy == null || !enemy.IsValid)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            StealSkill(player, enemy);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var enemies = Utilities.GetPlayers()
                .Where(p => p != null
                    && p.PawnIsAlive 
                    && p.Team != player.Team 
                    && p.IsValid
                    && p.Team != CsTeam.Spectator 
                    && p.Team != CsTeam.None)
                .ToArray();

            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = [];
                foreach (var enemy in enemies)
                {
                    var enemyInfo = PlayerManager.GetPlayerByIndex(enemy.Index);
                    if (enemyInfo == null) continue;
                    
                    var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == enemyInfo.Skill);
                    if (skillData == null) continue;
                    
                    menuItems.Add(($"{enemy.PlayerName} : {player.GetSkillName(skillData.Skill)}", enemy.Index.ToString()));
                }

                SkillUtils.CreateMenu(player, menuItems);
                SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName)}",
                    border: !Utilities.GetPlayers().Any(p => p != null && p.Team == player.Team && p != player) ? "tb" : "t");
            }
            else
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            
            playerInfo.SpecialSkill = Skills.None;
            SkillUtils.CloseMenu(player);
        }

        private static void StealSkill(CCSPlayerController player, CCSPlayerController enemy)
        {
            if (player == null || enemy == null) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            var enemyInfo = PlayerManager.GetPlayerByIndex(enemy.Index);
            if (playerInfo == null || enemyInfo == null) return;
            
            var enemySkill = enemyInfo.Skill;
            bool ctSkill = Event.counterterroristSkills.Any(s => s.Name == enemySkill.ToString());
            bool ttSkill = Event.terroristSkills.Any(s => s.Name == enemySkill.ToString());

            uint playerIndex = player.Index;
            uint enemyIndex = enemy.Index;

            if ((player.Team == CsTeam.Terrorist && ctSkill) || (player.Team == CsTeam.CounterTerrorist && ttSkill))
            {
                Instance.AddTimer(.1f, () =>
                {
                    var e = Utilities.GetPlayerFromIndex((int)enemyIndex);
                    if (e == null || !e.IsValid) return;

                    var p = Utilities.GetPlayerFromIndex((int)playerIndex);
                    if (p == null || !p.IsValid) return;

                    Instance.SkillAction(skillName.ToString(), "EnableSkill", [p]);
                    p.PrintToChat($" {ChatColors.Red}" + p.GetTranslation("thief_incorrect_skill", e.PlayerName));
                }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                return;
            }

            SkillUtils.CloseMenu(player);
            Instance.AddTimer(.1f, () =>
            {
                var e = Utilities.GetPlayerFromIndex((int)enemyIndex);
                if (e == null || !e.IsValid) return;

                var p = Utilities.GetPlayerFromIndex((int)playerIndex);
                if (p == null || !p.IsValid) return;

                var pInfo = PlayerManager.GetPlayerByIndex(p.Index);
                if (pInfo == null) return;

                pInfo.Skill = enemySkill;
                pInfo.SpecialSkill = skillName;

                SkillUtils.CloseMenu(p);
                Instance.SkillAction(enemySkill.ToString(), "EnableSkill", [p]);

                p.PrintToChat($" {ChatColors.Green}" + p.GetTranslation("thief_player_info", e.PlayerName));

                if (SkillsInfo.GetValue<bool>(enemySkill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                {
                    float delay = Math.Max((float)(Event.GetFreezeTimeEnd() - DateTime.Now).TotalSeconds, 0);
                    Instance?.AddTimer(delay, () =>
                    {
                        var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                        if (player == null || !player.IsValid) return;

                        var info = PlayerManager.GetPlayerByIndex(player!.Index);
                        if (info?.Skill == enemySkill)
                            Instance?.SkillAction(enemySkill.ToString(), "EnableSkill", [player]);
                    }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                }
                else
                    Instance?.SkillAction(enemySkill.ToString(), "EnableSkill", [p]);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

            Instance.AddTimer(.1f, () =>
            {
                var eInfo = PlayerManager.GetPlayerByIndex(enemyIndex);
                if (eInfo == null) return;

                var e = Utilities.GetPlayerFromIndex((int)enemyIndex);
                if (e == null || !e.IsValid) return;

                Instance.SkillAction(enemySkill.ToString(), "DisableSkill", [e]);
                
                eInfo.SpecialSkill = enemySkill;
                eInfo.Skill = Skills.None;
                e.PrintToChat($" {ChatColors.Red}" + e.GetTranslation("thief_enemy_info"));
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#adaec7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}