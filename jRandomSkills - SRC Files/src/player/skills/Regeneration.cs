using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Regeneration : ISkill
    {
        private const Skills skillName = Skills.Regeneration;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            if (Server.TickCount % (int)(64 * SkillsInfo.GetValue<float>(skillName, "cooldown")) != 0) return;
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill != skillName) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;
                SkillUtils.AddHealth(pawn, SkillsInfo.GetValue<int>(skillName, "healthToAdd"));
            }
        }

        public class SkillConfig : SkillsInfo.DefaultSkillInfo
        {
            public int HealthToAdd { get; set; }
            public float Cooldown { get; set; }
            public SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff462e", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int healthToAdd = 1, float cooldown = .25f) : base(skill, active, color, onlyTeam, needsTeammates)
            {
                HealthToAdd = healthToAdd;
                Cooldown = cooldown;
            }
        }
    }
}