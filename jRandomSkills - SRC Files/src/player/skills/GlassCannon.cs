using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class GlassCannon : ISkill
    {
        private const Skills skillName = Skills.GlassCannon;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        private static int MaxHealth() => Math.Max(1, SkillsInfo.GetValue<int>(skillName, "startHealth"));

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo != null)
            {
                int percent = (int)MathF.Round(SkillsInfo.GetValue<float>(skillName, "extraDamagePercent") * 100f);
                playerInfo.PrintHTML = player.GetTranslation("glasscannon_hud", $"<font color='#ff5e5e'>{MaxHealth()}</font>", $"<font color='#9fe8ff'>{percent}</font>");
            }

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            ClampHealth(pawn, MaxHealth());
        }

        private static void ClampHealth(CCSPlayerPawn pawn, int health)
        {
            if (pawn.MaxHealth != health)
            {
                pawn.MaxHealth = health;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");
            }

            if (pawn.Health > health)
            {
                pawn.Health = health;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 8 != 0) return;

            int health = MaxHealth();
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid || PlayerManager.GetPlayer(player)?.Skill != skillName) continue;

                var pawn = player.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                ClampHealth(pawn, health);
            }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo != null)
                playerInfo.PrintHTML = null;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return;

            pawn.MaxHealth = 100;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            if (SkillUtils.IsFreezeTime() && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE && pawn.Health > 0)
            {
                pawn.Health = 100;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#9fe8ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon, int startHealth = 70, float extraDamagePercent = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int StartHealth { get; set; } = startHealth;
            public float ExtraDamagePercent { get; set; } = extraDamagePercent;
        }
    }
}
