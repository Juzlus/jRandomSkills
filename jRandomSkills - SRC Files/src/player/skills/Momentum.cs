using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Momentum : ISkill
    {
        private const Skills skillName = Skills.Momentum;
        private static readonly ConcurrentDictionary<uint, int> stacks = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            stacks.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            stacks[player.Index] = 0;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            stacks.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(attacker) || victim == null || !victim.IsValid || attacker == victim) return;
            if (attacker!.Team == victim.Team) return;
            if (!stacks.TryGetValue(attacker.Index, out int current)) return;

            int maxStacks = SkillsInfo.GetValue<int>(skillName, "maxStacks");
            stacks[attacker.Index] = Math.Min(current + 1, maxStacks);
        }

        public static void OnTick()
        {
            if (stacks.IsEmpty || !SkillUtils.IsHudFrame()) return;

            float perKill = SkillsInfo.GetValue<float>(skillName, "damagePerKill");
            foreach (var (index, count) in stacks)
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(index);
                if (playerInfo == null || playerInfo.Skill != skillName) continue;

                playerInfo.PrintHTML = count > 0
                    ? $"<font color='#FFA500'>+{Math.Round(count * perKill * 100)}%</font>"
                    : null;
            }
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null || stacks.IsEmpty) return;
            if (damagedEntity.DesignerName != "player") return;

            var attackerEnt = damageInfo.Attacker?.Value;
            if (attackerEnt == null || !attackerEnt.IsValid || attackerEnt.DesignerName != "player") return;
            if (attackerEnt.Handle == damagedEntity.Handle) return;

            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);
            if (SkillUtils.IsFriendlyFireBlocked(skillName, damageInfo, victimPawn)) return;

            CCSPlayerPawn attackerPawn = new(attackerEnt.Handle);
            var attacker = PlayerManager.GetPlayerEvent(attackerPawn.Controller?.Value?.As<CCSPlayerController>());
            if (attacker == null || !stacks.TryGetValue(attacker.Index, out int count) || count <= 0) return;

            damageInfo.Damage *= 1f + count * SkillsInfo.GetValue<float>(skillName, "damagePerKill");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffa500", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float damagePerKill = .15f, int maxStacks = 5, bool friendlyFire = false) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float DamagePerKill { get; set; } = damagePerKill;
            public int MaxStacks { get; set; } = maxStacks;
            public bool FriendlyFire { get; set; } = friendlyFire;
        }
    }
}
