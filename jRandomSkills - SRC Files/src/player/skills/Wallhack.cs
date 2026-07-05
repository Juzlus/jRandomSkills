using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;

namespace src.player.skills
{
    public class Wallhack : ISkill
    {
        private const Skills skillName = Skills.Wallhack;
        private static readonly ConcurrentDictionary<uint, byte> playersInAction = new();
        private static readonly ConcurrentDictionary<uint, (uint RelayIndex, uint GlowIndex, CsTeam Team)> glows = new();
        private static readonly ConcurrentDictionary<uint, DateTime> temporaryBlockList = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var kvp in temporaryBlockList)
            {
                if (DateTime.Now > kvp.Value)
                    temporaryBlockList.TryRemove(kvp.Key, out _);
            }

            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));
                var observedPlayer = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == player?.Pawn?.Value?.ObserverServices?.ObserverTarget?.Value?.Handle);
                var observerInfo = observedPlayer != null ? PlayerManager.GetPlayerByIndex(observedPlayer.Index) : null;

                foreach (var kvp in glows)
                {
                    var enemyIndex = kvp.Key;
                    var glowInfo = kvp.Value;
                    var enemy = Utilities.GetPlayers().FirstOrDefault(e => e != null && e.IsValid && e.Index == enemyIndex);

                    bool hasSkill = playerInfo?.Skill == skillName;
                    bool observerHasSkill = observerInfo != null && observerInfo?.Skill == skillName;
                    bool differentTeam = glowInfo.Team != player.Team;

                    if (enemy != null && enemy.IsValid)
                    {
                        var enemyPawn = enemy.PlayerPawn?.Value;
                        bool pawnValid = enemyPawn != null && enemyPawn.IsValid;
                        bool alive = pawnValid && enemyPawn!.Health > 0;
                        bool notInvisible = pawnValid && enemyPawn!.Render.A != 102 && enemyPawn!.Render.A != 128;

                        bool shouldShow = alive && notInvisible && differentTeam && (hasSkill || observerHasSkill);

                        if (shouldShow)
                            continue;
                    }

                    var glowEntity1 = Utilities.GetEntityFromIndex<CBaseEntity>((int)glowInfo.RelayIndex);
                    if (glowEntity1 == null || !glowEntity1.IsValid) continue;

                    var glowEntity2 = Utilities.GetEntityFromIndex<CBaseEntity>((int)glowInfo.GlowIndex);
                    if (glowEntity2 == null || !glowEntity2.IsValid) continue;

                    if (info.TransmitEntities.Contains(glowEntity1.Index))
                        info.TransmitEntities.Remove(glowEntity1.Index);

                    if (info.TransmitEntities.Contains(glowEntity2.Index))
                        info.TransmitEntities.Remove(glowEntity2.Index);
                }

                foreach (var kvp in temporaryBlockList)
                    if (info.TransmitEntities.Contains(kvp.Key))
                        info.TransmitEntities.Remove(kvp.Key);
            }
        }

        public static void NewRound()
        {
            foreach (var kvp in glows)
            {
                var relayIndex = kvp.Value.RelayIndex;
                var glowIndex = kvp.Value.GlowIndex;

                SkillUtils.SafeKillEntity<CDynamicProp>(relayIndex);
                SkillUtils.SafeKillEntity<CDynamicProp>(glowIndex);

                temporaryBlockList.TryAdd(relayIndex, DateTime.Now.AddSeconds(2));
                temporaryBlockList.TryAdd(glowIndex, DateTime.Now.AddSeconds(2));
            }

            glows.Clear();
            playersInAction.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();
            playersInAction.TryAdd(player.Index, 0);

            if (glows.IsEmpty)
                SetGlowEffectForAll();

            SkillUtils.ForceFullUpdateToAll();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            playersInAction.TryRemove(player.Index, out _);

            if (playersInAction.IsEmpty)
            {
                NewRound();
                return;
            }

            SkillUtils.ForceFullUpdateToAll();
        }

        private static void SetGlowEffectForAll()
        {
            var enemies = Utilities.GetPlayers().Where(p => 
                p != null &&
                p.IsValid &&
                p.PlayerPawn?.Value != null &&
                p.PlayerPawn.Value.IsValid &&
                p.PlayerPawn.Value.Health > 0 &&
            (p.Team == CsTeam.Terrorist || p.Team == CsTeam.CounterTerrorist)).ToList();
            
            foreach (var enemy in enemies)
            {
                var enemyInfo = PlayerManager.GetPlayerByIndex(enemy.Index);
                if (enemyInfo?.Skill == Skills.Ghost)
                    continue;

                var enemyPawn = enemy.PlayerPawn?.Value;
                if (enemyPawn == null || !enemyPawn.IsValid) continue;

                var skeleton = enemyPawn.CBodyComponent?.SceneNode?.GetSkeletonInstance();
                var modelName = skeleton?.ModelState?.ModelName;
                if (string.IsNullOrEmpty(modelName)) continue;

                var modelGlow = EntityManager.CreateTrackedDynamicProp(enemy.Index);
                var modelRelay = EntityManager.CreateTrackedDynamicProp(enemy.Index);

                if (modelGlow == null || !modelGlow.IsValid || modelRelay == null || !modelRelay.IsValid)
                    continue;

                var relayEntity = modelRelay.CBodyComponent?.SceneNode?.Owner?.Entity;
                if (relayEntity != null)
                    relayEntity.Flags = (uint)(relayEntity.Flags & ~(1 << 2));

                modelRelay.SetModel(modelName);
                modelRelay.Spawnflags = 256u;
                modelRelay.RenderMode = RenderMode_t.kRenderNone;
                modelRelay.DispatchSpawn();

                var glowEntity = modelGlow.CBodyComponent?.SceneNode?.Owner?.Entity;
                if (glowEntity != null)
                    glowEntity.Flags = (uint)(glowEntity.Flags & ~(1 << 2));

                modelGlow.SetModel(modelName);
                modelGlow.Spawnflags = 256u;
                modelGlow.Render = Color.FromArgb(1, 255, 255, 255);
                modelGlow.DispatchSpawn();

                try
                {
                    modelGlow.Glow.GlowColorOverride = enemy.Team == CsTeam.Terrorist ? Color.FromArgb(255, 255, 165, 0) : Color.FromArgb(255, 173, 216, 230);
                    modelGlow.Glow.GlowRange = 5000;
                    modelGlow.Glow.GlowTeam = -1;
                    modelGlow.Glow.GlowType = 3;
                    modelGlow.Glow.GlowRangeMin = 100;
                }
                catch
                { }

                modelRelay.AcceptInput("FollowEntity", enemyPawn, modelRelay, "!activator");
                modelGlow.AcceptInput("FollowEntity", modelRelay, modelGlow, "!activator");

                glows.TryAdd(enemy.Index, (modelRelay.Index, modelGlow.Index, enemy.Team));
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5d00ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = 1, Rarity rarity = Rarity.Epic) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}