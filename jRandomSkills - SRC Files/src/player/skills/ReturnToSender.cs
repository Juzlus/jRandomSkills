using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class ReturnToSender : ISkill
    {
        private const Skills skillName = Skills.ReturnToSender;
        private static readonly ConcurrentDictionary<nint, byte> playersToSender = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            playersToSender.Clear();
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            int damage = @event.DmgHealth;

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim) || attacker == victim) return;
            var attackerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);
            if (attackerInfo == null || attackerInfo.Skill != skillName) return;

            if (playersToSender.ContainsKey(victim!.Handle))
                return;

            var spawnpoint = SkillUtils.GetSpawnPointVector(victim);
            if (spawnpoint == null) return;

            victim!.PlayerPawn!.Value!.Teleport(spawnpoint);
            playersToSender.TryAdd(victim.Handle, 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            playersToSender.TryRemove(player.Handle, out _);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#a68132", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}