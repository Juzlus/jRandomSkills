using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class Grapple : ISkill
    {
        private const Skills skillName = Skills.Grapple;

        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly ConcurrentDictionary<uint, byte> liveRopes = [];
        private static readonly object setLock = new();

        private static readonly Color terroristRope = Color.FromArgb(255, 138, 84, 34);
        private static readonly Color counterTerroristRope = Color.FromArgb(255, 52, 92, 138);

        private const double attemptCooldown = .3;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var skillInfo in SkillPlayerInfo.Values)
                    DestroyRope(skillInfo);

                SkillPlayerInfo.Clear();
                SweepOrphanRopes();
            }
        }

        public static void RoundEnd()
        {
            foreach (var skillInfo in SkillPlayerInfo.Values)
                DestroyRope(skillInfo);

            SweepOrphanRopes();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            SkillPlayerInfo[player.Index] = new PlayerSkillInfo
            {
                CanUse = true,
                Cooldown = DateTime.MinValue,
            };
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;

            if (SkillPlayerInfo.TryRemove(player.Index, out var skillInfo))
                DestroyRope(skillInfo);

            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            if (SkillPlayerInfo.TryRemove(playerIndex, out var skillInfo))
                DestroyRope(skillInfo);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
                DestroyRope(skillInfo);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) return;

            if (skillInfo.Anchor != null)
            {
                DestroyRope(skillInfo);
                return;
            }

            if (!skillInfo.CanUse) return;

            if ((DateTime.Now - skillInfo.LastAttempt).TotalSeconds < attemptCooldown) return;
            skillInfo.LastAttempt = DateTime.Now;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);

            if (!TryAttach(player, skillInfo))
            {
                if (playerEvent != null && playerEvent.IsValid)
                    playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("grapple_no_anchor_info"));
                return;
            }

            skillInfo.CanUse = false;
            skillInfo.Cooldown = DateTime.Now;
        }

        private static bool TryAttach(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            if (!RayTrace.IsAvailable) return false;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return false;

            var result = RayTrace.EyeTrace(player);
            if (result == null || !result.Value.DidHit) return false;
            if (result.Value.HitPlayer(out _)) return false;

            Vector anchor = new(result.Value.EndPosX, result.Value.EndPosY, result.Value.EndPosZ);

            Vector eyePos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            if (SkillUtils.GetDistance(eyePos, anchor) < SkillsInfo.GetValue<float>(skillName, "minDistance")) return false;

            skillInfo.Anchor = anchor;
            skillInfo.EndTick = Server.TickCount + (int)(SkillsInfo.GetValue<float>(skillName, "maxPullSeconds") * 64);

            if (!EntityManager.OverBudget())
            {
                Color ropeColor = player.Team == CsTeam.Terrorist ? terroristRope : counterTerroristRope;
                var rope = EntityManager.CreateTrackedBeam(player.Index, eyePos, anchor, ropeColor);

                if (rope != null && rope.IsValid)
                {
                    float width = SkillsInfo.GetValue<float>(skillName, "ropeWidth");
                    rope.Width = width;
                    rope.EndWidth = width;

                    Utilities.SetStateChanged(rope, "CBeam", "m_fWidth");
                    Utilities.SetStateChanged(rope, "CBeam", "m_fEndWidth");

                    uint ropeIndex = rope.Index;
                    skillInfo.RopeIndex = ropeIndex;
                    liveRopes[ropeIndex] = 0;

                    jRandomSkills.Instance.AddTimer(SkillsInfo.GetValue<float>(skillName, "maxPullSeconds") + 1f,
                        () => KillRope(ropeIndex),
                        CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                }
            }

            player.EmitSound("SolidMetal.BulletImpact");
            return true;
        }

        private static void DestroyRope(PlayerSkillInfo skillInfo)
        {
            skillInfo.Anchor = null;
            skillInfo.EndTick = 0;

            if (skillInfo.RopeIndex == null) return;

            uint ropeIndex = skillInfo.RopeIndex.Value;
            skillInfo.RopeIndex = null;

            KillRope(ropeIndex);
        }

        private static void KillRope(uint ropeIndex)
        {
            if (!liveRopes.TryRemove(ropeIndex, out _)) return;

            var rope = Utilities.GetEntityFromIndex<CBeam>((int)ropeIndex);
            if (rope != null && rope.IsValid)
            {
                rope.Width = 0;
                rope.EndWidth = 0;
                rope.Render = Color.FromArgb(0, 0, 0, 0);

                Utilities.SetStateChanged(rope, "CBeam", "m_fWidth");
                Utilities.SetStateChanged(rope, "CBeam", "m_fEndWidth");
                Utilities.SetStateChanged(rope, "CBaseModelEntity", "m_clrRender");
            }

            EntityManager.DestroyEntity(ropeIndex);

            Server.NextFrame(() =>
            {
                var leftover = Utilities.GetEntityFromIndex<CBeam>((int)ropeIndex);
                if (leftover != null && leftover.IsValid)
                    leftover.Remove();
            });
        }

        private static void SweepOrphanRopes()
        {
            foreach (uint ropeIndex in liveRopes.Keys)
                KillRope(ropeIndex);
        }

        public static void OnTick()
        {
            if (SkillPlayerInfo.IsEmpty) return;

            bool hudFrame = SkillUtils.IsHudFrame();

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;
                if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) continue;
                if (PlayerManager.GetPlayerByIndex(player.Index)?.Skill != skillName) continue;

                if (skillInfo.Anchor != null)
                    HandlePull(player, skillInfo);

                if (hudFrame)
                    UpdateHUD(player, skillInfo);
            }
        }

        private static void HandlePull(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            {
                DestroyRope(skillInfo);
                return;
            }

            if (Server.TickCount >= skillInfo.EndTick)
            {
                DestroyRope(skillInfo);
                return;
            }

            Vector anchor = skillInfo.Anchor!;
            Vector eyePos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);

            float dx = anchor.X - eyePos.X;
            float dy = anchor.Y - eyePos.Y;
            float dz = anchor.Z - eyePos.Z;
            float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

            if (distance <= SkillsInfo.GetValue<float>(skillName, "stopDistance"))
            {
                DestroyRope(skillInfo);
                return;
            }

            float pullSpeed = SkillsInfo.GetValue<float>(skillName, "pullSpeed");
            float invLength = pullSpeed / distance;

            pawn.AbsVelocity.X = dx * invLength;
            pawn.AbsVelocity.Y = dy * invLength;
            pawn.AbsVelocity.Z = dz * invLength;

            if (skillInfo.RopeIndex == null) return;

            var rope = Utilities.GetEntityFromIndex<CBeam>((int)skillInfo.RopeIndex.Value);
            if (rope == null || !rope.IsValid)
            {
                liveRopes.TryRemove(skillInfo.RopeIndex.Value, out _);
                skillInfo.RopeIndex = null;
                return;
            }

            rope.Teleport(eyePos);
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
            float cooldown = Math.Max(time, 0);

            if (cooldown == 0 && !skillInfo.CanUse)
                skillInfo.CanUse = true;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            if (skillInfo.Anchor != null)
                playerInfo.PrintHTML = $"<font color='#00FF00'>{player.GetTranslation("grapple_pulling_info")}</font>";
            else if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public class PlayerSkillInfo
        {
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
            public DateTime LastAttempt { get; set; }
            public Vector? Anchor { get; set; }
            public uint? RopeIndex { get; set; }
            public int EndTick { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#38e0c4", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, float cooldown = 10f, float maxDistance = 1500f, float minDistance = 150f, float stopDistance = 90f, float pullSpeed = 850f, float maxPullSeconds = 3f, float ropeWidth = 0.8f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float MaxDistance { get; set; } = maxDistance;
            public float MinDistance { get; set; } = minDistance;
            public float StopDistance { get; set; } = stopDistance;
            public float PullSpeed { get; set; } = pullSpeed;
            public float MaxPullSeconds { get; set; } = maxPullSeconds;
            public float RopeWidth { get; set; } = ropeWidth;
        }
    }
}
