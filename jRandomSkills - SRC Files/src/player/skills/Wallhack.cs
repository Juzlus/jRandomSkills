using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Wallhack : ISkill
    {
        private const Skills skillName = Skills.Wallhack;
        private static readonly ConcurrentDictionary<ulong, byte> playersInAction = new();
        private static ConcurrentDictionary<uint, (uint RelayIndex, uint GlowIndex, CsTeam Team)> glows = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var (info, player) in infoList)
            {
                if (player == null) continue;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);

                var observedPlayer = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == player?.Pawn?.Value?.ObserverServices?.ObserverTarget?.Value?.Handle);
                var observerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == observedPlayer?.SteamID);

                foreach (var kvp in glows)
                {
                    var enemyIndex = kvp.Key;
                    var glowInfo = kvp.Value;

                    var enemy = Utilities.GetPlayers().FirstOrDefault(e => e.IsValid && e.Index == enemyIndex);

                    if (enemy != null && enemy.PawnIsAlive)
                        if (glowInfo.Team != player.Team && (playerInfo?.Skill == skillName || (observerInfo != null && observerInfo?.Skill == skillName)))
                            continue;

                    var glowEntity1 = Utilities.GetEntityFromIndex<CBaseEntity>((int)glowInfo.RelayIndex);
                    if (glowEntity1 == null || !glowEntity1.IsValid) continue;

                    var glowEntity2 = Utilities.GetEntityFromIndex<CBaseEntity>((int)glowInfo.GlowIndex);
                    if (glowEntity2 == null || !glowEntity2.IsValid) continue;

                    info.TransmitEntities.Remove(glowEntity1.Index);
                    info.TransmitEntities.Remove(glowEntity2.Index);
                }
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
            }

            glows.Clear();
            playersInAction.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();
            playersInAction.TryAdd(player.SteamID, 0);
            if (glows.IsEmpty)
                SetGlowEffectForAll();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            playersInAction.TryRemove(player.SteamID, out _);
            if (playersInAction.IsEmpty)
                NewRound();
        }

        private static void SetGlowEffectForAll()
        {
            var enemies = Utilities.GetPlayers().Where(p => p.PawnIsAlive && (p.Team == CsTeam.Terrorist || p.Team == CsTeam.CounterTerrorist)).ToList();
            foreach (var enemy in enemies)
            {
                var enemyInfo = Instance.SkillPlayer.FirstOrDefault(e => e.SteamID == enemy.SteamID);
                if (enemyInfo?.Skill == Skills.Ghost)
                    continue;

                var enemyPawn = enemy.PlayerPawn?.Value;
                if (enemyPawn == null || !enemyPawn.IsValid) continue;

                var skeleton = enemyPawn.CBodyComponent?.SceneNode?.GetSkeletonInstance();
                var modelName = skeleton?.ModelState?.ModelName;
                if (string.IsNullOrEmpty(modelName)) continue;

                var modelGlow = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
                var modelRelay = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");

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