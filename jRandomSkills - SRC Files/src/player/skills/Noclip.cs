using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

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
            var duration = SkillsInfo.GetValue<float>(skillName, "duration");
            if (playerPawn?.CBodyComponent == null) return;

            if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
            {
                if (!player.IsValid || !player.PawnIsAlive) return;
                if (skillInfo.CanUse)
                {
                    skillInfo.CanUse = false;
                    skillInfo.IsFlying = true;
                    skillInfo.Cooldown = DateTime.Now;
                    skillInfo.LastPosition = playerPawn.AbsOrigin == null ? null : new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z);

                    playerPawn.ActualMoveType = MoveType_t.MOVETYPE_NOCLIP;
                    Instance.AddTimer(duration, () => {
                        if (playerPawn == null || !playerPawn.IsValid || !skillInfo.IsFlying) return;
                        skillInfo.IsFlying = false;
                        playerPawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                    });

                    Instance.AddTimer(duration + 4, () => {
                        if (playerPawn == null || !playerPawn.IsValid || !player.PawnIsAlive || skillInfo.IsFlying) return;
                        if (skillInfo.LastPosition == null || playerPawn.AbsOrigin == null) return;
                        skillInfo.IsFlying = false;
                        var diff = Math.Abs(playerPawn.AbsOrigin.Z - skillInfo.LastPosition.Z);
                        if (diff > 3000 && playerPawn.AbsOrigin.Z < skillInfo.LastPosition.Z)
                            playerPawn.Teleport(skillInfo.LastPosition, null, new Vector(0,0,0));
                    });
                }
                else if (skillInfo.IsFlying)
                {
                    skillInfo.IsFlying = false;
                    playerPawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;

                    Instance.AddTimer(4, () => {
                        if (playerPawn == null || !playerPawn.IsValid || !player.PawnIsAlive || skillInfo.IsFlying) return;
                        if (skillInfo.LastPosition == null || playerPawn.AbsOrigin == null) return;
                        skillInfo.IsFlying = false;
                        var diff = Math.Abs(playerPawn.AbsOrigin.Z - skillInfo.LastPosition.Z);
                        if (diff > 3000 && playerPawn.AbsOrigin.Z < skillInfo.LastPosition.Z)
                            playerPawn.Teleport(skillInfo.LastPosition, null, new Vector(0, 0, 0));
                    });
                }
            }
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public bool IsFlying { get; set; }
            public DateTime Cooldown { get; set; }
            public Vector? LastPosition { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#44ebd4", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float cooldown = 30f, float duration = 2f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Cooldown { get; set; } = cooldown;
            public float Duration { get; set; } = duration;
        }
    }
}