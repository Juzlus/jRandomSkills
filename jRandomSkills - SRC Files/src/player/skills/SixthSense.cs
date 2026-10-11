using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;

namespace src.player.skills
{
    // "I see dead people": the hit that should kill you leaves you as a half-transparent ghost with 175 HP for
    // seven seconds. When the time is up you fall for good, credited to the one who got you.
    public class SixthSense : ISkill
    {
        private const Skills skillName = Skills.SixthSense;
        private sealed class GhostInfo { public uint Killer; public DateTime Until; public bool Used; }
        private static readonly ConcurrentDictionary<uint, GhostInfo> ghosts = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound() => ghosts.Clear();

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            ghosts[player.Index] = new GhostInfo();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (ghosts.TryRemove(player.Index, out var g) && g.Used) SetAlpha(player, 255);
        }

        public static void PlayerDisconnect(uint playerIndex) => ghosts.TryRemove(playerIndex, out _);

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (ghosts.IsEmpty || damagedEntity == null || damageInfo == null || damageInfo.Handle == nint.Zero) return;
            if (damagedEntity.DesignerName != "player") return;

            var victimPawn = new CCSPlayerPawn(damagedEntity.Handle);
            if (!victimPawn.IsValid) return;
            var victim = PlayerManager.GetPlayerEvent(victimPawn.Controller?.Value?.As<CCSPlayerController>());
            if (victim == null || !victim.IsValid || !ghosts.TryGetValue(victim.Index, out var ghost)) return;
            if (ghost.Used) return;
            if (SkillUtils.IsFriendlyFireBlocked(damageInfo, victimPawn)) return;
            if (!SkillUtils.IsPredictedLethal(damageInfo, victimPawn)) return;

            // The blow is swallowed; the ghost rises where the player stands.
            damageInfo.Damage = 0;
            ghost.Used = true;
            ghost.Until = DateTime.Now.AddSeconds(SkillsInfo.GetValue<float>(skillName, "ghostSeconds"));

            var attackerEnt = damageInfo.Attacker?.Value;
            if (attackerEnt != null && attackerEnt.IsValid && attackerEnt.DesignerName == "player")
            {
                var killer = PlayerManager.GetPlayerEvent(new CCSPlayerPawn(attackerEnt.Handle).Controller?.Value?.As<CCSPlayerController>());
                if (killer != null && killer.IsValid) ghost.Killer = killer.Index;
            }

            victimPawn.Health = SkillsInfo.GetValue<int>(skillName, "ghostHealth");
            Utilities.SetStateChanged(victimPawn, "CBaseEntity", "m_iHealth");
            SetAlpha(victim, SkillsInfo.GetValue<int>(skillName, "alpha"));
            SkillUtils.EmitSoundToPlayer(victim, "Player.Respawn", SkillsInfo.GetValue<float>(skillName, "soundVolume"));
            victim.PrintToChat($" {ChatColors.Lime}{victim.GetTranslation("sixthsense_info", (int)SkillsInfo.GetValue<float>(skillName, "ghostSeconds"))}");
        }

        public static void OnTick()
        {
            if (ghosts.IsEmpty) return;

            foreach (var (index, ghost) in ghosts)
            {
                if (!ghost.Used) continue;
                var player = Utilities.GetPlayerFromIndex((int)index);
                var pawn = player?.PlayerPawn.Value;
                if (player == null || !player.IsValid || pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                if (SkillUtils.IsHudFrame())
                {
                    var info = PlayerManager.GetPlayerByIndex(index);
                    int left = (int)Math.Ceiling((ghost.Until - DateTime.Now).TotalSeconds);
                    if (info != null) info.PrintHTML = $"<font color='#B0E0E6'>{player.GetTranslation("sixthsense_hud", Math.Max(left, 0))}</font>";
                }

                if (DateTime.Now < ghost.Until) continue;

                // Time is up: the ghost fades for good, the kill goes to whoever got them.
                SetAlpha(player, 255);
                var killer = ghost.Killer != 0 ? Utilities.GetPlayerFromIndex((int)ghost.Killer) : null;
                SkillUtils.TakeHealth(pawn, pawn.Health + 1, killer != null && killer.IsValid ? killer : null, null);
                ghosts.TryRemove(index, out _);
            }
        }

        private static void SetAlpha(CCSPlayerController player, int alpha)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;
            pawn.Render = Color.FromArgb(Math.Clamp(alpha, 0, 255), 255, 255, 255);
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#B0E0E6", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, float ghostSeconds = 7f, int ghostHealth = 175, int alpha = 110, float soundVolume = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float GhostSeconds { get; set; } = ghostSeconds;
            public int GhostHealth { get; set; } = ghostHealth;
            public int Alpha { get; set; } = alpha;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
