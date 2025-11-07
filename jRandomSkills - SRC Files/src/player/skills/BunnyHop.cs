using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class BunnyHop : ISkill
    {
        private const Skills skillName = Skills.BunnyHop;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    GiveBunnyHop(player);
            }
        }

        private static void GiveBunnyHop(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;
            if (JumpBan.bannedPlayers.ContainsKey(playerPawn)) return;

            var flags = (PlayerFlags)playerPawn.Flags;
            var buttons = player.Buttons;

            if (buttons.HasFlag(PlayerButtons.Jump) && flags.HasFlag(PlayerFlags.FL_ONGROUND) && !playerPawn.MoveType.HasFlag(MoveType_t.MOVETYPE_LADDER))
            {
                playerPawn.AbsVelocity.Z = SkillsInfo.GetValue<float>(skillName, "jumpVelocity");
                var maxSpeed = SkillsInfo.GetValue<float>(skillName, "maxSpeed");

                var vX = playerPawn.AbsVelocity.X;
                var vY = playerPawn.AbsVelocity.Y;
                var speed2D = Math.Sqrt(vX * vX + vY * vY);
                var scale = 1d;

                if (speed2D < maxSpeed)
                {
                    var newSpeed = Math.Min(speed2D * SkillsInfo.GetValue<float>(skillName, "jumpBoost"), maxSpeed);
                    scale = newSpeed / (speed2D == 0 ? 1 : speed2D);
                }
                else if (speed2D > maxSpeed)
                    scale = maxSpeed / speed2D;

                playerPawn.AbsVelocity.X = (float)(vX * scale);
                playerPawn.AbsVelocity.Y = (float)(vY * scale);
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d1430a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float maxSpeed = 500f, float jumpVelocity = 300f, float jumpBoost = 2f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float MaxSpeed { get; set; } = maxSpeed;
            public float JumpVelocity { get; set; } = jumpVelocity;
            public float JumpBoost { get; set; } = jumpBoost;
        }
    }
}