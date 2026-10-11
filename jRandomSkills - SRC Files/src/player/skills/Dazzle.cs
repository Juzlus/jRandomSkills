using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    // Every bullet that hits an enemy blinds them for a moment, like a short flash.
    public class Dazzle : ISkill
    {
        private const Skills skillName = Skills.Dazzle;
        private static readonly ConcurrentDictionary<uint, byte> holders = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound() => holders.Clear();

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            holders[player.Index] = 0;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            holders.TryRemove(player.Index, out _);
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (holders.IsEmpty || damagedEntity == null || damageInfo == null || damageInfo.Handle == nint.Zero) return;
            if (damagedEntity.DesignerName != "player" || !SkillUtils.IsBulletDamage(damageInfo)) return;

            var attackerEnt = damageInfo.Attacker?.Value;
            if (attackerEnt == null || !attackerEnt.IsValid || attackerEnt.DesignerName != "player") return;
            var attackerPawn = new CCSPlayerPawn(attackerEnt.Handle);
            var attacker = PlayerManager.GetPlayerEvent(attackerPawn.Controller?.Value?.As<CCSPlayerController>());
            if (attacker == null || !holders.ContainsKey(attacker.Index)) return;

            var victimPawn = new CCSPlayerPawn(damagedEntity.Handle);
            if (!victimPawn.IsValid || victimPawn.TeamNum == attackerPawn.TeamNum || victimPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            float duration = SkillsInfo.GetValue<float>(skillName, "blindSeconds");
            float alpha = SkillsInfo.GetValue<float>(skillName, "blindAlpha");
            float now = Server.CurrentTime;

            if (victimPawn.BlindUntilTime <= now) victimPawn.BlindStartTime = now;
            victimPawn.BlindUntilTime = now + duration;
            victimPawn.FlashDuration = duration;
            victimPawn.FlashMaxAlpha = alpha;
            Utilities.SetStateChanged(victimPawn, "CCSPlayerPawnBase", "m_flFlashDuration");
            Utilities.SetStateChanged(victimPawn, "CCSPlayerPawnBase", "m_flFlashMaxAlpha");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#FFF59D", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, float blindSeconds = 0.9f, float blindAlpha = 200f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float BlindSeconds { get; set; } = blindSeconds;
            public float BlindAlpha { get; set; } = blindAlpha;
        }
    }
}
