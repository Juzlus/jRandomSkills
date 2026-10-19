using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class Titan : ISkill
    {
        private const Skills skillName = Skills.Titan;
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        private static float Scale => 1f + Math.Max(0f, SkillsInfo.GetValue<float>(skillName, "sizeBonus"));

        public static void EnableSkill(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            SkillUtils.ChangePlayerScale(player, Scale);

            int health = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "health"));
            pawn.MaxHealth = health;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            pawn.Health = health;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return;

            SkillUtils.ChangePlayerScale(player, 1f);

            pawn.MaxHealth = 100;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            pawn.Health = Math.Min(pawn.Health, 100);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;

            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            if (holderBuffer.Count == 0) return;

            float scale = Scale;
            foreach (var player in holderBuffer)
            {
                var pawn = player.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                var skeleton = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance();
                if (skeleton != null && Math.Abs(skeleton.Scale - scale) >= 0.01f)
                    SkillUtils.ChangePlayerScale(player, scale);
            }
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || !damagedEntity.IsValid || damageInfo == null) return;
            if (damagedEntity.DesignerName != "player") return;

            var attackerEntity = damageInfo.Attacker?.Value;
            if (attackerEntity == null || !attackerEntity.IsValid || attackerEntity.Handle == damagedEntity.Handle) return;
            if (attackerEntity.DesignerName != "player") return;

            var attacker = PlayerManager.GetPlayerEvent(PlayerManager.GetControllerByPawn(attackerEntity.Handle));
            if (attacker == null || PlayerManager.GetPlayer(attacker)?.Skill != skillName) return;

            damageInfo.Damage *= 1f + SkillsInfo.GetValue<float>(skillName, "extraDamagePercent");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8a6d3b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Epic, float sizeBonus = .3f, float extraDamagePercent = .3f, int health = 300) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float SizeBonus { get; set; } = sizeBonus;
            public float ExtraDamagePercent { get; set; } = extraDamagePercent;
            public int Health { get; set; } = health;
        }
    }
}
