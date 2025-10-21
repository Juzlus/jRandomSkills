using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Jackal : ISkill
    {
        private const Skills skillName = Skills.Jackal;
        private static readonly ConcurrentDictionary<ulong, byte> playersInAction = [];
        private static readonly ConcurrentDictionary<CCSPlayerController, ConcurrentQueue<CBeam>> stepBeams = [];
        private static readonly string particleName = "particles/ui/hud/ui_map_def_utility_trail.vpcf";

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.RegisterListener<Listeners.OnServerPrecacheResources>((ResourceManifest manifest) => manifest.AddResource(particleName));
        }

        public static void NewRound()
        {
            foreach (var beams in stepBeams.Values)
                foreach (var beam in beams)
                    if (beam != null && beam.IsValid)
                        beam.AcceptInput("Kill");
            stepBeams.Clear();
            playersInAction.Clear();
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach( var (info, player) in infoList)
            {
                if (player == null) continue;
                foreach (var step in stepBeams)
                {
                    var enemy = step.Key;
                    var beams = step.Value;

                    var observedPlayer = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == player?.Pawn?.Value?.ObserverServices?.ObserverTarget?.Value?.Handle);
                    var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                    var observerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == observedPlayer?.SteamID);

                    foreach (var beam in beams)
                    {
                        var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)beam.Index);
                        if (entity == null || !entity.IsValid) continue;

                        if (playerInfo?.Skill != skillName && observerInfo?.Skill != skillName)
                            info.TransmitEntities.Remove(entity.Index);
                        else if (enemy.Team == player.Team)
                            info.TransmitEntities.Remove(beam.Index);
                    }
                }
            }
        }

        public static void CreatePlayerTrail(CCSPlayerPawn playerPawn)
        {
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return;

            CParticleSystem particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system")!;
            if (particle == null) return;

            particle.EffectName = particleName;
            particle.StartActive = true;

            particle.Teleport(playerPawn.AbsOrigin);
            particle.DispatchSpawn();

            particle.AcceptInput("SetParent", playerPawn, particle, "!activator");
            particle.AcceptInput("Start");

            Instance.AddTimer(3f, () => {
                if (particle != null && particle.IsValid)
                    particle.AcceptInput("Kill");
                CreatePlayerTrail(playerPawn);
            });
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();
            playersInAction.TryAdd(player.SteamID, 0);
            foreach (var _player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV && p.PawnIsAlive && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist))
                if (!stepBeams.ContainsKey(_player))
                {
                    stepBeams.TryAdd(_player, []);
                    CreatePlayerTrail(player.PlayerPawn.Value);
                }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            playersInAction.TryRemove(player.SteamID, out _);
            if (playersInAction.IsEmpty)
                NewRound();
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#f542ef", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, int maxStepBeam = 100) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates)
        {
            public int MaxStepBeam { get; set; } = maxStepBeam;
        }
    }
}