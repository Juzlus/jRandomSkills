using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Duplicator : ISkill
    {
        private const Skills skillName = Skills.Duplicator;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void NewRound()
        {
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
                var enemies = Utilities.GetPlayers().Where(p => p != null && p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p => p != null && p.IsValid && p.Index != player.Index && p.PlayerPawn?.Value != null && p.PlayerPawn.Value.IsValid && p.PlayerPawn.Value.Health > 0 && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();

                ConcurrentBag<(string, string)> menuItems = [];
                foreach (var enemy in enemies)
                {
                    var enemyInfo = PlayerManager.GetPlayerByIndex(enemy?.Index);
                    if (enemyInfo == null) continue;

                    var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == enemyInfo.Skill);
                    if (skillData == null) continue;

                    menuItems.Add(($"\u202A{enemy!.PlayerName}\u202C : {player.GetSkillName(skillData.Skill)}", enemy.Index.ToString()));
                }
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;
            if (!player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            string enemyId = commands[0];
            var enemy = Utilities.GetPlayers().FirstOrDefault(p => p.Index.ToString() == enemyId);

            if (enemy == null)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            DuplicateSkill(player, enemy);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && p != player && p.IsValid && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<string> skills = [];
                ConcurrentBag<(string, string)> menuItems = [];
                foreach (var enemy in enemies)
                {
                    var enemyInfo = PlayerManager.GetPlayerByIndex(enemy.Index);
                    if (enemyInfo == null) continue;

                    var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == enemyInfo.Skill);
                    if (skillData == null) continue;
                    
                    skills.Add(skillData.Skill.ToString());
                    menuItems.Add(($"\u202A{enemy.PlayerName}\u202C : {player.GetSkillName(skillData.Skill)}", enemy.Index.ToString()));
                }

                int ctSkills = Event.counterterroristSkills.Count(s => skills.Contains(s.Name));
                int ttSkills = Event.terroristSkills.Count(s => skills.Contains(s.Name));
                
                if ((player.Team == CsTeam.Terrorist && ctSkills == skills.Count) || (player.Team == CsTeam.CounterTerrorist && ttSkills == skills.Count))
                {
                    Event.SetRandomSkill(player);
                    return;
                }

                SkillUtils.CreateMenu(player, menuItems);
                SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName)}",
                    border: !Utilities.GetPlayers().Any(p => p.Team == player.Team && p != player) ? "tb" : "t");
            }
            else
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.SpecialSkill = Skills.None;
            SkillUtils.CloseMenu(player);
        }

        private static void DuplicateSkill(CCSPlayerController player, CCSPlayerController enemy)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            var enemyInfo = PlayerManager.GetPlayerByIndex(enemy.Index);
            if (playerInfo == null || enemyInfo == null) return;

            var enemySkill = enemyInfo.Skill;
            bool ctSkill = Event.counterterroristSkills.Any(s => s.Name == enemySkill.ToString());
            bool ttSkill = Event.terroristSkills.Any(s => s.Name == enemySkill.ToString());

            uint playerIndex = player.Index;
            string enemyName = enemy.PlayerName;

            if ((player.Team == CsTeam.Terrorist && ctSkill) || (player.Team == CsTeam.CounterTerrorist && ttSkill))
            {
                Instance.AddTimer(.1f, () =>
                {
                    var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                    if (player == null || !player.IsValid) return;

                    Instance.SkillAction(skillName.ToString(), "EnableSkill", [player]);
                    player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("thief_incorrect_skill", enemyName));
                }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                return;
            }

            SkillUtils.CloseMenu(player);
            Instance.AddTimer(.1f, () =>
            {
                playerInfo.Skill = enemySkill;
                playerInfo.SpecialSkill = skillName;
                SkillUtils.CloseMenu(player);

                if (SkillsInfo.GetValue<bool>(enemySkill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                    Instance?.AddTimer(Math.Max((float)(Event.GetFreezeTimeEnd() - DateTime.Now).TotalSeconds, 0), () => {
                        var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                        if (player == null || !player.IsValid) return;

                        var dupInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                        if (dupInfo == null || dupInfo.Skill != enemySkill) return;
                        Instance?.SkillAction(enemySkill.ToString(), "EnableSkill", [player]);
                    }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                else
                    Instance?.SkillAction(enemySkill.ToString(), "EnableSkill", [player]);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            player.PrintToChat($" {ChatColors.Green}" + player.GetTranslation("duplicator_player_info", enemy.PlayerName));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffb73b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}