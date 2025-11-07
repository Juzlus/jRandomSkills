using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class KillerFlash : ISkill
    {
        private const Skills skillName = Skills.KillerFlash;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerBlind(EventPlayerBlind @event)
        {
            var player = @event.Userid;
            var attacker = @event.Attacker;
            if (!Instance.IsPlayerValid(player) || !Instance.IsPlayerValid(attacker)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            var attackerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);

            if (attackerInfo?.Skill == skillName && playerInfo?.Skill != Skills.AntyFlash && player!.PlayerPawn.Value!.FlashDuration >= SkillsInfo.GetValue<float>(skillName, "flashDuration"))
                player?.PlayerPawn?.Value?.CommitSuicide(false, true);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.FlashbangGrenade);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#57bcff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float flashDuration = 1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float FlashDuration { get; set; } = flashDuration;
        }
    }
}