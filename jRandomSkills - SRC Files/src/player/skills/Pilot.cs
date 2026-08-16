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
        private static readonly ConcurrentDictionary<uint, Pilot_PlayerInfo> PlayerPilotInfo = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(SkillsInfo.GetValue<string>(skillName, "particleName"));
        }

        public static void NewRound()
        {
            foreach (var pilotInfo in PlayerPilotInfo.Values)
                ClearTrail(pilotInfo);

            PlayerPilotInfo.Clear();
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            if (PlayerPilotInfo.TryGetValue(player.Index, out var pilotInfo))
            {
                ClearTrail(pilotInfo);
                pilotInfo.IsFlying = false;
            }
        }

        public static void OnTick()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill == skillName)
                    HandlePilot(player);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            PlayerPilotInfo.TryAdd(player.Index, new Pilot_PlayerInfo { SteamID = player.Index });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (PlayerPilotInfo.TryRemove(player.Index, out var pilotInfo))
                ClearTrail(pilotInfo);

            SkillUtils.ResetPrintHTML(player);
        }

        private static void HandlePilot(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;

            if (!PlayerPilotInfo.TryGetValue(player.Index, out var pilotInfo)) return;

            var flags = (PlayerFlags)playerPawn.Flags;
            var buttons = player.Buttons;
            float currentTime = Server.CurrentTime;

            bool isJumpDown = (buttons & PlayerButtons.Jump) != 0
                || (playerPawn.MovementServices?.Buttons?.ButtonStates[0] & (ulong)PlayerButtons.Jump) != 0;
            bool wasJumpDown = (pilotInfo.LastButtons & PlayerButtons.Jump) != 0;
            bool jumpPressed = (isJumpDown && !wasJumpDown)
                || (playerPawn.MovementServices?.QueuedButtonChangeMask & (ulong)PlayerButtons.Jump) != 0;
            bool isOnGround = (flags & PlayerFlags.FL_ONGROUND) != 0;

            if (jumpPressed && !isOnGround)
            {
                if (pilotInfo.JumpCount == 0)
                    pilotInfo.JumpCount++;
                else
                {
                    pilotInfo.JumpCount++;
                    pilotInfo.LastJumpTime = currentTime;

                    if (pilotInfo.JumpCount >= 2)
                        pilotInfo.IsFlying = true;
                }
            }
            else if (!isJumpDown && currentTime - pilotInfo.LastJumpTime > .1f)
                pilotInfo.IsFlying = false;

            if (isOnGround)
            {
                pilotInfo.IsFlying = false;
                if (isOnGround) pilotInfo.JumpCount = 0;
            }

            bool inUse = pilotInfo.IsFlying && pilotInfo.Fuel > 0 && !isOnGround;

            float maximumFuel = SkillsInfo.GetValue<float>(skillName, "maximumFuel");
            float consumption = SkillsInfo.GetValue<float>(skillName, "fuelConsumption");
            float refuelling = SkillsInfo.GetValue<float>(skillName, "refuelling");

            pilotInfo.Fuel = Math.Clamp(
                pilotInfo.Fuel + (inUse ? -consumption : refuelling),
                0,
                maximumFuel
            );

            if (inUse)
            {
                ApplyPilotEffect(player);
                StartTrail(player, playerPawn, pilotInfo);
            }
            else
            {
                PauseTrail(pilotInfo);

                if (isOnGround)
                    ClearTrail(pilotInfo);

                if (pilotInfo.Fuel <= 0)
                    pilotInfo.IsFlying = false;
            }

            pilotInfo.LastButtons = buttons;
            UpdateHUD(player, pilotInfo);
        }

        private static void UpdateHUD(CCSPlayerController player, Pilot_PlayerInfo pilotInfo)
        {
            if (pilotInfo == null) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
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
            playerInfo.PrintHTML = $"{player.GetTranslation("pilot_hud_info")}: <font color='{fuelColor}'>{(pilotInfo.Fuel / maximumFuel) * 100:F0}%</font>";
        }

        private static string GetFuelColor(float fuelPercentage)
        {
            var maximumFuel = SkillsInfo.GetValue<float>(skillName, "maximumFuel");
            if (fuelPercentage > (maximumFuel / 2f)) return "#00FF00";
            if (fuelPercentage > (maximumFuel / 4f)) return "#FFFF00";
            return "#FF0000";
        }

        private static void StartTrail(CCSPlayerController player, CCSPlayerPawn pawn, Pilot_PlayerInfo pilotInfo)
        {
            if (pilotInfo.TrailIndex != null)
            {
                var existing = Utilities.GetEntityFromIndex<CParticleSystem>((int)pilotInfo.TrailIndex.Value);
                if (existing != null && existing.IsValid)
                {
                    ResumeTrail(pilotInfo);
                    return;
                }

                ClearTrail(pilotInfo);
            }

            if (pawn.AbsOrigin == null) return;

            var particle = EntityManager.CreateTrackedParticleSystem(player.Index, SkillsInfo.GetValue<string>(skillName, "particleName"));
            if (particle == null || !particle.IsValid) return;

            float offset = SkillsInfo.GetValue<float>(skillName, "particleOffset");
            particle.Teleport(new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + offset));
            particle.AcceptInput("SetParent", pawn, particle, "!activator");
            particle.AcceptInput("Start");

            pilotInfo.TrailIndex = particle.Index;
            pilotInfo.TrailActive = true;
        }

        private static void ResumeTrail(Pilot_PlayerInfo pilotInfo)
        {
            if (pilotInfo.TrailActive || pilotInfo.TrailIndex == null) return;

            var particle = Utilities.GetEntityFromIndex<CParticleSystem>((int)pilotInfo.TrailIndex.Value);
            if (particle != null && particle.IsValid)
                particle.AcceptInput("Start");

            pilotInfo.TrailActive = true;
        }

        private static void PauseTrail(Pilot_PlayerInfo pilotInfo)
        {
            if (!pilotInfo.TrailActive || pilotInfo.TrailIndex == null) return;

            var particle = Utilities.GetEntityFromIndex<CParticleSystem>((int)pilotInfo.TrailIndex.Value);
            if (particle != null && particle.IsValid)
                particle.AcceptInput("Stop");

            pilotInfo.TrailActive = false;
        }

        private static void StopTrail(Pilot_PlayerInfo pilotInfo)
        {
            if (pilotInfo.TrailIndex == null) return;

            uint index = pilotInfo.TrailIndex.Value;
            pilotInfo.TrailIndex = null;
            pilotInfo.TrailActive = false;

            var particle = Utilities.GetEntityFromIndex<CParticleSystem>((int)index);
            if (particle != null && particle.IsValid)
            {
                particle.AcceptInput("Stop");
                particle.AcceptInput("DestroyImmediately");
            }

            EntityManager.DestroyEntity(index);

            if (EntityManager.SuppressKills) return;

            Server.NextFrame(() =>
            {
                var leftover = Utilities.GetEntityFromIndex<CParticleSystem>((int)index);
                if (leftover != null && leftover.IsValid)
                    leftover.Remove();
            });
        }

        private static void ClearTrail(Pilot_PlayerInfo pilotInfo)
        {
            StopTrail(pilotInfo);

            foreach (var leftover in EntityManager.GetPlayerEntities((uint)pilotInfo.SteamID, "particle_system"))
                EntityManager.DestroyEntity(leftover);
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
            public required ulong SteamID { get; set; }
            public float Fuel { get; set; } = SkillsInfo.GetValue<float>(skillName, "maximumFuel");
            public PlayerButtons LastButtons { get; set; } = 0;
            public int JumpCount { get; set; } = 0;
            public float LastJumpTime { get; set; } = 0;
            public bool IsFlying { get; set; } = false;
            public uint? TrailIndex { get; set; }
            public bool TrailActive { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#1466F5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float maximumFuel = 150f, float fuelConsumption = .64f, float refuelling = .1f, string particleName = "particles/inferno_fx/incgrenade_thrown_trail.vpcf", float particleOffset = 10f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public string ParticleName { get; set; } = particleName;
            public float ParticleOffset { get; set; } = particleOffset;
            public float MaximumFuel { get; set; } = maximumFuel;
            public float FuelConsumption { get; set; } = fuelConsumption;
            public float Refuelling { get; set; } = refuelling;
        }
    }
}