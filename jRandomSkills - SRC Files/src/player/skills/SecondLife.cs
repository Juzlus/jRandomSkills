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

            if (info.Damage < victimPawn.Health) return;

            if (usedThisRound.TryGetValue(victim.Handle, out int savedTick))
            {
                if (savedTick == Server.TickCount)
                    info.Damage = 0;
                return;
            }

            lock (setLock)
            {
                if (usedThisRound.ContainsKey(victim.Handle)) return;

                var spawnpoint = SkillUtils.GetSpawnPointVector(victim);
                if (spawnpoint == null) return; // no clean respawn point -> let the normal death happen

                usedThisRound.TryAdd(victim.Handle, Server.TickCount);
                info.Damage = 0;

                victimPawn.Health = SkillsInfo.GetValue<int>(skillName, "startHealth");
                Utilities.SetStateChanged(victimPawn, "CBaseEntity", "m_iHealth");

                Server.NextFrame(() =>
                {
                    if (victim == null || !victim.IsValid) return;
                    var pawn = victim.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) return;

                    pawn.Teleport(spawnpoint, null, new Vector(0, 0, 0));
                });
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
