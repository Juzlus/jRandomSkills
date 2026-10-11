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
    // Press the use key to leap at the nearest enemy you can see and slam into the ground next to them; crouching
    // in the air slams on the spot. Enemies around the landing spot are knocked back and hurt.
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
                        skillInfo.LandedTick = Server.TickCount;
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

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) return;

            float cooldown = SkillsInfo.GetValue<float>(skillName, "cooldown");
            if (skillInfo.Slamming || skillInfo.Cooldown.AddSeconds(cooldown) > DateTime.Now) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var target = NearestVisibleEnemy(player, pawn, out float distance);
            if (target == null)
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("groundslam_no_target")}");
                return;
            }

            // A leap that lands next to the enemy: forward speed from the distance, a fixed rise, capped.
            float maxLeap = SkillsInfo.GetValue<float>(skillName, "leapSpeed");
            float rise = SkillsInfo.GetValue<float>(skillName, "leapRise");
            Vector to = new(target.X - pawn.AbsOrigin.X, target.Y - pawn.AbsOrigin.Y, 0);
            float flat = to.Length();
            if (flat < 1f) to = new Vector(1, 0, 0); else to = new Vector(to.X / flat, to.Y / flat, 0);
            float airTime = 2f * rise / 800f;                       // up and down under sv_gravity 800
            float speed = Math.Clamp(flat / MathF.Max(airTime, 0.1f), 300f, maxLeap);

            skillInfo.Slamming = true;
            skillInfo.Cooldown = DateTime.Now;
            pawn.Teleport(null, null, new Vector(to.X * speed, to.Y * speed, rise));
            pawn.EmitSound("Player.Respawn", volume: SkillsInfo.GetValue<float>(skillName, "soundVolume"));
        }

        // The closest living enemy with a clear line from the eyes to their chest.
        private static Vector? NearestVisibleEnemy(CCSPlayerController player, CCSPlayerPawn pawn, out float bestDistance)
        {
            bestDistance = float.MaxValue;
            Vector? best = null;
            Vector eye = new(pawn.AbsOrigin!.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            float maxRange = SkillsInfo.GetValue<float>(skillName, "leapRange");

            foreach (var enemy in PlayerManager.GetTickPlayers())
            {
                if (!Instance.IsPlayerValid(enemy) || enemy.Index == player.Index || enemy.Team == player.Team || !enemy.PawnIsAlive) continue;
                var enemyPawn = enemy.PlayerPawn.Value;
                if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.AbsOrigin == null) continue;

                float d = (float)SkillUtils.GetDistance(pawn.AbsOrigin, enemyPawn.AbsOrigin);
                if (d > maxRange || d >= bestDistance) continue;

                Vector chest = new(enemyPawn.AbsOrigin.X, enemyPawn.AbsOrigin.Y, enemyPawn.AbsOrigin.Z + 40f);
                var trace = RayTrace.TraceShape(player, eye, chest);
                if (trace.HasValue && trace.Value.DidHit && trace.Value.Fraction < 0.97f) continue; // a wall in between

                bestDistance = d;
                best = new Vector(enemyPawn.AbsOrigin.X, enemyPawn.AbsOrigin.Y, enemyPawn.AbsOrigin.Z);
            }
            return best;
        }

        // The slam drives the player into the ground far faster than a normal fall; the game would count that as
        // lethal fall damage, so fall damage is swallowed while slamming and for a few ticks after landing.
        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (SkillPlayerInfo.IsEmpty || damagedEntity == null || damageInfo == null || damageInfo.Handle == nint.Zero) return;
            if ((damageInfo.BitsDamageType & DamageTypes_t.DMG_FALL) == 0) return;

            var pawn = new CCSPlayerPawn(damagedEntity.Handle);
            if (!pawn.IsValid || pawn.DesignerName != "player") return;
            var controller = pawn.Controller?.Value;
            if (controller == null || !controller.IsValid) return;

            var player = PlayerManager.GetPlayerEvent(controller.As<CCSPlayerController>());
            if (player == null || !SkillPlayerInfo.TryGetValue(player.Index, out var info)) return;

            if (info.Slamming || info.LandedTick + 16 >= Server.TickCount)
                damageInfo.Damage = 0;
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
            public int LandedTick { get; set; } = -100;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#b5651d", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon, float cooldown = 6f, float slamVelocity = 1500f, float radius = 220f, float pushVelocity = 500f, float jumpVelocity = 300f, int damage = 25, int minAirTicks = 8, float soundVolume = .6f, float leapSpeed = 900f, float leapRise = 350f, float leapRange = 1500f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float SlamVelocity { get; set; } = slamVelocity;
            public float Radius { get; set; } = radius;
            public float PushVelocity { get; set; } = pushVelocity;
            public float JumpVelocity { get; set; } = jumpVelocity;
            public int Damage { get; set; } = damage;
            public int MinAirTicks { get; set; } = minAirTicks;
            public float SoundVolume { get; set; } = soundVolume;
            public float LeapSpeed { get; set; } = leapSpeed;
            public float LeapRise { get; set; } = leapRise;
            public float LeapRange { get; set; } = leapRange;
        }
    }
}
