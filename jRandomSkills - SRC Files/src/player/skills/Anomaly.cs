using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Anomaly : ISkill
    {
        private const Skills skillName = Skills.Anomaly;
        private static readonly float tickRate = 64;
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
                    {
                        UpdateHUD(player, skillInfo);
                        if (Server.TickCount % tickRate != 0) return;
                        var pawn = player.PlayerPawn.Value;
                        if (pawn != null && pawn.IsValid && pawn.AbsOrigin != null)
                        {
                            if (skillInfo.LastRotations == null || skillInfo.LastPositions == null) continue;
                            skillInfo.LastPositions.Enqueue(new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z));
                            skillInfo.LastRotations.Enqueue(new QAngle(pawn.EyeAngles.X, pawn.EyeAngles.Y, pawn.EyeAngles.Z));
                            if (skillInfo.LastRotations.Count > SkillsInfo.GetValue<int>(skillName, "secondsInBack"))
                            {
                                skillInfo.LastPositions.TryDequeue(out _);
                                skillInfo.LastRotations.TryDequeue(out _);
                            }
                        }
                    }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.SteamID, new PlayerSkillInfo
            {
                SteamID = player.SteamID,
                CanUse = true,
                Cooldown = DateTime.MinValue,
                LastPositions = [],
                LastRotations = [], 
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
            if (skillInfo != null)
            {
                float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
                cooldown = Math.Max(time, 0);

                if (cooldown == 0 && skillInfo?.CanUse == false)
                    skillInfo.CanUse = true;
            }

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
            {
                if (!player.IsValid || !player.PawnIsAlive) return;
                if (skillInfo.CanUse)
                {
                    skillInfo.CanUse = false;
                    skillInfo.Cooldown = DateTime.Now;
                    if (skillInfo.LastRotations == null || skillInfo.LastRotations.IsEmpty || skillInfo.LastPositions == null || skillInfo.LastPositions.IsEmpty) return;
                    Vector? lastPosition = skillInfo.LastPositions.FirstOrDefault();
                    QAngle? lastRotation = skillInfo.LastRotations.FirstOrDefault();
                    if (lastPosition != null && lastRotation != null)
                        playerPawn.Teleport(lastPosition, lastRotation, null);
                }
            }
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
            public ConcurrentQueue<Vector>? LastPositions { get; set; }
            public ConcurrentQueue<QAngle>? LastRotations { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#a86eff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", int secondsInBack = 5, float cooldown = 15) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int SecondsInBack { get; set; } = secondsInBack;
            public float Cooldown { get; set; } = cooldown;
        }
    }
}