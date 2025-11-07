using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Behind : ISkill
    {
        private const Skills skillName = Skills.Behind;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = @event!.Attacker;
            var victim = @event!.Userid;

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim) || attacker == victim) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);

            if (playerInfo?.Skill == skillName && victim!.PawnIsAlive)
                if (Instance.Random.NextDouble() <= playerInfo.SkillChance)
                    RotateEnemy(victim);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            float newChance = (float)Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "ChanceTo") - SkillsInfo.GetValue<float>(skillName, "ChanceFrom")) + SkillsInfo.GetValue<float>(skillName, "ChanceFrom");
            playerInfo.SkillChance = newChance;
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, newChance)}", false);
        }

        private static void RotateEnemy(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player.PlayerPawn.Value;

            if (pawn == null || !pawn.IsValid || pawn.LifeState != (int)LifeState_t.LIFE_ALIVE) return;

            var currentPosition = pawn.AbsOrigin;
            var currentAngles = pawn.EyeAngles;

            QAngle newAngles = new(
                currentAngles.X,
                currentAngles.Y + 180,
                currentAngles.Z
            );

            pawn.Teleport(currentPosition, newAngles, new Vector(0, 0, 0));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#00FF00", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float chanceFrom = .2f, float chanceTo = .4f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
        }
    }
}