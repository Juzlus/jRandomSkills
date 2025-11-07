using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class ShortBomb : ISkill
    {
        private const Skills skillName = Skills.ShortBomb;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void BombPlanted(EventBombPlanted @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            if (playerInfo?.Skill != skillName) return;

            var plantedBomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
            if (plantedBomb != null)
                Server.NextFrame(() => plantedBomb.C4Blow = (float)Server.EngineTime + SkillsInfo.GetValue<int>(skillName, "detonationTime"));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#f5b74c", CsTeam onlyTeam = CsTeam.Terrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int detonationTime = 20) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int DetonationTime { get; set; } = detonationTime;
        }
    }
}