using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Entities;
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
        private static Timer? mainSkillTimer = null;
        private static readonly ConcurrentDictionary<uint, uint?> activeTrails = [];
        private static readonly ConcurrentDictionary<uint, byte> playersInAction = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(SkillsInfo.GetValue<string>(skillName, "particleName"));
        }

        public static void NewRound()
        {
            if (mainSkillTimer != null)
            {
                mainSkillTimer.Kill();
                mainSkillTimer = null;
            }

            foreach (var index in activeTrails.Keys)
                EntityManager.DestroyPlayerEntities(index);

            activeTrails.Clear();
            playersInAction.Clear();
        }

        private static readonly List<(CsTeam OwnerTeam, uint RelayEntityIndex)> transmitTrails = [];

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            if (activeTrails.IsEmpty) return;

            transmitTrails.Clear();
            foreach (var (trackedPlayerIndex, relayIndex) in activeTrails)
            {
                if (relayIndex == null) continue;

                var trailOwner = Utilities.GetPlayerFromIndex((int)trackedPlayerIndex);
                if (trailOwner == null || !trailOwner.IsValid) continue;

                var relayEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)relayIndex);
                if (relayEntity == null || !relayEntity.IsValid) continue;

                transmitTrails.Add((trailOwner.Team, relayEntity.Index));
            }

            if (transmitTrails.Count == 0) return;

            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));
                bool isJackalOwner = playerInfo?.Skill == skillName;

                if (!isJackalOwner)
                {
                    var targetHandle = player.Pawn.Value?.ObserverServices?.ObserverTarget.Value?.Handle ?? nint.Zero;
                    if (targetHandle != nint.Zero)
                    {
                        var observed = PlayerManager.GetTickPlayers()
                            .FirstOrDefault(p => p?.Pawn?.Value?.Handle == targetHandle);
                        if (observed != null && observed.IsValid)
                        {
                            var observedInfo = PlayerManager.GetPlayerByIndex(observed.Index);
                            if (observedInfo?.Skill == skillName)
                                isJackalOwner = true;
                        }
                    }
                }

                var playerTeam = player.Team;
                foreach (var (ownerTeam, relayEntityIndex) in transmitTrails)
                {
                    if (playerTeam == ownerTeam || !isJackalOwner)
                        if (info.TransmitEntities.Contains(relayEntityIndex))
                            info.TransmitEntities.Remove(relayEntityIndex);
                }
            }
        }

        public static void CreatePlayerTrail(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null
                || playerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || playerPawn.Health <= 0) return; ;

            foreach (var old in EntityManager.GetPlayerEntities(player.Index, "prop_physics_multiplayer"))
                EntityManager.DestroyEntity(old);
            foreach (var old in EntityManager.GetPlayerEntities(player.Index, "particle_system"))
                EntityManager.DestroyEntity(old);

            var relay = EntityManager.CreateTrackedPhysicsProp(player.Index);
            if (relay == null || !relay.IsValid) return;

            // Register before parenting so the trail is filtered from its first snapshot.
            activeTrails[player.Index] = relay.Index;

            CBaseEntity target = playerPawn;
            var entities = EntityManager.GetPlayerEntities(player.Index, "empty_prop");

            if (entities.Count > 0)
            {
                var entity = Utilities.GetEntityFromIndex<CDynamicProp>((int)entities[0]);
                if (entity != null && entity.IsValid)
                    target = entity;
            }

            relay.AcceptInput("SetParent", target, relay, "!activator");

            var particle = EntityManager.CreateTrackedParticleSystem(
                player.Index,
                SkillsInfo.GetValue<string>(skillName, "particleName"));

            if (particle != null && particle.IsValid && target.AbsOrigin != null)
            {
                Vector pos = new(target.AbsOrigin.X, target.AbsOrigin.Y, target.AbsOrigin.Z);
                if (entities.Count > 0) pos.Z -= 30;

                particle.Teleport(pos);
                particle.AcceptInput("SetParent", relay, particle, "!activator");
                particle.AcceptInput("Start");
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            Event.EnableTransmit();
            playersInAction.TryAdd(player.Index, 0);

            mainSkillTimer ??= Instance.AddTimer(2.5f, () => UpdateAllTrails(),
                    CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT | CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

            SkillUtils.ForceFullUpdateToAll();
        }

        private static void UpdateAllTrails()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
                CreatePlayerTrail(player);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            uint playerIndex = player.Index;

            playersInAction.TryRemove(playerIndex, out _);
            EntityManager.DestroyPlayerEntities(playerIndex);
            activeTrails.TryRemove(playerIndex, out _);

            if (playersInAction.IsEmpty && mainSkillTimer != null)
            {
                mainSkillTimer.Kill();
                mainSkillTimer = null;
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#f542ef", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Common, string particleName = "particles/ui/hud/ui_map_def_utility_trail.vpcf") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public string ParticleName { get; set; } = particleName;
        }
    }
}