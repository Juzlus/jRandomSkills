using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Push : ISkill
    {
        private const Skills skillName = Skills.Push;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim) || attacker == victim)
                return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);

            if (playerInfo?.Skill == skillName && victim!.PawnIsAlive)
            {
                if (Instance.Random.NextDouble() <= playerInfo.SkillChance)
                    PushEnemy(victim, attacker!.PlayerPawn.Value!.EyeAngles);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            float newChance = (float)Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "ChanceTo") - SkillsInfo.GetValue<float>(skillName, "ChanceFrom")) + SkillsInfo.GetValue<float>(skillName, "ChanceFrom");
            playerInfo.SkillChance = newChance;
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, newChance)}", false);
        }

        private static void PushEnemy(CCSPlayerController player, QAngle attackerAngle)
        {
            if (player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid || player.PlayerPawn.Value.LifeState != (int)LifeState_t.LIFE_ALIVE)
                return;

            var currentPosition = player.PlayerPawn.Value.AbsOrigin;
            var currentAngles = player.PlayerPawn.Value.EyeAngles;

            Vector newVelocity = SkillUtils.GetForwardVector(attackerAngle) * SkillsInfo.GetValue<float>(skillName, "pushVelocity");
            newVelocity.Z = player.PlayerPawn.Value.AbsVelocity.Z + SkillsInfo.GetValue<float>(skillName, "jumpVelocity");

            player.PlayerPawn.Value.Teleport(currentPosition, currentAngles, newVelocity);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#1e9ab0", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float chanceFrom = .3f, float chanceTo = .4f, float jumpVelocity = 300f, float pushVelocity = 400f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
            public float JumpVelocity { get; set; } = jumpVelocity;
            public float PushVelocity { get; set; } = pushVelocity;
        }
    }
}