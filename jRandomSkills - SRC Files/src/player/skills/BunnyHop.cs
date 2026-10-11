using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class BunnyHop : ISkill
    {
        private const Skills skillName = Skills.BunnyHop;
        private static readonly ConcurrentDictionary<uint, int> playersLastJump = [];
        private static readonly ConcurrentDictionary<uint, (float X, float Y)> airVelocity = [];
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void NewRound()
        {
            playersLastJump.Clear();
            airVelocity.Clear();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            playersLastJump.TryRemove(playerIndex, out _);
            airVelocity.TryRemove(playerIndex, out _);
        }

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        // Nothing is ever replicated to the client for this skill. sv_autobunnyhopping / sv_enablebunnyhopping /
        // the stamina cvars drive client-side movement prediction; any value the client holds that differs
        // from what the server simulates makes every jump mispredict. The hop is server-side only.
        public static void DisableSkill(CCSPlayerController player) { }

        public static void OnTick()
        {
            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            if (holderBuffer.Count == 0) return;

            foreach (var playerEvent in holderBuffer)
            {
                var player = PlayerManager.GetPlayerFromEvent(playerEvent) ?? playerEvent;
                if (player == null || !player.IsValid) continue;

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
            if (eventPlayerPawn.MoveType.HasFlag(MoveType_t.MOVETYPE_LADDER) || eventPlayerPawn.MoveType == MoveType_t.MOVETYPE_NONE) return;

            // No landing stamina penalty: the game would otherwise slow every hop and make the chain feel jerky.
            var movement = playerPawn.MovementServices;
            if (movement != null)
                Schema.SetSchemaValue(movement.Handle, "CCSPlayer_MovementServices", "m_flStamina", 0f);

            // Hold jump: as long as the key is down every landing turns straight into the next hop.
            bool jumpHeld = (player.Buttons & PlayerButtons.Jump) != 0;
            bool onGround = flags.HasFlag(PlayerFlags.FL_ONGROUND);

            float vX = eventPlayerPawn.AbsVelocity.X;
            float vY = eventPlayerPawn.AbsVelocity.Y;

            if (!onGround)
            {
                // Remember the air speed: the game clamps it on landing (sv_enablebunnyhopping 0) and this
                // is what gets restored on the next hop, like autobhop with the cap disabled.
                airVelocity[eventPlayer!.Index] = (vX, vY);
                return;
            }

            if (!jumpHeld) return;

            // Only once per landing: a new hop needs a tick in the air in between.
            if (playersLastJump.TryGetValue(eventPlayer!.Index, out int lastTick) && lastTick + 1 >= Server.TickCount) return;
            playersLastJump[eventPlayer.Index] = Server.TickCount;

            float jumpVelocity = SkillsInfo.GetValue<float>(skillName, "jumpVelocity");
            float maxSpeed = SkillsInfo.GetValue<float>(skillName, "maxSpeed");
            float boost = Math.Clamp(SkillsInfo.GetValue<float>(skillName, "jumpBoost"), 1f, 1.5f);

            // Start from the pre-landing air speed if the landing clamp took some away.
            if (airVelocity.TryGetValue(eventPlayer.Index, out var air))
            {
                float airSpeed = MathF.Sqrt(air.X * air.X + air.Y * air.Y);
                float groundSpeed = MathF.Sqrt(vX * vX + vY * vY);
                if (airSpeed > groundSpeed) { vX = air.X; vY = air.Y; }
            }

            // The hop keeps the player's speed (plus the optional boost) but never above maxSpeed: without the
            // cap a push from another skill or a long fall was carried from hop to hop and felt like flying.
            float speed2D = MathF.Sqrt(vX * vX + vY * vY);
            float target = speed2D < 10f ? speed2D : Math.Min(speed2D * boost, maxSpeed);
            float scale = speed2D > 0f ? target / speed2D : 1f;

            // Written straight into the velocity (no Teleport): a teleport each hop is what made it feel laggy.
            eventPlayerPawn.AbsVelocity.X = vX * scale;
            eventPlayerPawn.AbsVelocity.Y = vY * scale;
            eventPlayerPawn.AbsVelocity.Z = jumpVelocity;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d1430a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float maxSpeed = 500f, float jumpVelocity = 301.99f, float jumpBoost = 1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float MaxSpeed { get; set; } = maxSpeed;
            public float JumpVelocity { get; set; } = jumpVelocity;
            public float JumpBoost { get; set; } = jumpBoost;
        }
    }
}