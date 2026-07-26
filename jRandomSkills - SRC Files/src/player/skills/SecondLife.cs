using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class SecondLife : ISkill
    {
        private const Skills skillName = Skills.SecondLife;
        private const float DefaultHeadshotMultiplier = 4f;
        private static readonly ConcurrentDictionary<nint, int> usedThisRound = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            usedThisRound.Clear();
        }

        // Must intercept pre-damage: player_hurt fires after the death is committed, so a
        // lethal hit (esp. fall damage) can't be undone there. Cancelling the blow here keeps the respawn clean.
        public static void OnTakeDamage(DynamicHook h)
        {
            var victimEntity = h.GetParam<CEntityInstance>(0);
            var info = h.GetParam<CTakeDamageInfo>(1);
            if (victimEntity == null || !victimEntity.IsValid || info == null) return;

            var victimPawn = victimEntity.As<CCSPlayerPawn>();
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.DesignerName != "player") return;

            var victimController = victimPawn.Controller.Value;
            if (victimController == null || !victimController.IsValid) return;

            var victim = victimController.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid || !victim.PawnIsAlive) return;

            var victimInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(victim)?.Index ?? victim.Index));
            if (victimInfo == null || victimInfo.Skill != skillName) return;

            if (SkillUtils.IsFriendlyFireBlocked(info, victimPawn)) return;

            float effectiveDamage = info.Damage;
            if (SkillUtils.GetHitGroup(info) == HitGroup_t.HITGROUP_HEAD)
                effectiveDamage *= GetHeadshotMultiplier(info);

            if (effectiveDamage < victimPawn.Health) return;

            if (usedThisRound.TryGetValue(victim.Handle, out int savedTick))
            {
                if (savedTick == Server.TickCount)
                    info.Damage = 0;
                return;
            }

            if (TryConsumeRevive(victim, victimPawn))
                info.Damage = 0;
        }

        private static float GetHeadshotMultiplier(CTakeDamageInfo info)
        {
            var ability = info.Ability?.Value;
            if (ability == null || !ability.IsValid) return DefaultHeadshotMultiplier;

            var weapon = ability.As<CCSWeaponBase>();
            if (weapon == null || !weapon.IsValid) return DefaultHeadshotMultiplier;

            var vdata = weapon.GetVData<CCSWeaponBaseVData>();
            if (vdata == null || vdata.HeadshotMultiplier <= 0) return DefaultHeadshotMultiplier;

            return vdata.HeadshotMultiplier;
        }

        public static bool TryConsumeRevive(CCSPlayerController? victim, CCSPlayerPawn? victimPawn)
        {
            if (victim == null || !victim.IsValid) return false;
            if (victimPawn == null || !victimPawn.IsValid) return false;

            if (SkillUtils.IsFreezeTime()) return false;

            lock (setLock)
            {
                if (usedThisRound.ContainsKey(victim.Handle)) return false;

                var spawnpoint = SkillUtils.GetSpawnPointVector(victim);
                if (spawnpoint == null) return false; // no clean respawn point -> let the normal death happen

                usedThisRound.TryAdd(victim.Handle, Server.TickCount);

                victimPawn.Health = SkillsInfo.GetValue<int>(skillName, "startHealth");
                Utilities.SetStateChanged(victimPawn, "CBaseEntity", "m_iHealth");

                Server.NextFrame(() =>
                {
                    if (victim == null || !victim.IsValid) return;
                    if (SkillUtils.IsFreezeTime()) return;
                    var pawn = victim.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) return;

                    pawn.Teleport(spawnpoint, null, new Vector(0, 0, 0));
                });

                return true;
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SetHealth(player, SkillsInfo.GetValue<int>(skillName, "startHealth"));
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            usedThisRound.TryRemove(player.Handle, out _);
            if (player.PlayerPawn.Value == null) return;
            SetHealth(player, Math.Min(player.PlayerPawn.Value.Health + SkillsInfo.GetValue<int>(skillName, "startHealth"), 100));
        }

        private static void SetHealth(CCSPlayerController player, int health)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            pawn.Health = health;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d41c1c", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int startHealth = 50) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int StartHealth { get; set; } = startHealth;
        }
    }
}
