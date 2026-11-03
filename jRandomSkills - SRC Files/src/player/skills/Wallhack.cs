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

        private static readonly Dictionary<nint, jSkill_PlayerInfo?> infoByPawnHandleBuffer = [];
        private static readonly List<(uint Relay, uint Glow, CsTeam Team, bool Showable, bool EntitiesValid)> glowStatesBuffer = [];

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var kvp in temporaryBlockList)
            {
                if (DateTime.Now > kvp.Value)
                    temporaryBlockList.TryRemove(kvp.Key, out _);
            }

            if (glows.IsEmpty && temporaryBlockList.IsEmpty) return;

            // One pawn->info map per frame instead of a GetPlayers scan per client.
            var infoByPawnHandle = infoByPawnHandleBuffer;
            infoByPawnHandle.Clear();
            foreach (var p in PlayerManager.GetTickPlayers())
            {
                var h = p?.Pawn?.Value?.Handle;
                if (p != null && p.IsValid && h != null)
                    infoByPawnHandle[h.Value] = PlayerManager.GetPlayer(p);
            }

            var glowStates = glowStatesBuffer;
            glowStates.Clear();
            foreach (var kvp in glows)
            {
                var glowInfo = kvp.Value;
                var enemy = PlayerManager.GetController(kvp.Key);

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

                var playerInfo = PlayerManager.GetPlayer(PlayerManager.GetPlayerEvent(player) ?? player);
                var targetHandle = player.Pawn.Value?.ObserverServices?.ObserverTarget?.Value?.Handle ?? nint.Zero;
                jSkill_PlayerInfo? observerInfo = null;
                if (targetHandle != nint.Zero)
                    infoByPawnHandle.TryGetValue(targetHandle, out observerInfo);

                bool seesGlows = playerInfo?.Skill == skillName || (observerInfo != null && observerInfo?.Skill == skillName);

                foreach (var glow in glowStates)
                {
                    if (glow.Showable && seesGlows && glow.Team != player.Team) continue;
                    if (!glow.EntitiesValid) continue;

                    SkillUtils.HideTransmitDormant(info, glow.Relay);

                    SkillUtils.HideTransmitDormant(info, glow.Glow);
                }

                foreach (var kvp in temporaryBlockList)
                    SkillUtils.HideTransmitDormant(info, kvp.Key);
            }
        }

        public static void NewRound()
        {
            buildInProgress = false;
            buildGeneration++;

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
            RemoveGlow(victim.Key());
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
            playersInAction.TryAdd(player.Key(), 0);

            if (buildInProgress || !glows.IsEmpty) return;

            SetGlowEffectForAll();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            playersInAction.TryRemove(player.Key(), out _);

            if (playersInAction.IsEmpty)
            {
                NewRound();
                return;
            }
        }

        private const int FreezeMaxInterval = 8;
        private static bool buildInProgress;
        private static int buildGeneration;

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
            SpawnGlowStep(enemies, 0, ++buildGeneration);
        }

        private static void SpawnGlowStep(List<CCSPlayerController> enemies, int next, int generation)
        {
            if (!buildInProgress || generation != buildGeneration || playersInAction.IsEmpty) return;

            if (next >= enemies.Count)
            {
                buildInProgress = false;
                return;
            }

            var enemy = enemies[next];
            if (enemy != null && enemy.IsValid && !glows.ContainsKey(enemy.Key()))
            {
                var pawn = enemy.PlayerPawn?.Value;
                if (pawn != null && pawn.IsValid && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                    SpawnGlowFor(enemy);
            }

            int interval = StepInterval(enemies.Count - next - 1);
            if (interval <= 1)
                Server.NextFrame(() => SpawnGlowStep(enemies, next + 1, generation));
            else
                SafeTimer.AddTick(interval, () => SpawnGlowStep(enemies, next + 1, generation), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        private static int StepInterval(int remaining)
        {
            if (remaining <= 0 || !SkillUtils.IsFreezeTime()) return 1;

            double freezeTicks = (Event.GetFreezeTimeEnd() - DateTime.Now).TotalSeconds / Server.TickInterval;
            return Math.Clamp((int)(freezeTicks * .75 / remaining), 1, FreezeMaxInterval);
        }

        private static void SpawnGlowFor(CCSPlayerController enemy)
        {
            var enemyInfo = PlayerManager.GetPlayer(enemy);
            if (enemyInfo?.Skill == Skills.Ghost)
                return;

            var enemyPawn = enemy.PlayerPawn?.Value;
            if (enemyPawn == null || !enemyPawn.IsValid) return;

            var skeleton = enemyPawn.CBodyComponent?.SceneNode?.GetSkeletonInstance();
            var modelName = skeleton?.ModelState?.ModelName;
            if (string.IsNullOrEmpty(modelName)) return;

            var modelGlow = EntityManager.CreateTrackedDynamicProp(enemy.Key());
            var modelRelay = EntityManager.CreateTrackedDynamicProp(enemy.Key());

            if (modelGlow == null || !modelGlow.IsValid || modelRelay == null || !modelRelay.IsValid)
                return;

            glows.TryAdd(enemy.Key(), (modelRelay.Index, modelGlow.Index, enemy.Team));

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