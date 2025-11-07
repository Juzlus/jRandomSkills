using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class PsychicDefusing : ISkill
    {
        private const Skills skillName = Skills.PsychicDefusing;
        private static readonly ConcurrentDictionary<CCSPlayerPawn, PlayerSkillInfo> SkillPlayerInfo = [];
        private static Vector? bombLocation = null;
        private static readonly float tickRate = 64f;
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                SkillPlayerInfo.Clear();
                bombLocation = null;
            }
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill == skillName)
                SkillPlayerInfo.TryRemove(pawn, out _);
        }

        public static void BombPlanted(EventBombPlanted _)
        {
            var plantedBomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
            if (plantedBomb != null)
                bombLocation = plantedBomb.AbsOrigin;
        }

        public static void OnTick()
        {
            if (bombLocation == null) return;
            foreach (var skillInfo in SkillPlayerInfo)
            {
                var pawn = skillInfo.Key;
                var info = skillInfo.Value;

                var playerController = pawn.Controller.Value;
                if (playerController == null || !pawn.Controller.IsValid) return;

                var player = playerController.As<CCSPlayerController>();
                if (player == null || !player.IsValid) return;

                if (pawn.AbsOrigin == null || SkillUtils.GetDistance(pawn.AbsOrigin, bombLocation) > SkillsInfo.GetValue<float>(skillName, "maxDefusingRange"))
                {
                    info.Defusing = false;
                    info.DefusingTime = SkillsInfo.GetValue<float>(skillName, "defusingTime");
                    SkillUtils.ResetPrintHTML(player);
                    continue;
                }

                if (!info.Defusing)
                    pawn.EmitSound("c4.disarmstart");
                info.Defusing = true;
                info.DefusingTime -= (1f / tickRate);

                if (info.DefusingTime <= 0)
                {
                    var plantedBomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
                    if (plantedBomb != null)
                    {
                        plantedBomb.AcceptInput("Kill");
                        SkillUtils.TerminateRound(CsTeam.CounterTerrorist);
                    }
                    SkillUtils.ResetPrintHTML(player);
                    SkillPlayerInfo.Clear();
                }

                UpdateHUD(player, info);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;
            SkillPlayerInfo.TryAdd(pawn, new PlayerSkillInfo
            {
                SteamID = player.SteamID,
                Defusing = false,
                DefusingTime = SkillsInfo.GetValue<float>(skillName, "defusingTime"),
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.ResetPrintHTML(player);
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;
            SkillPlayerInfo.TryRemove(pawn, out _);
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            if (!skillInfo.Defusing) return;
            int cooldown = (int)Math.Ceiling(skillInfo.DefusingTime);

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else 
                playerInfo.PrintHTML = $"{player.GetTranslation("psychicdefusing_hud_info", $"<font color='#00d5ff'>{cooldown}</font>")}";
        }
        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool Defusing { get; set; }
            public float DefusingTime { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#507529", CsTeam onlyTeam = CsTeam.CounterTerrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float maxDefusingRange = 80f, float defusingTime = 10f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float MaxDefusingRange { get; set; } = maxDefusingRange;
            public float DefusingTime { get; set; } = defusingTime;
        }
    }
}