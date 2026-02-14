using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class PawelJumper : ISkill
    {
        private const Skills skillName = Skills.PawelJumper;
        private static readonly int?[] J = new int?[64];
        private static readonly PlayerButtons[] LB = new PlayerButtons[64];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!Instance.IsPlayerValid(player)) return;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    GiveAdditionalJump(player);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerPawn == null || playerInfo == null) return;

            var skillConfig = SkillsInfo.LoadedConfig.FirstOrDefault(s => s.Name == skillName.ToString());
            if (skillConfig == null) return;

            float extraJumps = (float)Instance.Random.Next(SkillsInfo.GetValue<int>(skillName, "extraJumpsMin"), SkillsInfo.GetValue<int>(skillName, "extraJumpsMax") + 1);
            playerInfo.SkillChance = extraJumps;
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, extraJumps)}",
                border: !Utilities.GetPlayers().Any(p => p.Team == player.Team && !p.IsBot && p != player) ? "tb" : "t");
        }

        private static void GiveAdditionalJump(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;

            var flags = (PlayerFlags)playerPawn.Flags;
            var buttons = player.Buttons;

            bool isJumpDown = (playerPawn.MovementServices?.Buttons?.ButtonStates[0] & (ulong)PlayerButtons.Jump) != 0 || (buttons & PlayerButtons.Jump) != 0;
            bool wasJumpDown = (LB[player.Slot] & PlayerButtons.Jump) != 0;

            bool jumpPressed = (isJumpDown && !wasJumpDown)
                || (playerPawn.MovementServices?.QueuedButtonChangeMask & (ulong)PlayerButtons.Jump) != 0;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerPawn == null || playerInfo == null) return;

            bool isOnGround = (flags & PlayerFlags.FL_ONGROUND) != 0;

            if (isOnGround)
                J[player.Slot] = 0;
            else if (jumpPressed && J[player.Slot] < playerInfo.SkillChance + 1)
            {
                J[player.Slot]++;
                playerPawn.AbsVelocity.Z = 300;
            }

            LB[player.Slot] = buttons;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#FFA500", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int extraJumpsMin = 1, int extraJumpsMax = 4) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int ExtraJumpsMin { get; set; } = extraJumpsMin;
            public int ExtraJumpsMax { get; set; } = extraJumpsMax;
        }
    }
}