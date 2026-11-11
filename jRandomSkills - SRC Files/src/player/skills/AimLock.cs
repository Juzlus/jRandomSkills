using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class AimLock : ISkill
    {
        private const Skills skillName = Skills.AimLock;
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
                SkillPlayerInfo.Clear();
        }

        public static void OnTick()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayer(player);
                if (playerInfo?.Skill == skillName)
                {
                    if (SkillPlayerInfo.TryGetValue(player.Key(), out var skillInfo))
                    {
                        if (SkillUtils.IsHudFrame()) UpdateHUD(player, skillInfo);

                        if (skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "duration")) > DateTime.Now)
                            LookAtEnemy(player);
                    }
                }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillPlayerInfo.TryAdd(player.Key(), new PlayerSkillInfo
            {
                PlayerIndex = player.Key(),
                CanUse = true,
                Cooldown = DateTime.MinValue,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillPlayerInfo.TryRemove(player.Key(), out _);
            SkillUtils.ResetPrintHTML(player);
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            if (player == null || !player.IsValid || skillInfo == null) return;

            float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
            float cooldown = Math.Max(time, 0);

            if (cooldown == 0 && !skillInfo.CanUse)
                skillInfo.CanUse = true;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;

            if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            if (SkillPlayerInfo.TryGetValue(player.Key(), out var skillInfo))
            {
                if (!skillInfo.CanUse) return;

                skillInfo.Cooldown = DateTime.Now;
                skillInfo.CanUse = false;
            }
        }

        private static void LookAtEnemy(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            var myOrigin = pawn.AbsOrigin;
            float myX = myOrigin.X, myY = myOrigin.Y, myZ = myOrigin.Z;
            float eyeZ = myZ + pawn.ViewOffset.Z;
            var myTeam = player.Team;

            CCSPlayerPawn? enemyPawn = null;
            float bestDist = float.MaxValue;
            foreach (var p in PlayerManager.GetTickPlayers())
            {
                if (p == null || !p.IsValid || p.Team == myTeam) continue;

                var candidate = p.PlayerPawn.Value;
                if (candidate == null || !candidate.IsValid || candidate.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;
                var origin = candidate.AbsOrigin;
                if (origin == null) continue;

                float dx = origin.X - myX, dy = origin.Y - myY, dz = origin.Z - myZ;
                float dist = dx * dx + dy * dy + dz * dz;
                if (dist >= bestDist) continue;

                bestDist = dist;
                enemyPawn = candidate;
            }

            if (enemyPawn == null || enemyPawn.AbsOrigin == null || enemyPawn.ViewOffset == null || enemyPawn.AbsRotation == null) return;

            var enemyOrigin = enemyPawn.AbsOrigin;
            double yawRad = enemyPawn.AbsRotation.Y * Math.PI / 180.0;
            double pitchRad = enemyPawn.AbsRotation.X * Math.PI / 180.0;
            float fwdX = (float)(Math.Cos(pitchRad) * Math.Cos(yawRad)) * 5;
            float fwdY = (float)(Math.Cos(pitchRad) * Math.Sin(yawRad)) * 5;

            float dirX = enemyOrigin.X + fwdX - myX;
            float dirY = enemyOrigin.Y + fwdY - myY;
            float dirZ = enemyOrigin.Z + enemyPawn.ViewOffset.Z + 2 - eyeZ;
            QAngle angle = VectorToAngle(dirX, dirY, dirZ);

            pawn.Look(angle);
        }

        private static QAngle VectorToAngle(float x, float y, float z)
        {
            float pitch = (float)(-Math.Atan2(z, Math.Sqrt(x * x + y * y)) * 180 / Math.PI);
            float yaw = (float)(Math.Atan2(y, x) * 180 / Math.PI);

            return new QAngle(pitch, yaw, 0);
        }

        public class PlayerSkillInfo
        {
            public uint PlayerIndex { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#fa7b48", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float cooldown = 20f, float duration = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float Duration { get; set; } = duration;
        }
    }
}