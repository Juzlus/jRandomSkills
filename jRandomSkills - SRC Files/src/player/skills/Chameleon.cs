using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Chameleon : ISkill
    {
        private const Skills skillName = Skills.Chameleon;

        private static readonly ConcurrentDictionary<uint, byte> holders = [];
        private static jRandomSkills? registeredOn;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));

            if (ReferenceEquals(registeredOn, Instance)) return;

            registeredOn = Instance;
            Instance.RegisterEventHandler<EventPlayerDeath>(OnAnyDeath);
        }

        public static void NewRound()
        {
            holders.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            holders[player.Index] = 0;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            holders.TryRemove(player.Index, out _);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            holders.TryRemove(playerIndex, out _);
        }

        private static HookResult OnAnyDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            try
            {
                if (holders.IsEmpty) return HookResult.Continue;

                var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
                if (attacker == null || !attacker.IsValid) return HookResult.Continue;

                var victim = PlayerManager.GetPlayerEvent(@event.Userid);
                if (victim == null || !victim.IsValid) return HookResult.Continue;

                if (attacker.Index == victim.Index) return HookResult.Continue;
                if (attacker.Team == victim.Team) return HookResult.Continue;
                if (!holders.ContainsKey(attacker.Index)) return HookResult.Continue;

                var victimInfo = PlayerManager.GetPlayerByIndex(victim.Index);
                if (victimInfo == null || victimInfo.IsDrawing) return HookResult.Continue;

                CopySkill(attacker, victim.PlayerName, victimInfo.Skill);
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[jRandomSkills] Chameleon.OnAnyDeath failed: {ex.Message}");
            }

            return HookResult.Continue;
        }

        private static void CopySkill(CCSPlayerController player, string victimName, Skills victimSkill)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);

            if (!CanCopy(player, victimSkill, out string? blockReason))
            {
                if (blockReason != null && playerEvent != null && playerEvent.IsValid)
                    playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation(blockReason, victimName));

                return;
            }

            uint playerIndex = player.Index;
            Skills previousSkill = playerInfo.Skill;

            if (!holders.TryRemove(playerIndex, out _)) return;

            Instance.AddTimer(.1f, () =>
            {
                var target = Utilities.GetPlayerFromIndex((int)playerIndex);
                if (target == null || !target.IsValid) return;

                var targetInfo = PlayerManager.GetPlayerByIndex(playerIndex);
                if (targetInfo == null || targetInfo.Skill != previousSkill) return;

                Instance.SkillAction(previousSkill.ToString(), "DisableSkill", [target]);

                targetInfo.Skill = victimSkill;
                targetInfo.SpecialSkill = skillName;
                targetInfo.SkillChance = null;

                if (target.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

                if (SkillsInfo.GetValue<bool>(victimSkill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                    Instance.AddTimer(Math.Max((float)(Event.GetFreezeTimeEnd() - DateTime.Now).TotalSeconds, 0), () =>
                    {
                        var heir = Utilities.GetPlayerFromIndex((int)playerIndex);
                        if (heir == null || !heir.IsValid) return;

                        if (PlayerManager.GetPlayerByIndex(playerIndex)?.Skill != victimSkill) return;
                        Instance.SkillAction(victimSkill.ToString(), "EnableSkill", [heir]);
                    }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                else
                    Instance.SkillAction(victimSkill.ToString(), "EnableSkill", [target]);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

            if (playerEvent != null && playerEvent.IsValid)
                playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("chameleon_copied_info", player.GetSkillName(victimSkill)));
        }

        private static bool CanCopy(CCSPlayerController player, Skills victimSkill, out string? blockReason)
        {
            blockReason = null;

            if (victimSkill == Skills.None || victimSkill == skillName) return false;
            if (SkillData.GetInfo(victimSkill) == null) return false;

            bool ctOnly = Event.counterterroristSkills.Any(s => s.Name == victimSkill.ToString());
            bool ttOnly = Event.terroristSkills.Any(s => s.Name == victimSkill.ToString());

            if ((player.Team == CsTeam.Terrorist && ctOnly) || (player.Team == CsTeam.CounterTerrorist && ttOnly))
            {
                blockReason = "chameleon_wrong_team_info";
                return false;
            }

            if (!player.IsBot)
            {
                string permission = SkillsInfo.GetValue<string>(victimSkill, "requiredPermission");
                if (!string.IsNullOrEmpty(permission) && !AdminManager.PlayerHasPermissions(player, permission))
                    return false;
            }

            if (!SkillsInfo.GetValue<bool>(victimSkill, "needsTeammates")) return true;

            return PlayerManager.GetTickPlayers().Any(p =>
                p != null && p.IsValid && p.Index != player.Index && p.Team == player.Team
                && p.PlayerPawn?.Value != null && p.PlayerPawn.Value.IsValid && p.PlayerPawn.Value.Health > 0);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5fd98a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Rare) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
