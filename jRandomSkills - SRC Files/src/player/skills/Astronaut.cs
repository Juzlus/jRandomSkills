using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Astronaut : ISkill
    {
        private const Skills skillName = Skills.Astronaut;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            ApplyGravityModifier(player);
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
                DisableSkill(player);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;

            player.PlayerPawn.Value.ActualGravityScale = 1;
        }

        private static void ApplyGravityModifier(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;

            float gravityModifier = (float)Math.Round(Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "ChanceTo") - SkillsInfo.GetValue<float>(skillName, "chanceFrom")) + SkillsInfo.GetValue<float>(skillName, "chanceFrom"), 1);
            playerInfo.SkillChance = gravityModifier;
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, gravityModifier)}", false);
            player.PlayerPawn.Value.ActualGravityScale = gravityModifier;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7E10AD", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float chanceFrom = .1f, float chanceTo = .7f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
        }
    }
}