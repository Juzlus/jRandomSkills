using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills
{
    public class Jackal : ISkill
    {
        private const Skills skillName = Skills.Jackal;
        private static readonly ConcurrentDictionary<ulong, byte> playersInAction = [];
        private static readonly ConcurrentDictionary<uint, uint?> playersStep = [];
        private static readonly ConcurrentDictionary<ulong, Timer?> activeTimers = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(SkillsInfo.GetValue<string>(skillName, "particleName"));
        }

        public static void NewRound()
        {
            foreach (var particleIndex in playersStep.Values)
            {
                if (particleIndex == null) continue;
                var particle = Utilities.GetEntityFromIndex<CParticleSystem>((int)particleIndex);

                if (particle != null && particle.IsValid)
                    particle.AcceptInput("Kill");
            }
                
            playersStep.Clear();
            playersInAction.Clear();
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach( var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);

                var targetHandle = player.Pawn.Value?.ObserverServices?.ObserverTarget.Value?.Handle ?? nint.Zero;
                bool isObservingJackal = false;

                if (targetHandle != nint.Zero)
                {
                    var target = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == targetHandle);
                    var targetInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == target?.SteamID);
                    if (targetInfo?.Skill == skillName) isObservingJackal = true;
                }

                bool hasSkill = playerInfo?.Skill == skillName || isObservingJackal;

                foreach (var param in playersStep)
                {
                    var enemyIndex = param.Key;
                    var particleIndex = param.Value;

                    if (particleIndex == null) continue;

                    var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);
                    if (enemy == null || !enemy.IsValid) continue;

                    var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)particleIndex);
                    if (entity == null || !entity.IsValid) continue;

                    if (!hasSkill || enemy.Team == player.Team)
                        info.TransmitEntities.Remove(entity.Index);
                }
            }
        }

        public static void CreatePlayerTrail(CCSPlayerController? player)
        {
            if (player == null) return;
            var playerPawn = player.PlayerPawn.Value;

            ulong steamID = player.SteamID;
            if (activeTimers.TryRemove(steamID, out var oldTimer))
                oldTimer?.Kill();

            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            if (!playersStep.ContainsKey(player.Index)) return;

            CParticleSystem particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system")!;
            if (particle == null) return;

            particle.EffectName = SkillsInfo.GetValue<string>(skillName, "particleName");
            particle.StartActive = true;

            particle.Teleport(playerPawn.AbsOrigin);
            particle.DispatchSpawn();

            particle.AcceptInput("SetParent", playerPawn, particle, "!activator");
            particle.AcceptInput("Start");

            uint particleId = particle.Index;

            playersStep.AddOrUpdate(player.Index, particle.Index, (k, v) => particle.Index);

            activeTimers[player.SteamID] = Instance.AddTimer(2.5f, () => {
                var particle = Utilities.GetEntityFromIndex<CParticleSystem>((int)particleId);
                if (particle != null && particle.IsValid)
                    particle.AcceptInput("Kill");

                var player = Utilities.GetPlayerFromSteamId(steamID);
                if (player != null && player.IsValid && playersStep.ContainsKey(player.Index))
                    CreatePlayerTrail(player);
            });
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();
            playersInAction.TryAdd(player.SteamID, 0);
            foreach (var _player in Utilities.GetPlayers().Where(p => p.Team != player.Team && p.IsValid && !p.IsBot && !p.IsHLTV && p.PawnIsAlive && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist))
            {
                if (!playersStep.ContainsKey(_player.Index))
                    playersStep.TryAdd(_player.Index, null);
                CreatePlayerTrail(_player);
            }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            playersInAction.TryRemove(player.SteamID, out _);

            if (playersStep.TryRemove(player.Index, out var particleIndex) && particleIndex != null)
            {
                var particle = Utilities.GetEntityFromIndex<CParticleSystem>((int)particleIndex);
                if (particle != null && particle.IsValid)
                    particle.AcceptInput("Kill");
            }

            if (playersInAction.IsEmpty)
                NewRound();
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#f542ef", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", string particleName = "particles/ui/hud/ui_map_def_utility_trail.vpcf") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public string ParticleName { get; set; } = particleName;
        }
    }
}