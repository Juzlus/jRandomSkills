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

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            var snapshot = activeTrails.ToArray();

            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(player)!.Index);
                bool isJackalOwner = playerInfo?.Skill == skillName;

                if (!isJackalOwner)
                {
                    var targetHandle = player.Pawn.Value?.ObserverServices?.ObserverTarget.Value?.Handle ?? nint.Zero;
                    if (targetHandle != nint.Zero)
                    {
                        var observed = Utilities.GetPlayers()
                            .FirstOrDefault(p => p?.Pawn?.Value?.Handle == targetHandle);
                        if (observed != null && observed.IsValid)
                        {
                            var observedInfo = PlayerManager.GetPlayerByIndex(observed.Index);
                            if (observedInfo?.Skill == skillName)
                                isJackalOwner = true;
                        }
                    }
                }

                foreach (var (trackedPlayerIndex, relayIndex) in snapshot)
                {
                    if (relayIndex == null) continue;

                    var trailOwner = Utilities.GetPlayerFromIndex((int)trackedPlayerIndex);
                    if (trailOwner == null || !trailOwner.IsValid) continue;

                    var relayEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)relayIndex);
                    if (relayEntity == null || !relayEntity.IsValid) continue;

                    if (player.Team == trailOwner.Team || !isJackalOwner)
                        info.TransmitEntities.Remove(relayEntity.Index);
                }
            }
        }

        public static void CreatePlayerTrail(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null
                || playerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || playerPawn.Health <= 0) return; ;

            var relay = EntityManager.CreateTrackedPhysicsProp(player.Index);
            if (relay == null || !relay.IsValid) return;

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

            activeTrails[player.Index] = relay.Index;
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            Event.EnableTransmit();
            playersInAction.TryAdd(player.Index, 0);

            var opponents = Utilities.GetPlayers()
                .Where(p => p != null
                    && p.IsValid
                    && p.Team != player.Team
                    && p.PawnIsAlive
                    && (p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist))
                .ToArray();

            mainSkillTimer ??= Instance.AddTimer(2.5f, () => UpdateAllTrails(),
                    CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT | CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        private static void UpdateAllTrails()
        {
            foreach (var player in Utilities.GetPlayers())
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#f542ef", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = 1, Rarity rarity = Rarity.Common, string particleName = "particles/ui/hud/ui_map_def_utility_trail.vpcf") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public string ParticleName { get; set; } = particleName;
        }
    }
}