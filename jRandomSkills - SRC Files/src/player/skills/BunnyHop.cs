using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class BunnyHop : ISkill
    {
        private const Skills skillName = Skills.BunnyHop;
        private static readonly ConcurrentDictionary<uint, int> playersLastJump = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));
                if (playerInfo?.Skill == skillName)
                    GiveBunnyHop(player);
            }
        }

        private static void GiveBunnyHop(CCSPlayerController player)
        {
            var eventPlayer = PlayerManager.GetPlayerEvent(player);
            var eventPlayerPawn = eventPlayer?.PlayerPawn?.Value;
            if (eventPlayerPawn == null || !eventPlayerPawn.IsValid) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;

            if (JumpBan.bannedPlayers.ContainsKey(player.Index)) return;

            var flags = (PlayerFlags)eventPlayerPawn.Flags;
            var buttons = player.Buttons;

            if ((playerPawn.MovementServices?.QueuedButtonChangeMask & (ulong)PlayerButtons.Jump) != 0)
                playersLastJump.AddOrUpdate(eventPlayer!.Index, Server.TickCount, (_, _) => Server.TickCount);

            bool jumpPressed = buttons.HasFlag(PlayerButtons.Jump)
                || (playersLastJump.TryGetValue(eventPlayer!.Index, out int tick) && tick + 20 >= Server.TickCount);

            if (jumpPressed && flags.HasFlag(PlayerFlags.FL_ONGROUND) && !eventPlayerPawn.MoveType.HasFlag(MoveType_t.MOVETYPE_LADDER))
            {
                eventPlayerPawn.AbsVelocity.Z = SkillsInfo.GetValue<float>(skillName, "jumpVelocity");
                var maxSpeed = SkillsInfo.GetValue<float>(skillName, "maxSpeed");

                var vX = eventPlayerPawn.AbsVelocity.X;
                var vY = eventPlayerPawn.AbsVelocity.Y;
                var speed2D = Math.Sqrt(vX * vX + vY * vY);
                var scale = 1d;

                if (speed2D < maxSpeed)
                {
                    var newSpeed = Math.Min(speed2D * SkillsInfo.GetValue<float>(skillName, "jumpBoost"), maxSpeed);
                    scale = newSpeed / (speed2D == 0 ? 1 : speed2D);
                }
                else if (speed2D > maxSpeed)
                    scale = maxSpeed / speed2D;

                eventPlayerPawn.AbsVelocity.X = (float)(vX * scale);
                eventPlayerPawn.AbsVelocity.Y = (float)(vY * scale);
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d1430a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float maxSpeed = 500f, float jumpVelocity = 300f, float jumpBoost = 2f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float MaxSpeed { get; set; } = maxSpeed;
            public float JumpVelocity { get; set; } = jumpVelocity;
            public float JumpBoost { get; set; } = jumpBoost;
        }
    }
}