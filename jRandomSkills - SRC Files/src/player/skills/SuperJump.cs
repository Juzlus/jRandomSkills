using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    // Press the use key on the ground for a very high jump; the landing never hurts.
    public class SuperJump : ISkill
    {
        private const Skills skillName = Skills.SuperJump;
        private static readonly ConcurrentDictionary<uint, DateTime> cooldowns = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            cooldowns.Clear();
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
        }

        public static void OnTick()
        {
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
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            if ((pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) == 0) return;

            cooldowns[player.Index] = DateTime.Now;
            float up = SkillsInfo.GetValue<float>(skillName, "jumpVelocity");
            pawn.Teleport(null, null, new Vector(pawn.AbsVelocity.X, pawn.AbsVelocity.Y, up));
            pawn.EmitSound("Default.WalkJump", volume: SkillsInfo.GetValue<float>(skillName, "soundVolume"));
        }

        // The holder never takes fall damage: a super jump lands hard.
        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (cooldowns.IsEmpty || damagedEntity == null || damageInfo == null || damageInfo.Handle == nint.Zero) return;
            if ((damageInfo.BitsDamageType & DamageTypes_t.DMG_FALL) == 0) return;

            var pawn = new CCSPlayerPawn(damagedEntity.Handle);
            if (!pawn.IsValid || pawn.DesignerName != "player") return;
            var controller = pawn.Controller?.Value;
            if (controller == null || !controller.IsValid) return;

            var player = PlayerManager.GetPlayerEvent(controller.As<CCSPlayerController>());
            if (player != null && cooldowns.ContainsKey(player.Index))
                damageInfo.Damage = 0;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7CFC00", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon, float jumpVelocity = 700f, float cooldown = 6f, float soundVolume = .6f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float JumpVelocity { get; set; } = jumpVelocity;
            public float Cooldown { get; set; } = cooldown;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
