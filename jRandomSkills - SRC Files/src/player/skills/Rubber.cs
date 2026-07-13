using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Rubber: ISkill
    {
        private const Skills skillName = Skills.Rubber;
        private static readonly ConcurrentDictionary<uint, float> playersToSlow = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            playersToSlow.Clear();
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim) || attacker == victim) return;
            var attackerInfo = PlayerManager.GetPlayerByIndex(attacker!.Index);

            var victimPawn = victim!.PlayerPawn.Value;
            if (victimPawn == null || !victimPawn.IsValid) return;

            var rubberTime = SkillsInfo.GetValue<float>(skillName, "slownessTime");
            if (attackerInfo?.Skill == skillName)
                playersToSlow.AddOrUpdate(victim.Index, Server.TickCount + (64 * rubberTime), (k, v) => Server.TickCount + (64 * rubberTime));
        }

        public static void OnTick()
        {
            foreach(var item in playersToSlow)
            {
                var playerIndex = item.Key;
                var time = item.Value;

                var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                if (player == null || !player.IsValid) continue;

                if (time >= Server.TickCount)
                    ChangeVelocity(player);
                else
                    playersToSlow.TryRemove(item.Key, out _);
            }
        }

        private static void ChangeVelocity(CCSPlayerController player)
        {
            if (player.PlayerPawn == null) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            pawn.VelocityModifier = SkillsInfo.GetValue<float>(skillName, "slownessModifier");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8B4513", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float slownessTime = 2f, float slownessModifier = .2f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float SlownessTime { get; set; } = slownessTime;
            public float SlownessModifier { get; set; } = slownessModifier;
        }
    }
}