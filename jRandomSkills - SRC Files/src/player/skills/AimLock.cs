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
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill == skillName)
                {
                    if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
                    {
                        UpdateHUD(player, skillInfo);

                        if (skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "duration")) > DateTime.Now)
                            LookAtEnemy(player);
                    }
                }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillPlayerInfo.TryAdd(player.Index, new PlayerSkillInfo
            {
                PlayerIndex = player.Index,
                CanUse = true,
                Cooldown = DateTime.MinValue,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            if (player == null || !player.IsValid || skillInfo == null) return;

            float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
            float cooldown = Math.Max(time, 0);

            if (cooldown == 0 && !skillInfo.CanUse)
                skillInfo.CanUse = true;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
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

            var myEyePos = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);

            var enemy = Utilities.GetPlayers()
                .Where(p => p.IsValid && p.PawnIsAlive && p.Team != player.Team && p.PlayerPawn.Value != null && p.PlayerPawn.Value.AbsOrigin != null)
                .OrderBy(p => (p.PlayerPawn.Value!.AbsOrigin! - pawn.AbsOrigin).LengthSqr())
                .FirstOrDefault();

            if (enemy == null) return;

            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.AbsOrigin == null || enemyPawn.ViewOffset == null) return;

            Vector diff = SkillUtils.GetForwardVector(enemyPawn.AbsRotation!) * 5;
            Vector enemyHead = new(
                enemyPawn.AbsOrigin.X + diff.X,
                enemyPawn.AbsOrigin.Y + diff.Y,
                enemyPawn.AbsOrigin.Z + enemyPawn.ViewOffset.Z + 2
            );

            Vector direction = enemyHead - myEyePos;
            QAngle angle = VectorToAngle(direction);

            pawn.Look(angle);
        }

        private static QAngle VectorToAngle(Vector direction)
        {
            float pitch = (float)(-Math.Atan2(direction.Z, Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y)) * 180 / Math.PI);
            float yaw = (float)(Math.Atan2(direction.Y, direction.X) * 180 / Math.PI);

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