using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class TrueArmor : ISkill
    {
        private const Skills skillName = Skills.TrueArmor;

        private static readonly ConcurrentDictionary<uint, byte> holders = [];
        private static readonly ConcurrentDictionary<uint, int> pending = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            pending.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            holders[player.Index] = 0;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            holders.TryRemove(player.Index, out _);
            pending.TryRemove(player.Index, out _);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            holders.TryRemove(playerIndex, out _);
            pending.TryRemove(playerIndex, out _);
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (holders.IsEmpty) return;

            if (!TryResolveVictim(damagedEntity, damageInfo, out var victim, out var victimPawn, out var info)) return;
            if (!holders.ContainsKey(victim!.Index)) return;

            int armor = victimPawn!.ArmorValue;
            if (armor <= 0) return;

            float damage = info!.Damage;
            if (damage <= 0) return;

            float ratio = SkillsInfo.GetValue<float>(skillName, "armorShare");
            float absorbed = MathF.Min(damage * ratio, armor);
            if (absorbed <= 0) return;

            info.Damage = damage - absorbed;

            pending[victim.Index] = (int)MathF.Round(armor - absorbed);
            victimPawn.ArmorValue = 0;

            Utilities.SetStateChanged(victimPawn, "CCSPlayerPawn", "m_ArmorValue");

            Debug.WriteToDebug($"[TrueArmor] {victim.PlayerName}: raw={damage:0.#} absorbed={absorbed:0.#} " +
                $"passed={info.Damage:0.#} armor={armor}->{pending[victim.Index]} hp={victimPawn.Health}", DebugCategory.Damage);
        }

        public static void OnTakeDamagePost(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo, CTakeDamageResult damageResult)
        {
            if (pending.IsEmpty) return;

            if (!TryResolveVictim(damagedEntity, damageInfo, out var victim, out var victimPawn, out _)) return;
            if (!pending.TryRemove(victim!.Index, out int restored)) return;

            victimPawn!.ArmorValue = restored;
            Utilities.SetStateChanged(victimPawn, "CCSPlayerPawn", "m_ArmorValue");
        }

        private static bool TryResolveVictim(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo, out CCSPlayerController? victim, out CCSPlayerPawn? pawn, out CTakeDamageInfo? info)
        {
            victim = null;
            pawn = null;
            info = null;

            if (damagedEntity == null || !damagedEntity.IsValid || damageInfo == null) return false;

            var victimPawn = damagedEntity.As<CCSPlayerPawn>();
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.DesignerName != "player") return false;

            var controller = victimPawn.Controller.Value;
            if (controller == null || !controller.IsValid) return false;

            var resolved = controller.As<CCSPlayerController>();
            if (resolved == null || !resolved.IsValid) return false;

            var routed = PlayerManager.GetPlayerEvent(resolved);
            victim = routed != null && routed.IsValid ? routed : resolved;
            pawn = victimPawn;
            info = damageInfo;
            return true;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#9ad3f5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon, float armorShare = .33f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float ArmorShare { get; set; } = armorShare;
        }
    }
}
