using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills
{
    public class Jackal : ISkill
    {
        private const Skills skillName = Skills.Jackal;
        private static readonly ConcurrentDictionary<ulong, byte> playersInAction = new();
        private static readonly ConcurrentDictionary<uint, uint?> playersStep = new();
        private static readonly ConcurrentDictionary<ulong, Timer?> activeTimers = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(SkillsInfo.GetValue<string>(skillName, "particleName"));
        }

        public static void NewRound()
        {
            var particleIds = playersStep.Values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
            foreach (var pid in particleIds)
                SkillUtils.SafeKillEntity<CParticleSystem>(pid);

            var timers = activeTimers.Values.ToArray();
            foreach (var t in timers)
                t?.Kill();

            playersStep.Clear();
            playersInAction.Clear();
            activeTimers.Clear();
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var (info, player) in infoList)
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

                foreach (var kv in playersStep.ToArray())
                {
                    var enemyIndex = kv.Key;
                    var particleIndex = kv.Value;
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

            var timer = Instance.AddTimer(2.5f, () =>
            {
                SkillUtils.SafeKillEntity<CParticleSystem>(particleId);

                var pl = Utilities.GetPlayerFromSteamId(steamID);
                if (pl != null && pl.IsValid && playersStep.ContainsKey(pl.Index))
                    CreatePlayerTrail(pl);
            });

            activeTimers.AddOrUpdate(player.SteamID, timer, (_, prev) =>
            {
                prev?.Kill();
                return timer;
            });
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();
            playersInAction.TryAdd(player.SteamID, 0);

            var opponents = Utilities.GetPlayers()
                .Where(p => p.Team != player.Team
                            && p.IsValid
                            && !p.IsBot
                            && !p.IsHLTV
                            && p.PawnIsAlive
                            && (p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist))
                .ToArray();

            foreach (var _player in opponents)
            {
                playersStep.TryAdd(_player.Index, null);
                CreatePlayerTrail(_player);
            }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            playersInAction.TryRemove(player.SteamID, out _);

            if (playersStep.TryRemove(player.Index, out var particleIndex) && particleIndex != null)
            {
                SkillUtils.SafeKillEntity<CParticleSystem>(particleIndex);
            }

            if (activeTimers.TryRemove(player.SteamID, out var t))
                t?.Kill();

            if (playersInAction.IsEmpty)
                NewRound();
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#f542ef", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = 1, Rarity rarity = Rarity.Common, string particleName = "particles/ui/hud/ui_map_def_utility_trail.vpcf") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public string ParticleName { get; set; } = particleName;
        }
    }
}