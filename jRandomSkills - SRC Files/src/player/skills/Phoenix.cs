using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Phoenix : ISkill
    {
        private const Skills skillName = Skills.Phoenix;
        private static readonly ConcurrentDictionary<uint, int> phoenixTicks = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void NewRound()
        {
            phoenixTicks.Clear();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            phoenixTicks.TryRemove(player.Index, out _);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            phoenixTicks.TryRemove(playerIndex, out _);
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || !damagedEntity.IsValid || damageInfo == null) return;

            var victimPawn = damagedEntity.As<CCSPlayerPawn>();
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.DesignerName != "player") return;

            var victimController = victimPawn.Controller.Value;
            if (victimController == null || !victimController.IsValid) return;

            var victim = victimController.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid || !victim.PawnIsAlive) return;

            var victimInfo = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(victim)?.Index ?? victim.Index);
            if (victimInfo == null || victimInfo.Skill != skillName) return;

            if (SkillUtils.IsFriendlyFireBlocked(damageInfo, victimPawn)) return;

            if (!SkillUtils.IsPredictedLethal(damageInfo, victimPawn)) return;

            if (TryConsumeRevive(victim, victimPawn))
                damageInfo.Damage = 0;
        }

        public static bool TryConsumeRevive(CCSPlayerController? victim, CCSPlayerPawn? victimPawn)
        {
            if (victim == null || !victim.IsValid || !victim.PawnIsAlive) return false;
            if (victimPawn == null || !victimPawn.IsValid) return false;

            var victimInfo = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(victim)?.Index ?? victim.Index);
            if (victimInfo == null || victimInfo.Skill != skillName) return false;

            if (phoenixTicks.TryGetValue(victim.Index, out int savedTick) && savedTick + 4 > Server.TickCount)
                return true;

            if (victim.TeamChanged) return false;
            if (victimInfo.SkillChance is float chance && Instance.Random.NextDouble() > chance) return false; // roll failed -> die

            phoenixTicks[victim.Index] = Server.TickCount;

            victimPawn.Health = 100;
            Utilities.SetStateChanged(victimPawn, "CBaseEntity", "m_iHealth");

            SkillUtils.PrintToChat(victim, victim.GetTranslation("phoenix_respawn"));

            Server.NextFrame(() =>
            {
                if (victim == null || !victim.IsValid) return;
                var pawn = victim.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) return;

                var spawnpoint = SkillUtils.GetSpawnPointVector(victim);
                if (spawnpoint == null) return;

                pawn.Teleport(spawnpoint);
            });

            return true;
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            float newChance = (float)Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "ChanceTo") - SkillsInfo.GetValue<float>(skillName, "ChanceFrom")) + SkillsInfo.GetValue<float>(skillName, "ChanceFrom");
            playerInfo.SkillChance = newChance;

            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, newChance)}",
                border: !PlayerManager.GetTickPlayers().Any(p => p.Team == player.Team && p != player) ? "tb" : "t");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff5C0A", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float chanceFrom = .2f, float chanceTo = .4f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
        }
    }
}
