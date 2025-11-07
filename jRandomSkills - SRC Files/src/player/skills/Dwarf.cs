using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Dwarf : ISkill
    {
        private const Skills skillName = Skills.Dwarf;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!Instance.IsPlayerValid(player)) continue;
                DisableSkill(player);
            }
        }

        public static unsafe void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;

            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn != null && player.IsValid)
            {
                float newSize = (float)Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "maxScale") - SkillsInfo.GetValue<float>(skillName, "minScale")) + SkillsInfo.GetValue<float>(skillName, "minScale");
                newSize = (float)Math.Round(newSize, 2);
                playerInfo.SkillChance = newSize;

                SkillUtils.ChangePlayerScale(player, newSize);
                SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, newSize)}", false);
            }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;

            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn != null && playerPawn?.CBodyComponent != null)
            {
                SkillUtils.ChangePlayerScale(player, 1);
                Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_CBodyComponent");
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffff00", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float minScale = .6f, float maxScale = .95f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float MinScale { get; set; } = minScale;
            public float MaxScale { get; set; } = maxScale;
        }
    }
}