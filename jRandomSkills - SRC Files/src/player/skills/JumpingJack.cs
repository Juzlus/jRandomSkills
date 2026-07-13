using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class JumpingJack : ISkill
    {
        private const Skills skillName = Skills.JumpingJack;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerJump(EventPlayerJump @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            var playerEvent = PlayerManager.GetPlayerEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(playerEvent.Index);
            if (playerInfo?.Skill != skillName) return;

            SkillUtils.AddHealth(playerEvent.PlayerPawn.Value, SkillsInfo.GetValue<int>(skillName, "healthToAdd"));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#a86eff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int healthToAdd = 3) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int HealthToAdd { get; set; } = healthToAdd;
        }
    }
}