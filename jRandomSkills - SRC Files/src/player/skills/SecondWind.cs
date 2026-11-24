using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class SecondWind : ISkill
    {
        private const Skills skillName = Skills.SecondWind;
        private const int ticksPerSecond = 64;

        // Players that already used their wind this round, and the tick until which they resist damage.
        private static readonly ConcurrentDictionary<uint, byte> used = [];
        private static readonly ConcurrentDictionary<uint, int> resistUntil = [];

        public static void LoadSkill()
        {
            Localization.RegisterDescriptionArgs(skillName, () =>
            [
                SkillsInfo.GetValue<int>(skillName, "healthThreshold"),
                SkillsInfo.GetValue<int>(skillName, "healthToAdd"),
                (int)MathF.Round(Math.Clamp(SkillsInfo.GetValue<float>(skillName, "reduction"), 0f, 1f) * 100f),
                SkillsInfo.GetValue<float>(skillName, "resistSeconds").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            ]);
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            used.Clear();
            resistUntil.Clear();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            used.TryRemove(player.Key(), out _);
            resistUntil.TryRemove(player.Key(), out _);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            used.TryRemove(playerIndex, out _);
            resistUntil.TryRemove(playerIndex, out _);
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || !damagedEntity.IsValid || damageInfo == null) return;

            var victimPawn = damagedEntity.As<CCSPlayerPawn>();
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.DesignerName != "player") return;
            if (victimPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || victimPawn.Health <= 0) return;

            var victimController = victimPawn.Controller.Value;
            if (victimController == null || !victimController.IsValid) return;

            var controller = victimController.As<CCSPlayerController>();
            if (controller == null || !controller.IsValid) return;

            var victim = PlayerManager.GetPlayerEvent(controller) ?? controller;
            if (PlayerManager.GetPlayer(victim)?.Skill != skillName) return;
            if (SkillUtils.IsFriendlyFireBlocked(damageInfo, victimPawn)) return;

            uint key = victim.Key();
            int tick = Server.TickCount;
            float reduction = Math.Clamp(SkillsInfo.GetValue<float>(skillName, "reduction"), 0f, 1f);

            // The wind is already blowing: keep resisting until it runs out.
            if (resistUntil.TryGetValue(key, out int until) && tick < until)
            {
                damageInfo.Damage *= 1f - reduction;
                return;
            }

            if (used.ContainsKey(key)) return;
            if (victimPawn.Health - damageInfo.Damage > SkillsInfo.GetValue<int>(skillName, "healthThreshold")) return;
            if (!used.TryAdd(key, 0)) return;

            SkillUtils.AddHealth(victimPawn, SkillsInfo.GetValue<int>(skillName, "healthToAdd"));
            resistUntil[key] = tick + (int)(Math.Max(0f, SkillsInfo.GetValue<float>(skillName, "resistSeconds")) * ticksPerSecond);
            damageInfo.Damage *= 1f - reduction;

            victim.PrintToChat($" {ChatColors.Green}{victim.GetTranslation("secondwind_triggered")}");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7fd6ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int healthThreshold = 30, int healthToAdd = 40, float reduction = .5f, float resistSeconds = 3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int HealthThreshold { get; set; } = healthThreshold;
            public int HealthToAdd { get; set; } = healthToAdd;
            public float Reduction { get; set; } = reduction;
            public float ResistSeconds { get; set; } = resistSeconds;
        }
    }
}
