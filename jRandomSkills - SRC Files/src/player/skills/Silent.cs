using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Silent : ISkill
    {
        private const Skills skillName = Skills.Silent;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerMakeSound(UserMessage um)
        {
            var soundevent = um.ReadUInt("soundevent_hash");
            var userIndex = um.ReadUInt("source_entity_index");
            if (userIndex == 0) return;

            if (!Instance.footstepSoundEvents.Contains(soundevent) && !Instance.silentSoundEvents.Contains(soundevent))
                return;

            var player = Utilities.GetPlayers().FirstOrDefault(p => p.Pawn?.Value != null && p.Pawn.Value.IsValid && p.Pawn.Value.Index == userIndex);
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            if (playerInfo?.Skill != skillName) return;

            um.Recipients.Clear();
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#333333", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}