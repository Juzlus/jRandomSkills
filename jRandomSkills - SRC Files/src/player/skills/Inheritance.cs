using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Inheritance : ISkill
    {
        private const Skills skillName = Skills.Inheritance;

        private static readonly ConcurrentDictionary<uint, byte> holders = [];
        private static readonly ConcurrentDictionary<uint, FallenInfo> fallen = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            holders.Clear();
            fallen.Clear();

            foreach (var player in PlayerManager.GetTickPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            holders[player.Index] = 0;
            RefreshMenu(player);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;

            holders.TryRemove(player.Index, out _);
            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            holders.TryRemove(playerIndex, out _);
            fallen.TryRemove(playerIndex, out _);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            holders.TryRemove(victim.Index, out _);

            var victimInfo = PlayerManager.GetPlayerByIndex(victim.Index);
            if (victimInfo == null || victimInfo.IsDrawing) return;
            if (victimInfo.Skill == Skills.None || victimInfo.Skill == skillName) return;
            if (SkillData.GetInfo(victimInfo.Skill) == null) return;

            fallen[victim.Index] = new FallenInfo
            {
                PlayerName = victim.PlayerName,
                Skill = victimInfo.Skill,
                Team = victim.Team,
            };

            foreach (uint holderIndex in holders.Keys)
            {
                var holder = Utilities.GetPlayerFromIndex((int)holderIndex);
                if (holder == null || !holder.IsValid || holder.Team != victim.Team) continue;

                RefreshMenu(holder);
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo?.Skill != skillName) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (commands == null || commands.Length == 0 || !uint.TryParse(commands[0], out uint fallenIndex))
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            if (!fallen.TryGetValue(fallenIndex, out var fallenInfo) || !CanInherit(player, fallenInfo))
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            InheritSkill(player, fallenInfo);
        }

        private static void InheritSkill(CCSPlayerController player, FallenInfo fallenInfo)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            uint playerIndex = player.Index;
            Skills inheritedSkill = fallenInfo.Skill;

            holders.TryRemove(playerIndex, out _);
            SkillUtils.CloseMenu(player);

            Instance.AddTimer(.1f, () =>
            {
                var target = Utilities.GetPlayerFromIndex((int)playerIndex);
                if (target == null || !target.IsValid) return;

                var targetInfo = PlayerManager.GetPlayerByIndex(playerIndex);
                if (targetInfo == null || targetInfo.Skill != skillName) return;

                targetInfo.Skill = inheritedSkill;
                targetInfo.SpecialSkill = skillName;

                SkillUtils.CloseMenu(target);

                if (SkillsInfo.GetValue<bool>(inheritedSkill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                    Instance?.AddTimer(Math.Max((float)(Event.GetFreezeTimeEnd() - DateTime.Now).TotalSeconds, 0), () =>
                    {
                        var heir = Utilities.GetPlayerFromIndex((int)playerIndex);
                        if (heir == null || !heir.IsValid) return;

                        if (PlayerManager.GetPlayerByIndex(playerIndex)?.Skill != inheritedSkill) return;
                        Instance?.SkillAction(inheritedSkill.ToString(), "EnableSkill", [heir]);
                    }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                else
                    Instance?.SkillAction(inheritedSkill.ToString(), "EnableSkill", [target]);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("inheritance_player_info", fallenInfo.PlayerName));
        }

        private static void RefreshMenu(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            if (PlayerManager.GetPlayerByIndex(player.Index)?.Skill != skillName) return;

            ConcurrentBag<(string, string)> menuItems = [];
            foreach (var (fallenIndex, fallenInfo) in fallen)
            {
                if (fallenIndex == player.Index) continue;
                if (!CanInherit(player, fallenInfo)) continue;

                menuItems.Add(($"\u202A{fallenInfo.PlayerName}\u202C : {player.GetSkillName(fallenInfo.Skill)}", fallenIndex.ToString()));
            }

            if (menuItems.IsEmpty) return;

            if (SkillUtils.HasMenu(player))
                SkillUtils.UpdateMenu(player, menuItems);
            else
                SkillUtils.CreateMenu(player, menuItems);
        }

        private static bool CanInherit(CCSPlayerController player, FallenInfo fallenInfo)
        {
            if (fallenInfo.Team != player.Team) return false;

            if (!player.IsBot)
            {
                string permission = SkillsInfo.GetValue<string>(fallenInfo.Skill, "requiredPermission");
                if (!string.IsNullOrEmpty(permission) && !AdminManager.PlayerHasPermissions(player, permission))
                    return false;
            }

            if (!SkillsInfo.GetValue<bool>(fallenInfo.Skill, "needsTeammates")) return true;

            return PlayerManager.GetTickPlayers().Any(p =>
                p != null && p.IsValid && p.Index != player.Index && p.Team == player.Team
                && p.PlayerPawn?.Value != null && p.PlayerPawn.Value.IsValid && p.PlayerPawn.Value.Health > 0);
        }

        private class FallenInfo
        {
            public required string PlayerName { get; set; }
            public required Skills Skill { get; set; }
            public required CsTeam Team { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#c9a227", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = true, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Rare) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
