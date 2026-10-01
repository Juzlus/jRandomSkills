using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    // Crouch while in the air to slam into the ground; enemies around the landing spot are knocked back and hurt.
    public class GroundSlam : ISkill
    {
        private const Skills skillName = Skills.GroundSlam;
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
            SkillPlayerInfo[player.Index] = new PlayerSkillInfo { Cooldown = DateTime.MinValue, Slamming = false, AirTicks = 0 };
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void OnTick()
        {
            if (SkillPlayerInfo.IsEmpty) return;

            float cooldown = SkillsInfo.GetValue<float>(skillName, "cooldown");

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;
                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo?.HasSkill(skillName) != true) continue;
                if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) continue;

                if (SkillUtils.IsHudFrame())
                {
                    int left = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(cooldown) - DateTime.Now).TotalSeconds);
                    playerInfo.PrintHTML = left > 0 ? $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{left}</font>")}" : null;
                }

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                bool onGround = pawn.GroundEntity != null && pawn.GroundEntity.IsValid;

                if (skillInfo.Slamming)
                {
                    if (onGround)
                    {
                        skillInfo.Slamming = false;
                        Impact(player, pawn);
                    }
                    continue;
                }

                if (onGround) { skillInfo.AirTicks = 0; continue; }

                skillInfo.AirTicks++;
                bool duck = (player.Buttons & PlayerButtons.Duck) != 0;
                bool ready = skillInfo.Cooldown.AddSeconds(cooldown) <= DateTime.Now;

                // A few ticks in the air so a normal crouch-jump off the ground does not trigger it.
                if (duck && ready && skillInfo.AirTicks >= SkillsInfo.GetValue<int>(skillName, "minAirTicks"))
                {
                    skillInfo.Slamming = true;
                    skillInfo.Cooldown = DateTime.Now;
                    pawn.Teleport(null, null, new Vector(0, 0, -SkillsInfo.GetValue<float>(skillName, "slamVelocity")));
                }
            }
        }

        private static void Impact(CCSPlayerController player, CCSPlayerPawn pawn)
        {
            if (pawn.AbsOrigin == null) return;

            float radius = SkillsInfo.GetValue<float>(skillName, "radius");
            float pushVelocity = SkillsInfo.GetValue<float>(skillName, "pushVelocity");
            float jumpVelocity = SkillsInfo.GetValue<float>(skillName, "jumpVelocity");
            int damage = SkillsInfo.GetValue<int>(skillName, "damage");

            Vector origin = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
            pawn.EmitSound("Weapon_HEGrenade.Explode", volume: SkillsInfo.GetValue<float>(skillName, "soundVolume"));

            foreach (var enemy in PlayerManager.GetTickPlayers())
            {
                if (!Instance.IsPlayerValid(enemy) || enemy.Index == player.Index || enemy.Team == player.Team || !enemy.PawnIsAlive) continue;
                var enemyPawn = enemy.PlayerPawn.Value;
                if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.AbsOrigin == null) continue;
                if (SkillUtils.GetDistance(origin, enemyPawn.AbsOrigin) > radius) continue;

                Vector away = new(enemyPawn.AbsOrigin.X - origin.X, enemyPawn.AbsOrigin.Y - origin.Y, 0);
                float length = away.Length();
                if (length < 1f) away = new Vector(1, 0, 0); else away = new Vector(away.X / length, away.Y / length, 0);

                if (!Heavyweight.Resists(enemy))
                    enemyPawn.Teleport(null, null, new Vector(away.X * pushVelocity, away.Y * pushVelocity, jumpVelocity));

                if (damage > 0)
                    SkillUtils.TakeHealth(enemyPawn, damage, player, KillfeedIcons.Knife);
            }
        }

        public class PlayerSkillInfo
        {
            public DateTime Cooldown { get; set; }
            public bool Slamming { get; set; }
            public int AirTicks { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#b5651d", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon, float cooldown = 6f, float slamVelocity = 1500f, float radius = 220f, float pushVelocity = 500f, float jumpVelocity = 300f, int damage = 25, int minAirTicks = 8, float soundVolume = .6f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float SlamVelocity { get; set; } = slamVelocity;
            public float Radius { get; set; } = radius;
            public float PushVelocity { get; set; } = pushVelocity;
            public float JumpVelocity { get; set; } = jumpVelocity;
            public int Damage { get; set; } = damage;
            public int MinAirTicks { get; set; } = minAirTicks;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
