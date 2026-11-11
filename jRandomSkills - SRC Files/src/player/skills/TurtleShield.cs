using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class TurtleShield : ISkill
    {
        private const Skills skillName = Skills.TurtleShield;
        private static readonly List<CCSPlayerController> holderBuffer = [];
        private static readonly ConcurrentDictionary<uint, bool> lastState = [];

        public static void LoadSkill()
        {
            Localization.RegisterDescriptionArgs(skillName, () => [(object)(int)MathF.Round(SkillsInfo.GetValue<float>(skillName, "reduction") * 100f)]);
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            lastState[player.Key()] = false;
            UpdateHud(player, false);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            lastState.TryRemove(player.Key(), out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lastState.TryRemove(playerIndex, out _);
        }

        public static void NewRound()
        {
            lastState.Clear();
        }

        private static bool IsDucking(CCSPlayerPawn pawn)
        {
            return ((PlayerFlags)pawn.Flags & PlayerFlags.FL_DUCKING) != 0;
        }

        private static void UpdateHud(CCSPlayerController player, bool active)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;

            string state = player.GetTranslation(active ? "turtleshield_active" : "turtleshield_passive");
            playerInfo.PrintHTML = player.GetTranslation("turtleshield_hud", $"<font color='{(active ? "#3ad15a" : "#ff4d4d")}'>{state}</font>");
        }

        public static void OnTick()
        {
            if (Server.TickCount % 4 != 0) return;

            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            foreach (var player in holderBuffer)
            {
                if (player == null || !player.IsValid) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;

                bool active = pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE && IsDucking(pawn);
                uint key = player.Key();
                if (lastState.TryGetValue(key, out bool previous) && previous == active) continue;

                lastState[key] = active;
                UpdateHud(player, active);
            }
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || !damagedEntity.IsValid || damageInfo == null) return;

            var victimPawn = damagedEntity.As<CCSPlayerPawn>();
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.DesignerName != "player") return;

            var victimController = victimPawn.Controller.Value;
            if (victimController == null || !victimController.IsValid) return;

            var victim = victimController.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(PlayerManager.GetPlayerEvent(victim) ?? victim);
            if (playerInfo?.Skill != skillName) return;

            if (victimPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || !IsDucking(victimPawn)) return;
            if ((damageInfo.BitsDamageType & DamageTypes_t.DMG_FALL) != 0) return;
            if (SkillUtils.IsFriendlyFireBlocked(damageInfo, victimPawn)) return;

            float reduction = Math.Clamp(SkillsInfo.GetValue<float>(skillName, "reduction"), 0f, 1f);
            damageInfo.Damage *= 1f - reduction;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#4caf50", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float reduction = .4f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Reduction { get; set; } = reduction;
        }
    }
}
