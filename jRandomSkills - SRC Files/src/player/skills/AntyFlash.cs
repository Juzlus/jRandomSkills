using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class AntyFlash : ISkill
    {
        private const Skills skillName = Skills.AntyFlash;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerBlind(EventPlayerBlind @event)
        {
            var player = @event.Userid;
            var attacker = @event.Attacker;
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            if (attacker == null || !attacker.IsValid) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            var attackerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker.SteamID);

            if (playerInfo?.Skill == skillName)
                playerPawn.FlashDuration = 0.0f;
            else if (attackerInfo?.Skill == skillName)
                playerPawn.FlashDuration = SkillsInfo.GetValue<float>(skillName, "flashDuration");
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.FlashbangGrenade);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#D6E6FF", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float flashDuration = 7f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float FlashDuration { get; set; } = flashDuration;
        }
    }
}