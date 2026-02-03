using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Dash : ISkill
    {
        private const Skills skillName = Skills.Dash;
        private static readonly ConcurrentDictionary<ulong, PlayerSkillInfo> SkillPlayerInfo = [];
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

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!Instance.IsPlayerValid(player)) return;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
                    {
                        UpdateHUD(player, skillInfo);
                        HandleDash(player, skillInfo);
                    }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.SteamID, new PlayerSkillInfo
            {
                SteamID = player.SteamID,
                CanUse = true,
                Cooldown = DateTime.MinValue,
                LastFlags = 0,
                Jumps = 0,
                LastButtons = 0
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryRemove(player.SteamID, out _);
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

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        private static void HandleDash(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;

            var flags = (PlayerFlags)playerPawn.Flags;
            var buttons = player.Buttons;

            bool jumpPressed = (buttons & PlayerButtons.Jump) != 0
                || (playerPawn.MovementServices?.QueuedButtonChangeMask & (ulong)PlayerButtons.Jump) != 0;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerPawn == null || playerInfo == null) return;

            bool wasOnGround = (skillInfo.LastFlags & PlayerFlags.FL_ONGROUND) != 0;
            bool isOnGround = (flags & PlayerFlags.FL_ONGROUND) != 0;

            if (wasOnGround && !isOnGround && jumpPressed)
                skillInfo.Jumps = 0;
            else if (isOnGround)
                skillInfo.Jumps = 0;
            else if (!isOnGround && jumpPressed && skillInfo.Jumps < 1)
            {
                bool lastJumpPressed = (skillInfo.LastButtons & PlayerButtons.Jump) != 0;
                if (!lastJumpPressed && skillInfo.CanUse)
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
                    QAngle dashAngles = new(0, playerPawn.EyeAngles.Y + moveAngle, 0);

                    Vector newVelocity = SkillUtils.GetForwardVector(dashAngles) * SkillsInfo.GetValue<float>(skillName, "pushVelocity");
                    newVelocity.Z = playerPawn.AbsVelocity.Z + SkillsInfo.GetValue<float>(skillName, "jumpVelocity");

                    playerPawn.AbsVelocity.X = newVelocity.X;
                    playerPawn.AbsVelocity.Y = newVelocity.Y;
                    playerPawn.AbsVelocity.Z = newVelocity.Z;
                }
            }

            skillInfo.LastFlags = flags;
            skillInfo.LastButtons = buttons;
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
            public PlayerFlags LastFlags { get; set; }
            public int Jumps { get; set; }
            public PlayerButtons LastButtons { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#42bbfc", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float jumpVelocity = 150f, float pushVelocity = 600f, bool anyDirection = true, float cooldown = 2f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float JumpVelocity { get; set; } = jumpVelocity;
            public float PushVelocity { get; set; } = pushVelocity;
            public bool AnyDirection { get; set; } = anyDirection;
            public float Cooldown { get; set; } = cooldown;
        }
    }
}