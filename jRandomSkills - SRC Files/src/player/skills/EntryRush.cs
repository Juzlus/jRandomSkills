using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class EntryRush : ISkill
    {
        private const Skills skillName = Skills.EntryRush;
        // null until freeze time ends, then the moment the rush runs out.
        private static readonly ConcurrentDictionary<uint, DateTime?> rushUntil = [];
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            rushUntil.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            rushUntil[player.Index] = null;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (rushUntil.TryRemove(player.Index, out _))
                ResetSpeed(player);
            SkillUtils.ResetPrintHTML(player);
        }

        private static bool IsRushing(uint index) => rushUntil.TryGetValue(index, out var until) && until != null && DateTime.Now < until;

        private static void ResetSpeed(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn != null && pawn.IsValid && pawn.VelocityModifier != 0)
                pawn.VelocityModifier = 1;
        }

        public static void OnTick()
        {
            if (rushUntil.IsEmpty || SkillUtils.IsFreezeTime()) return;

            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            float speed = SkillsInfo.GetValue<float>(skillName, "speedMultiplier");
            bool hudFrame = SkillUtils.IsHudFrame();

            foreach (var player in holderBuffer)
            {
                if (!Instance.IsPlayerValid(player)) continue;
                if (!rushUntil.TryGetValue(player.Index, out var until)) continue;

                if (until == null)
                {
                    until = DateTime.Now.AddSeconds(SkillsInfo.GetValue<float>(skillName, "duration"));
                    rushUntil[player.Index] = until;
                }

                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                double left = (until.Value - DateTime.Now).TotalSeconds;

                if (left <= 0)
                {
                    rushUntil.TryRemove(player.Index, out _);
                    ResetSpeed(player);
                    if (playerInfo != null) playerInfo.PrintHTML = null;
                    continue;
                }

                if (hudFrame && playerInfo != null)
                    playerInfo.PrintHTML = player.GetTranslation("entryrush_hud", $"<font color='#00FF00'>{Math.Ceiling(left)}</font>");

                var pawn = player.PlayerPawn.Value!;
                if (pawn.VelocityModifier == 0) continue;
                var buttons = player.Buttons;
                if (buttons.HasFlag(PlayerButtons.Moveleft) || buttons.HasFlag(PlayerButtons.Moveright) || buttons.HasFlag(PlayerButtons.Forward) || buttons.HasFlag(PlayerButtons.Back))
                    pawn.VelocityModifier = speed;
            }
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null || rushUntil.IsEmpty) return;
            if (damagedEntity.DesignerName != "player") return;

            var victim = PlayerManager.GetPlayerEvent(new CCSPlayerPawn(damagedEntity.Handle).Controller?.Value?.As<CCSPlayerController>());
            if (victim == null || !IsRushing(victim.Index)) return;

            damageInfo.Damage *= SkillsInfo.GetValue<float>(skillName, "damageTakenMultiplier");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#4fd17a", CsTeam onlyTeam = CsTeam.CounterTerrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float duration = 8f, float speedMultiplier = 1.35f, float damageTakenMultiplier = .75f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Duration { get; set; } = duration;
            public float SpeedMultiplier { get; set; } = speedMultiplier;
            public float DamageTakenMultiplier { get; set; } = damageTakenMultiplier;
        }
    }
}
