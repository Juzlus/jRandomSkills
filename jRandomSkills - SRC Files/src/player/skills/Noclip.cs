using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using CS2TraceRay.Class;
using CS2TraceRay.Struct;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills
{
    public class Noclip : ISkill
    {
        private const Skills skillName = Skills.Noclip;
        private static readonly ConcurrentDictionary<ulong, PlayerSkillInfo> SkillPlayerInfo = [];
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
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
                        UpdateHUD(player, skillInfo);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.SteamID, new PlayerSkillInfo
            {
                SteamID = player.SteamID,
                CanUse = true,
                IsFlying = false,
                Cooldown = DateTime.MinValue,
                LastPosition = null,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryRemove(player.SteamID, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            float cooldown = 0;
            float flying = 0;
            if (skillInfo != null)
            {
                float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
                cooldown = Math.Max(time, 0);

                float flyingTime = (int)(skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "duration")) - DateTime.Now).TotalMilliseconds;
                flying = Math.Max(flyingTime, 0);

                if (cooldown == 0 && skillInfo?.CanUse == false)
                    skillInfo.CanUse = true;
            }

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            if (cooldown == 0)
            {
                playerInfo.PrintHTML = null;
                return;
            }

            playerInfo.PrintHTML =
                skillInfo?.IsFlying == true
                    ? $"{player.GetTranslation("active_hud_info", $"<font color='#00FF00'>{Math.Round(flying / 100, 2)}</font>")}"
                    : $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
            {
                if (skillInfo.IsFlying)
                {
                    StopFlying(player, skillInfo);
                    return;
                }

                if (skillInfo.CanUse)
                {
                    var duration = SkillsInfo.GetValue<float>(skillName, "duration");

                    skillInfo.CanUse = false;
                    skillInfo.IsFlying = true;
                    skillInfo.Cooldown = DateTime.Now;
                    skillInfo.LastPosition = playerPawn.AbsOrigin == null ? null : new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z);
                    SetNoclip(player, true);
                    skillInfo.Timer?.Kill();

                    skillInfo.Timer = Instance.AddTimer(duration, () =>
                    {
                        StopFlying(player, skillInfo);
                    });
                }
            }
        }

        private static void SetNoclip(CCSPlayerController player, bool noclip = true)
        {
            if (player == null || !player.IsValid) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || !player.PawnIsAlive) return;

            playerPawn.MoveType = noclip ? MoveType_t.MOVETYPE_NOCLIP : MoveType_t.MOVETYPE_WALK;
            Schema.SetSchemaValue(playerPawn.Handle, "CBaseEntity", "m_nActualMoveType", (int)playerPawn.MoveType);
            Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_MoveType");
        }

        private static void StopFlying(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            skillInfo.Timer?.Kill();
            skillInfo.Timer = null;

            if (!skillInfo.IsFlying) return;
            skillInfo.IsFlying = false;

            if (player == null || !player.IsValid || !player.PawnIsAlive) return;
            SetNoclip(player, false);

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || skillInfo.IsFlying) return;

            Vector? safePoint = GetCorrectPosition(player, skillInfo);
            playerPawn.Teleport(safePoint ?? skillInfo.LastPosition, null, new Vector(0, 0, 0));
        }

        private static Vector? GetCorrectPosition(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return null;

            Vector currentPos = playerPawn.AbsOrigin;
            float offset = 50;

            Vector[] checkOffsets =
            {
                currentPos,
                currentPos + new Vector(offset, 0, 10),
                currentPos + new Vector(-offset, 0, 10),
                currentPos + new Vector(0, offset, 10),
                currentPos + new Vector(0, -offset, 10),

                currentPos + new Vector(offset, 0, 60),
                currentPos + new Vector(-offset, 0, 60),
                currentPos + new Vector(0, offset, 60),
                currentPos + new Vector(0, -offset, 60),
            };

            ulong mask = playerPawn.Collision.CollisionAttribute.InteractsWith;
            ulong contents = playerPawn.Collision.CollisionGroup;
            bool hasGround = false;

            foreach (Vector targetPos in checkOffsets)
            {
                Vector start = new(targetPos.X, targetPos.Y, targetPos.Z + 70);
                Vector end = new(targetPos.X, targetPos.Y, targetPos.Z - 1000);

                CGameTrace groundTrace = TraceRay.TraceShape(start, end, mask, contents, player);
                if (!groundTrace.DidHit()) continue;

                hasGround = true;
                Vector newPos =
                    groundTrace.EndPos.Z > targetPos.Z
                    ? new(groundTrace.EndPos.X, groundTrace.EndPos.Y, groundTrace.EndPos.Z)
                    : targetPos;

                if (IsPositionSafe(newPos, player))
                    return newPos;
            }
            
            if (hasGround)
                skillInfo.Cooldown = DateTime.Now.AddSeconds(-SkillsInfo.GetValue<float>(skillName, "cooldown") + SkillsInfo.GetValue<float>(skillName, "cooldownWhenStuck"));
            return hasGround ? currentPos : null;
        }

        private static bool IsPositionSafe(Vector pos, CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return false;

            float footHeight = 0;
            float headHeight = 70;
            float innerDist = 12;

            ulong mask = playerPawn.Collision.CollisionAttribute.InteractsWith;
            ulong contents = playerPawn.Collision.CollisionGroup;

            Vector s1 = new(pos.X - innerDist, pos.Y - innerDist, pos.Z + footHeight);
            Vector e1 = new(pos.X + innerDist, pos.Y + innerDist, pos.Z + headHeight);
            CGameTrace t1 = TraceRay.TraceShape(s1, e1, mask, contents, player);
            if (t1.DidHit() || t1.AllSolid) return false;

            Vector s2 = new(pos.X + innerDist, pos.Y - innerDist, pos.Z + footHeight);
            Vector e2 = new(pos.X - innerDist, pos.Y + innerDist, pos.Z + headHeight);
            CGameTrace t2 = TraceRay.TraceShape(s2, e2, mask, contents, player);
            if (t2.DidHit() || t2.AllSolid) return false;

            return true;
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public bool IsFlying { get; set; }
            public DateTime Cooldown { get; set; }
            public Vector? LastPosition { get; set; }
            public Timer? Timer { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#44ebd4", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float cooldown = 30f, float duration = 2f, float cooldownWhenStuck = 5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Cooldown { get; set; } = cooldown;
            public float CooldownWhenStuck { get; set; } = cooldownWhenStuck;
            public float Duration { get; set; } = duration;
        }
    }
}