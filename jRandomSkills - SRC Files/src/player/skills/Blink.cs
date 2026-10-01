using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    // Press the use key to teleport a few metres in the direction you look. Charges refill on a cooldown.
    public class Blink : ISkill
    {
        private const Skills skillName = Skills.Blink;
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            SkillPlayerInfo.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillPlayerInfo[player.Index] = new PlayerSkillInfo
            {
                Charges = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "charges")),
                LastUse = DateTime.MinValue,
            };
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void OnTick()
        {
            if (SkillPlayerInfo.IsEmpty || !SkillUtils.IsHudFrame()) return;

            int maxCharges = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "charges"));
            float cooldown = SkillsInfo.GetValue<float>(skillName, "cooldown");

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;
                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo?.HasSkill(skillName) != true) continue;
                if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) continue;

                // One charge comes back every `cooldown` seconds.
                if (skillInfo.Charges < maxCharges && skillInfo.LastUse.AddSeconds(cooldown) <= DateTime.Now)
                {
                    skillInfo.Charges++;
                    skillInfo.LastUse = DateTime.Now;
                }

                if (skillInfo.Charges >= maxCharges)
                    playerInfo.PrintHTML = null;
                else
                {
                    int left = (int)Math.Ceiling((skillInfo.LastUse.AddSeconds(cooldown) - DateTime.Now).TotalSeconds);
                    playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{Math.Max(left, 0)}</font>")} ({skillInfo.Charges}/{maxCharges})";
                }
            }
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo?.HasSkill(skillName) != true) return;
            if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo) || skillInfo.Charges <= 0) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            float distance = SkillsInfo.GetValue<float>(skillName, "distance");
            Vector direction = SkillUtils.GetForwardVector(pawn.EyeAngles);
            Vector start = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            Vector end = start + direction * distance;

            // Stop short of whatever is in the way so the player never ends up inside a wall.
            var trace = RayTrace.TraceShape(player, start, end);
            float travelled = distance;
            if (trace.HasValue && trace.Value.Fraction < 1f)
                travelled = Math.Max(0f, distance * trace.Value.Fraction - 40f);

            if (travelled < 16f) return;

            Vector target = new(pawn.AbsOrigin.X + direction.X * travelled, pawn.AbsOrigin.Y + direction.Y * travelled, pawn.AbsOrigin.Z + direction.Z * travelled);
            if (target.Z < pawn.AbsOrigin.Z) target.Z = pawn.AbsOrigin.Z;

            if (skillInfo.Charges == Math.Max(1, SkillsInfo.GetValue<int>(skillName, "charges")))
                skillInfo.LastUse = DateTime.Now;
            skillInfo.Charges--;

            pawn.Teleport(target, null, new Vector(pawn.AbsVelocity.X, pawn.AbsVelocity.Y, 0));
            SkillUtils.EmitSoundToPlayer(player, "Player.Respawn", SkillsInfo.GetValue<float>(skillName, "soundVolume"));
        }

        public class PlayerSkillInfo
        {
            public int Charges { get; set; }
            public DateTime LastUse { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7df9ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon, float distance = 350f, int charges = 2, float cooldown = 8f, float soundVolume = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Distance { get; set; } = distance;
            public int Charges { get; set; } = charges;
            public float Cooldown { get; set; } = cooldown;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
