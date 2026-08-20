using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Punisher : ISkill
    {
        private const Skills skillName = Skills.Punisher;

        private static readonly ConcurrentDictionary<uint, float> storedDamage = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            storedDamage.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            storedDamage[player.Index] = 0f;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;

            storedDamage.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            storedDamage.TryRemove(playerIndex, out _);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            if (storedDamage.ContainsKey(victim.Index))
                storedDamage[victim.Index] = 0f;
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (storedDamage.IsEmpty) return;

            if (damagedEntity == null || !damagedEntity.IsValid || damageInfo == null) return;

            var victimPawn = damagedEntity.As<CCSPlayerPawn>();
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.DesignerName != "player") return;

            var victimController = victimPawn.Controller.Value;
            if (victimController == null || !victimController.IsValid) return;

            var victim = victimController.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid) return;

            if (SkillUtils.IsFriendlyFireBlocked(skillName, damageInfo, victimPawn)) return;

            uint victimIndex = PlayerManager.GetPlayerEvent(victim)?.Index ?? victim.Index;

            var attacker = damageInfo.Attacker?.Value?.As<CCSPlayerPawn>();
            var attackerController = attacker?.Controller?.Value?.As<CCSPlayerController>();
            uint? attackerIndex = attackerController != null && attackerController.IsValid
                ? PlayerManager.GetPlayerEvent(attackerController)?.Index ?? attackerController.Index
                : null;

            if (attackerIndex.HasValue && attackerIndex.Value != victimIndex && storedDamage.TryGetValue(attackerIndex.Value, out float banked) && banked > 0f)
            {
                storedDamage[attackerIndex.Value] = 0f;
                damageInfo.Damage += banked;

                var puncher = CounterStrikeSharp.API.Utilities.GetPlayerFromIndex((int)attackerIndex.Value);
                var puncherPawn = puncher?.PlayerPawn?.Value;

                if (puncherPawn != null && puncherPawn.IsValid && puncherPawn.Health > 0)
                    SkillUtils.SetHealth(puncherPawn, puncherPawn.Health + (int)MathF.Round(banked));

                var puncherEvent = PlayerManager.GetPlayerFromEvent(puncher);
                if (puncherEvent != null && puncherEvent.IsValid)
                    puncherEvent.PrintToChat($" {ChatColors.Green}{puncherEvent.GetTranslation("punisher_released_info", (int)MathF.Round(banked))}");
            }

            if (!storedDamage.ContainsKey(victimIndex)) return;
            if (attackerIndex.HasValue && attackerIndex.Value == victimIndex) return;

            float ratio = SkillsInfo.GetValue<float>(skillName, "storePercent");
            float cap = SkillsInfo.GetValue<float>(skillName, "maxStored");

            float stored = storedDamage[victimIndex] + (damageInfo.Damage * ratio);
            storedDamage[victimIndex] = MathF.Min(stored, cap);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffb347", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float storePercent = .5f, float maxStored = 100f, bool friendlyFire = true) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float StorePercent { get; set; } = storePercent;
            public float MaxStored { get; set; } = maxStored;
        public bool FriendlyFire { get; set; } = friendlyFire;
        }
    }
}
