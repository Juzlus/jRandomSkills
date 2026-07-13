using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Dash : ISkill
    {
        private const Skills skillName = Skills.Dash;
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                SkillPlayerInfo.Clear();
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.Index, new PlayerSkillInfo
            {
                Username = player.PlayerName,
                CanUse = true,
                Cooldown = DateTime.MinValue,
                Jumps = 0,
                WasOnGround = true,
                JumpReleasedTicks = 10
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            float cooldown = 0;
            if (skillInfo != null)
            {
                float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
                cooldown = Math.Max(time, 0);

                if (cooldown == 0 && skillInfo?.CanUse == false)
                    skillInfo.CanUse = true;
            }

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid) return;

                var eventPlayer = PlayerManager.GetPlayerEvent(player);
                var playerInfo = PlayerManager.GetPlayerByIndex(eventPlayer!.Index);

                if (playerInfo?.Skill == skillName)
                    if (SkillPlayerInfo.TryGetValue(eventPlayer!.Index, out var skillInfo))
                    {
                        UpdateHUD(player, skillInfo);
                        HandleDash(player, skillInfo);
                    }
            }
        }

        private static void HandleDash(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            var eventPlayer = PlayerManager.GetPlayerEvent(player);
            var eventPlayerPawn = eventPlayer?.PlayerPawn?.Value;
            if (eventPlayerPawn == null || !eventPlayerPawn.IsValid) return;

            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;

            var buttons = player.Buttons;

            bool jumpPressed = (buttons & PlayerButtons.Jump) != 0
                || (playerPawn.MovementServices?.QueuedButtonChangeMask & (ulong)PlayerButtons.Jump) != 0;

            bool isOnGround =
                eventPlayerPawn.GroundEntity != null
                && eventPlayerPawn.GroundEntity.IsValid;

            if (skillInfo.WasOnGround && !isOnGround && jumpPressed)
                skillInfo.Jumps = 0;
            else if (isOnGround)
                skillInfo.Jumps = 0;
            else if (!isOnGround && jumpPressed && skillInfo.Jumps < 1)
            {
                if (skillInfo.JumpReleasedTicks >= 3 && skillInfo.CanUse)
                {
                    skillInfo.Jumps++;
                    skillInfo.CanUse = false;
                    skillInfo.Cooldown = DateTime.Now;

                    float moveX = 0;
                    float moveY = 0;

                    PlayerButtons playerButtons = player.Buttons;
                    if (SkillsInfo.GetValue<bool>(skillName, "anyDirection"))
                    {
                        if (playerButtons.HasFlag(PlayerButtons.Forward))
                            moveY += 1;
                        if (playerButtons.HasFlag(PlayerButtons.Back))
                            moveY -= 1;
                        if (playerButtons.HasFlag(PlayerButtons.Moveleft))
                            moveX += 1;
                        if (playerButtons.HasFlag(PlayerButtons.Moveright))
                            moveX -= 1;

                        if (moveX == 0 && moveY == 0)
                            moveY = 1;
                    }
                    else
                        moveY = 1;

                    float moveAngle = MathF.Atan2(moveX, moveY) * (180f / MathF.PI);
                    QAngle dashAngles = new(0, eventPlayerPawn.EyeAngles.Y + moveAngle, 0);

                    Vector newVelocity = SkillUtils.GetForwardVector(dashAngles) * SkillsInfo.GetValue<float>(skillName, "pushVelocity");
                    newVelocity.Z = eventPlayerPawn.AbsVelocity.Z + SkillsInfo.GetValue<float>(skillName, "jumpVelocity");

                    eventPlayerPawn.AbsVelocity.X = newVelocity.X;
                    eventPlayerPawn.AbsVelocity.Y = newVelocity.Y;
                    eventPlayerPawn.AbsVelocity.Z = newVelocity.Z;
                }
            }

            skillInfo.WasOnGround = isOnGround;

            if (!jumpPressed)
                skillInfo.JumpReleasedTicks++;
            else
                skillInfo.JumpReleasedTicks = 0;
        }

        public class PlayerSkillInfo
        {
            public string? Username { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
            public int Jumps { get; set; }
            public bool WasOnGround { get; set; }
            public int JumpReleasedTicks { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#42bbfc", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float jumpVelocity = 150f, float pushVelocity = 600f, bool anyDirection = true, float cooldown = 2f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float JumpVelocity { get; set; } = jumpVelocity;
            public float PushVelocity { get; set; } = pushVelocity;
            public bool AnyDirection { get; set; } = anyDirection;
            public float Cooldown { get; set; } = cooldown;
        }
    }
}