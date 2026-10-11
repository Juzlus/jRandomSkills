using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    // Press the use key to shake the ground: every enemy within reach is slowed to a crawl for a few seconds.
    public class Tremor : ISkill
    {
        private const Skills skillName = Skills.Tremor;
        private static readonly ConcurrentDictionary<uint, DateTime> cooldowns = [];
        private static readonly ConcurrentDictionary<uint, (float Original, DateTime Until)> slowed = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            cooldowns.Clear();
            RestoreAll();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            cooldowns[player.Index] = DateTime.MinValue;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            cooldowns.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
            if (cooldowns.IsEmpty) RestoreAll();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            cooldowns.TryRemove(playerIndex, out _);
            slowed.TryRemove(playerIndex, out _);
        }

        public static void OnTick()
        {
            // Slowed enemies get their speed back when the time is up.
            foreach (var (index, entry) in slowed)
            {
                if (DateTime.Now < entry.Until) continue;
                slowed.TryRemove(index, out _);
                Restore(index, entry.Original);
            }

            if (cooldowns.IsEmpty || !SkillUtils.IsHudFrame()) return;
            float cooldown = SkillsInfo.GetValue<float>(skillName, "cooldown");
            foreach (var (index, last) in cooldowns)
            {
                var player = Utilities.GetPlayerFromIndex((int)index);
                var info = player != null && player.IsValid ? PlayerManager.GetPlayerByIndex(index) : null;
                if (info == null) continue;
                int left = (int)Math.Ceiling((last.AddSeconds(cooldown) - DateTime.Now).TotalSeconds);
                info.PrintHTML = left > 0 ? $"{player!.GetTranslation("hud_info", $"<font color='#FF0000'>{left}</font>")}" : null;
            }
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (!cooldowns.TryGetValue(player.Index, out var last)) return;
            if (last.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) > DateTime.Now) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            float radius = SkillsInfo.GetValue<float>(skillName, "radius");
            float factor = Math.Clamp(SkillsInfo.GetValue<float>(skillName, "slowFactor"), 0.05f, 1f);
            float seconds = SkillsInfo.GetValue<float>(skillName, "slowSeconds");
            int hit = 0;

            foreach (var enemy in PlayerManager.GetTickPlayers())
            {
                if (!Instance.IsPlayerValid(enemy) || enemy.Index == player.Index || enemy.Team == player.Team || !enemy.PawnIsAlive) continue;
                var enemyPawn = enemy.PlayerPawn.Value;
                if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.AbsOrigin == null) continue;
                if (SkillUtils.GetDistance(pawn.AbsOrigin, enemyPawn.AbsOrigin) > radius) continue;

                float original = slowed.TryGetValue(enemy.Index, out var prev) ? prev.Original : enemyPawn.VelocityModifier;
                slowed[enemy.Index] = (original, DateTime.Now.AddSeconds(seconds));
                enemyPawn.VelocityModifier = factor;
                Utilities.SetStateChanged(enemyPawn, "CCSPlayerPawn", "m_flVelocityModifier");
                enemy.PrintToChat($" {ChatColors.Red}{enemy.GetTranslation("tremor_enemy_info", (int)seconds)}");
                hit++;
            }

            cooldowns[player.Index] = DateTime.Now;
            pawn.EmitSound("Weapon_HEGrenade.Explode", volume: SkillsInfo.GetValue<float>(skillName, "soundVolume"));
            player.PrintToChat($" {ChatColors.Green}{player.GetTranslation("tremor_player_info", hit)}");
        }

        private static void RestoreAll()
        {
            foreach (var (index, entry) in slowed) Restore(index, entry.Original);
            slowed.Clear();
        }

        private static void Restore(uint index, float original)
        {
            var player = Utilities.GetPlayerFromIndex((int)index);
            var pawn = player?.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;
            pawn.VelocityModifier = original <= 0f ? 1f : original;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#A0522D", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, float radius = 600f, float slowFactor = 0.35f, float slowSeconds = 3f, float cooldown = 20f, float soundVolume = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Radius { get; set; } = radius;
            public float SlowFactor { get; set; } = slowFactor;
            public float SlowSeconds { get; set; } = slowSeconds;
            public float Cooldown { get; set; } = cooldown;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
