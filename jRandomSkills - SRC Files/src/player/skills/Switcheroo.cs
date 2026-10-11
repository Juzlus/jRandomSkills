using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    // Press the use key while aiming at an enemy to swap positions with them (Position Swap picks a random enemy instead).
    public class Switcheroo : ISkill
    {
        private const Skills skillName = Skills.Switcheroo;
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
            SkillPlayerInfo[player.Index] = new PlayerSkillInfo { Cooldown = DateTime.MinValue, LastMiss = DateTime.MinValue };
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

            float cooldown = SkillsInfo.GetValue<float>(skillName, "cooldown");
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;
                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo?.HasSkill(skillName) != true) continue;
                if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) continue;

                int left = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(cooldown) - DateTime.Now).TotalSeconds);
                if (left > 0)
                    playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{left}</font>")}";
                else if (skillInfo.LastMiss.AddSeconds(2) >= DateTime.Now)
                    playerInfo.PrintHTML = $"<font color='#FF0000'>{player.GetTranslation("hud_info_no_enemy")}</font>";
                else
                    playerInfo.PrintHTML = null;
            }
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo?.HasSkill(skillName) != true) return;
            if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) return;
            if (skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) > DateTime.Now) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var target = FindAimedEnemy(player, pawn);
            if (target == null)
            {
                skillInfo.LastMiss = DateTime.Now;
                return;
            }

            skillInfo.Cooldown = DateTime.Now;
            Swap(pawn, target.PlayerPawn.Value!);

            SkillUtils.EmitSoundToPlayer(player, "Player.Respawn", SkillsInfo.GetValue<float>(skillName, "soundVolume"));
            SkillUtils.EmitSoundToPlayer(target, "Player.Respawn", SkillsInfo.GetValue<float>(skillName, "soundVolume"));
            var targetEvent = PlayerManager.GetPlayerFromEvent(target);
            if (targetEvent != null && targetEvent.IsValid)
                targetEvent.PrintToChat($" {ChatColors.Red}{targetEvent.GetTranslation("switcheroo_enemy_info", player.PlayerName)}");
        }

        // The living enemy closest to the crosshair inside the aim cone, nearest first, within maxDistance.
        private static CCSPlayerController? FindAimedEnemy(CCSPlayerController player, CCSPlayerPawn pawn)
        {
            float maxDistance = SkillsInfo.GetValue<float>(skillName, "maxDistance");
            float minDot = MathF.Cos(SkillsInfo.GetValue<float>(skillName, "aimAngle") * MathF.PI / 180f);

            Vector eye = new(pawn.AbsOrigin!.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            Vector forward = SkillUtils.GetForwardVector(pawn.EyeAngles);

            CCSPlayerController? best = null;
            float bestDot = minDot;

            foreach (var enemy in PlayerManager.GetTickPlayers())
            {
                if (!Instance.IsPlayerValid(enemy) || enemy.Index == player.Index || enemy.Team == player.Team || !enemy.PawnIsAlive) continue;
                var enemyPawn = enemy.PlayerPawn.Value;
                if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.AbsOrigin == null) continue;

                Vector center = new(enemyPawn.AbsOrigin.X, enemyPawn.AbsOrigin.Y, enemyPawn.AbsOrigin.Z + 36f);
                Vector diff = center - eye;
                float length = diff.Length();
                if (length <= 0f || length > maxDistance) continue;

                float dot = (diff.X * forward.X + diff.Y * forward.Y + diff.Z * forward.Z) / length;
                if (dot < bestDot) continue;

                bestDot = dot;
                best = enemy;
            }

            return best;
        }

        private static void Swap(CCSPlayerPawn a, CCSPlayerPawn b)
        {
            if (a.AbsOrigin == null || b.AbsOrigin == null) return;

            Vector posA = new(a.AbsOrigin.X, a.AbsOrigin.Y, a.AbsOrigin.Z);
            Vector posB = new(b.AbsOrigin.X, b.AbsOrigin.Y, b.AbsOrigin.Z);
            QAngle anglesA = new(a.V_angle.X, a.V_angle.Y, 0);
            QAngle anglesB = new(b.V_angle.X, b.V_angle.Y, 0);
            Vector velA = new(a.AbsVelocity.X, a.AbsVelocity.Y, a.AbsVelocity.Z);
            Vector velB = new(b.AbsVelocity.X, b.AbsVelocity.Y, b.AbsVelocity.Z);

            a.Teleport(posB, null, velB);
            b.Teleport(posA, null, velA);
            a.Look(anglesB);
            b.Look(anglesA);
        }

        public class PlayerSkillInfo
        {
            public DateTime Cooldown { get; set; }
            public DateTime LastMiss { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#c77dff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, float cooldown = 20f, float maxDistance = 1500f, float aimAngle = 6f, float soundVolume = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float MaxDistance { get; set; } = maxDistance;
            public float AimAngle { get; set; } = aimAngle;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
