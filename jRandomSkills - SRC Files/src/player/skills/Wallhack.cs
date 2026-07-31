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

            if (glows.IsEmpty && temporaryBlockList.IsEmpty) return;

            // One pawn->info map per frame instead of a GetPlayers scan per client.
            var infoByPawnHandle = new Dictionary<nint, jSkill_PlayerInfo?>();
            foreach (var p in PlayerManager.GetTickPlayers())
            {
                var h = p?.Pawn?.Value?.Handle;
                if (p != null && p.IsValid && h != null)
                    infoByPawnHandle[h.Value] = PlayerManager.GetPlayerByIndex(p.Index);
            }

            var glowStates = new List<(uint Relay, uint Glow, CsTeam Team, bool Showable, bool EntitiesValid)>(glows.Count);
            foreach (var kvp in glows)
            {
                var glowInfo = kvp.Value;
                var enemy = Utilities.GetPlayerFromIndex((int)kvp.Key);

                bool showable = false;
                if (enemy != null && enemy.IsValid)
                {
                    var enemyPawn = enemy.PlayerPawn?.Value;
                    bool pawnValid = enemyPawn != null && enemyPawn.IsValid;
                    showable = pawnValid && enemyPawn!.Health > 0 && enemyPawn!.Render.A != 102 && enemyPawn!.Render.A != 128;
                }

                var relayEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)glowInfo.RelayIndex);
                var glowEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)glowInfo.GlowIndex);
                bool entitiesValid = relayEntity != null && relayEntity.IsValid && glowEntity != null && glowEntity.IsValid;

                glowStates.Add((glowInfo.RelayIndex, glowInfo.GlowIndex, glowInfo.Team, showable, entitiesValid));
            }

            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));
                var targetHandle = player.Pawn.Value?.ObserverServices?.ObserverTarget?.Value?.Handle ?? nint.Zero;
                jSkill_PlayerInfo? observerInfo = null;
                if (targetHandle != nint.Zero)
                    infoByPawnHandle.TryGetValue(targetHandle, out observerInfo);

                bool seesGlows = playerInfo?.Skill == skillName || (observerInfo != null && observerInfo?.Skill == skillName);

                foreach (var glow in glowStates)
                {
                    if (glow.Showable && seesGlows && glow.Team != player.Team) continue;
                    if (!glow.EntitiesValid) continue;

                    if (info.TransmitEntities.Contains(glow.Relay))
                        info.TransmitEntities.Remove(glow.Relay);

                    if (info.TransmitEntities.Contains(glow.Glow))
                        info.TransmitEntities.Remove(glow.Glow);
                }

                foreach (var kvp in temporaryBlockList)
                    if (info.TransmitEntities.Contains(kvp.Key))
                        info.TransmitEntities.Remove(kvp.Key);
            }
        }

        public static void NewRound()
        {
            buildInProgress = false;

            foreach (var kvp in glows)
            {
                var relayIndex = kvp.Value.RelayIndex;
                var glowIndex = kvp.Value.GlowIndex;

                // The glow follows the relay, so it dies first.
                SkillUtils.SafeKillEntity<CDynamicProp>(glowIndex);
                SkillUtils.SafeKillEntity<CDynamicProp>(relayIndex);

                temporaryBlockList.TryAdd(relayIndex, DateTime.Now.AddSeconds(2));
                temporaryBlockList.TryAdd(glowIndex, DateTime.Now.AddSeconds(2));
            }

            glows.Clear();
            playersInAction.Clear();
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = @event.Userid;
            if (victim == null || !victim.IsValid) return;
            RemoveGlow(victim.Index);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            RemoveGlow(playerIndex);
        }

        private static void RemoveGlow(uint enemyIndex)
        {
            if (!glows.TryRemove(enemyIndex, out var glowInfo)) return;

            SkillUtils.SafeKillEntity<CDynamicProp>(glowInfo.GlowIndex);
            SkillUtils.SafeKillEntity<CDynamicProp>(glowInfo.RelayIndex);

            temporaryBlockList.TryAdd(glowInfo.RelayIndex, DateTime.Now.AddSeconds(2));
            temporaryBlockList.TryAdd(glowInfo.GlowIndex, DateTime.Now.AddSeconds(2));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();
            playersInAction.TryAdd(player.Index, 0);

            if (glows.IsEmpty && !buildInProgress)
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
        }

        private const int GlowsPerFrame = 4;
        private static bool buildInProgress;

        private static void SetGlowEffectForAll()
        {
            var enemies = PlayerManager.GetTickPlayers().Where(p =>
                p != null &&
                p.IsValid &&
                p.PlayerPawn?.Value != null &&
                p.PlayerPawn.Value.IsValid &&
                p.PlayerPawn.Value.Health > 0 &&
            (p.Team == CsTeam.Terrorist || p.Team == CsTeam.CounterTerrorist)).ToList();

            buildInProgress = true;
            SpawnGlowChunk(enemies, 0);
        }

        private static void SpawnGlowChunk(List<CCSPlayerController> enemies, int start)
        {
            if (!buildInProgress) return;

            if (start >= enemies.Count)
            {
                buildInProgress = false;
                return;
            }

            int end = Math.Min(start + GlowsPerFrame, enemies.Count);
            for (int i = start; i < end; i++)
            {
                var enemy = enemies[i];
                if (enemy == null || !enemy.IsValid) continue;
                SpawnGlowFor(enemy);
            }

            Server.NextFrame(() => SpawnGlowChunk(enemies, end));
        }

        private static void SpawnGlowFor(CCSPlayerController enemy)
        {
            var enemyInfo = PlayerManager.GetPlayerByIndex(enemy.Index);
            if (enemyInfo?.Skill == Skills.Ghost)
                return;

            var enemyPawn = enemy.PlayerPawn?.Value;
            if (enemyPawn == null || !enemyPawn.IsValid) return;

            var skeleton = enemyPawn.CBodyComponent?.SceneNode?.GetSkeletonInstance();
            var modelName = skeleton?.ModelState?.ModelName;
            if (string.IsNullOrEmpty(modelName)) return;

            var modelGlow = EntityManager.CreateTrackedDynamicProp(enemy.Index);
            var modelRelay = EntityManager.CreateTrackedDynamicProp(enemy.Index);

            if (modelGlow == null || !modelGlow.IsValid || modelRelay == null || !modelRelay.IsValid)
                return;

            glows.TryAdd(enemy.Index, (modelRelay.Index, modelGlow.Index, enemy.Team));

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
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5d00ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Epic) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}