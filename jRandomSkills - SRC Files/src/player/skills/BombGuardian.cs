using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class BombGuardian : ISkill
    {
        private const Skills skillName = Skills.BombGuardian;
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.ResetPrintHTML(player);
        }

        private static bool IsGuarding(CCSPlayerPawn pawn, Vector? bombOrigin)
        {
            if (bombOrigin == null || pawn.AbsOrigin == null) return false;
            return SkillUtils.Distance(pawn.AbsOrigin, bombOrigin) <= SkillsInfo.GetValue<float>(skillName, "guardRadius");
        }

        private static Vector? GetBombOrigin()
        {
            return Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault(b => b != null && b.IsValid)?.AbsOrigin;
        }

        public static void OnTick()
        {
            if (!SkillUtils.IsHudFrame()) return;

            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            if (holderBuffer.Count == 0) return;

            var bombOrigin = GetBombOrigin();
            foreach (var player in holderBuffer)
            {
                if (!Instance.IsPlayerValid(player)) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo == null) continue;

                playerInfo.PrintHTML = IsGuarding(player.PlayerPawn.Value!, bombOrigin)
                    ? $"<font color='#00FF00'>{player.GetTranslation("bombguardian_hud_active")}</font>"
                    : null;
            }
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null) return;
            if (damagedEntity.DesignerName != "player") return;

            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);
            var victim = PlayerManager.GetPlayerEvent(victimPawn.Controller?.Value?.As<CCSPlayerController>());
            if (victim == null || PlayerManager.GetPlayerByIndex(victim.Index)?.Skill != skillName) return;

            if (IsGuarding(victimPawn, GetBombOrigin()))
                damageInfo.Damage *= SkillsInfo.GetValue<float>(skillName, "damageTakenMultiplier");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d4552a", CsTeam onlyTeam = CsTeam.Terrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float guardRadius = 400f, float damageTakenMultiplier = .7f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float GuardRadius { get; set; } = guardRadius;
            public float DamageTakenMultiplier { get; set; } = damageTakenMultiplier;
        }
    }
}
