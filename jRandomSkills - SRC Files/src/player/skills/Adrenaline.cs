using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Adrenaline : ISkill
    {
        private const Skills skillName = Skills.Adrenaline;
        private static readonly ConcurrentDictionary<uint, DateTime> boostedUntil = [];
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            boostedUntil.Clear();
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(attacker) || victim == null || !victim.IsValid || attacker == victim) return;
            if (attacker!.Team == victim.Team) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(attacker.Index);
            if (playerInfo?.Skill != skillName) return;

            var pawn = attacker.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            SkillUtils.AddHealth(pawn, SkillsInfo.GetValue<int>(skillName, "healOnKill"));
            boostedUntil[attacker.Index] = DateTime.Now.AddSeconds(SkillsInfo.GetValue<float>(skillName, "duration"));
            SkillUtils.ApplyScreenColor(attacker, 255, 60, 60, 60, 200, 100);
        }

        public static void OnTick()
        {
            if (boostedUntil.IsEmpty) return;

            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            float speed = SkillsInfo.GetValue<float>(skillName, "speedMultiplier");

            foreach (var player in holderBuffer)
            {
                if (!Instance.IsPlayerValid(player)) continue;
                if (!boostedUntil.TryGetValue(player.Index, out var until)) continue;

                var pawn = player.PlayerPawn.Value!;
                if (DateTime.Now > until)
                {
                    boostedUntil.TryRemove(player.Index, out _);
                    pawn.VelocityModifier = 1;
                    continue;
                }

                if (pawn.VelocityModifier == 0) continue;
                var buttons = player.Buttons;
                if (buttons.HasFlag(PlayerButtons.Moveleft) || buttons.HasFlag(PlayerButtons.Moveright) || buttons.HasFlag(PlayerButtons.Forward) || buttons.HasFlag(PlayerButtons.Back))
                    pawn.VelocityModifier = speed;
            }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (!boostedUntil.TryRemove(player.Index, out _)) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn != null && pawn.IsValid)
                pawn.VelocityModifier = 1;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff4d4d", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int healOnKill = 25, float duration = 5f, float speedMultiplier = 1.5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int HealOnKill { get; set; } = healOnKill;
            public float Duration { get; set; } = duration;
            public float SpeedMultiplier { get; set; } = speedMultiplier;
        }
    }
}
