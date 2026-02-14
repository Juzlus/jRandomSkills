using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Pilot : ISkill
    {
        private const Skills skillName = Skills.Pilot;
        private static readonly ConcurrentDictionary<ulong, Pilot_PlayerInfo> PlayerPilotInfo = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            PlayerPilotInfo.Clear();
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    HandlePilot(player);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            PlayerPilotInfo.TryAdd(player.SteamID, new Pilot_PlayerInfo
            {
                SteamID = player.SteamID,
                Fuel = SkillsInfo.GetValue<float>(skillName, "maximumFuel"),
                LastButtons = 0
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            PlayerPilotInfo.TryRemove(player.SteamID, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        private static void HandlePilot(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;

            if (!PlayerPilotInfo.TryGetValue(player.SteamID, out var pilotInfo)) return;

            var flags = (PlayerFlags)playerPawn.Flags;
            var buttons = player.Buttons;

            bool isJumpDown = (playerPawn.MovementServices?.Buttons?.ButtonStates[0] & (ulong)PlayerButtons.Jump) != 0 || (buttons & PlayerButtons.Jump) != 0;
            bool wasJumpDown = (pilotInfo.LastButtons & PlayerButtons.Jump) != 0;

            bool jumpPressed = (isJumpDown && !wasJumpDown)
                || (playerPawn.MovementServices?.QueuedButtonChangeMask & (ulong)PlayerButtons.Jump) != 0;

            bool isOnGround = (flags & PlayerFlags.FL_ONGROUND) != 0;
            bool inUse = jumpPressed && !isOnGround;

            var maximumFuel = SkillsInfo.GetValue<float>(skillName, "maximumFuel");
            pilotInfo.Fuel = Math.Min(
                Math.Max(
                    0,
                    pilotInfo.Fuel - (inUse 
                        ? SkillsInfo.GetValue<float>(skillName, "fuelConsumption")
                        : -SkillsInfo.GetValue<float>(skillName, "refuelling"))), 
                maximumFuel);
            pilotInfo.LastButtons = buttons;

            if (inUse && pilotInfo.Fuel > 0)
                ApplyPilotEffect(player);

            UpdateHUD(player, pilotInfo);
        }

        private static void UpdateHUD(CCSPlayerController player, Pilot_PlayerInfo pilotInfo)
        {
            if (pilotInfo == null) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            var maximumFuel = SkillsInfo.GetValue<float>(skillName, "maximumFuel");
            if (pilotInfo.Fuel == maximumFuel && playerInfo.SkillDescriptionHudExpired >= DateTime.Now)
            {
                playerInfo.PrintHTML = null;
                return;
            }

            var buttons = player.Buttons;
            float fuelPercentage = maximumFuel;

            string fuelColor = GetFuelColor(pilotInfo.Fuel);
            playerInfo.PrintHTML = $"{player.GetTranslation("pilot_hud_info")}: <font color='{fuelColor}'>{(pilotInfo.Fuel/maximumFuel)*100:F0}%</font>";
        }

        private static string GetFuelColor(float fuelPercentage)
        {
            var maximumFuel = SkillsInfo.GetValue<float>(skillName, "maximumFuel");
            if (fuelPercentage > (maximumFuel/2f)) return "#00FF00";
            if (fuelPercentage > (maximumFuel/4f)) return "#FFFF00";
            return "#FF0000";
        }

        private static void ApplyPilotEffect(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;
            
            QAngle eye_angle = playerPawn.EyeAngles;
            double pitch = (Math.PI / 180) * eye_angle.X;
            double yaw = (Math.PI / 180) * eye_angle.Y;
            Vector eye_vector = new((float)(Math.Cos(yaw) * Math.Cos(pitch)), (float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)(-Math.Sin(pitch)));

            Vector currentVelocity = playerPawn.AbsVelocity;

            Vector jetpackVelocity = new(
                eye_vector.X * 5.0f,
                eye_vector.Y * 5.0f,
                0.80f * 15.0f
            );

            float newVelocityX = currentVelocity.X + jetpackVelocity.X;
            float newVelocityY = currentVelocity.Y + jetpackVelocity.Y;
            float newVelocityZ = currentVelocity.Z + jetpackVelocity.Z;

            playerPawn.AbsVelocity.X = newVelocityX;
            playerPawn.AbsVelocity.Y = newVelocityY;
            playerPawn.AbsVelocity.Z = newVelocityZ;
        }


        public class Pilot_PlayerInfo
        {
            public ulong SteamID { get; set; }
            public float Fuel { get; set; }
            public PlayerButtons LastButtons { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#1466F5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float maximumFuel = 150f, float fuelConsumption = .64f, float refuelling = .1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float MaximumFuel { get; set; } = maximumFuel;
            public float FuelConsumption { get; set; } = fuelConsumption;
            public float Refuelling { get; set; } = refuelling;
        }
    }
}