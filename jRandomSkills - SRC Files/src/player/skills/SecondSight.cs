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
    // Whoever shoots you is outlined through walls for a few seconds, for your eyes only.
    public class SecondSight : ISkill
    {
        private const Skills skillName = Skills.SecondSight;
        private static readonly ConcurrentDictionary<uint, byte> holders = [];
        // (holder, enemy) -> glow pair and expiry
        private static readonly ConcurrentDictionary<(uint Holder, uint Enemy), (uint Relay, uint Glow, DateTime Until)> glows = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var key in glows.Keys.ToArray()) RemoveGlow(key);
            holders.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            Event.EnableTransmit();
            holders[player.Index] = 0;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            holders.TryRemove(player.Index, out _);
            foreach (var key in glows.Keys.Where(k => k.Holder == player.Index).ToArray()) RemoveGlow(key);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            holders.TryRemove(playerIndex, out _);
            foreach (var key in glows.Keys.Where(k => k.Holder == playerIndex || k.Enemy == playerIndex).ToArray()) RemoveGlow(key);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;
            foreach (var key in glows.Keys.Where(k => k.Enemy == victim.Index).ToArray()) RemoveGlow(key);
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (holders.IsEmpty || damagedEntity == null || damageInfo == null || damageInfo.Handle == nint.Zero) return;
            if (damagedEntity.DesignerName != "player") return;

            var victimPawn = new CCSPlayerPawn(damagedEntity.Handle);
            var victim = PlayerManager.GetPlayerEvent(victimPawn.Controller?.Value?.As<CCSPlayerController>());
            if (victim == null || !holders.ContainsKey(victim.Index)) return;

            var attackerEnt = damageInfo.Attacker?.Value;
            if (attackerEnt == null || !attackerEnt.IsValid || attackerEnt.DesignerName != "player") return;
            var attackerPawn = new CCSPlayerPawn(attackerEnt.Handle);
            var attacker = PlayerManager.GetPlayerEvent(attackerPawn.Controller?.Value?.As<CCSPlayerController>());
            if (attacker == null || !attacker.IsValid || attacker.Index == victim.Index || attacker.Team == victim.Team) return;

            float seconds = SkillsInfo.GetValue<float>(skillName, "seconds");
            var key = (victim.Index, attacker.Index);
            if (glows.TryGetValue(key, out var existing))
            {
                glows[key] = (existing.Relay, existing.Glow, DateTime.Now.AddSeconds(seconds));
                return;
            }

            SpawnGlow(key, attacker, attackerPawn, seconds);
            victim.PrintToChat($" {ChatColors.Lime}{victim.GetTranslation("secondsight_info", attacker.PlayerName, (int)seconds)}");
        }

        private static void SpawnGlow((uint Holder, uint Enemy) key, CCSPlayerController enemy, CCSPlayerPawn enemyPawn, float seconds)
        {
            var modelName = enemyPawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState?.ModelName;
            if (string.IsNullOrEmpty(modelName)) return;

            var modelGlow = EntityManager.CreateTrackedDynamicProp(key.Holder);
            var modelRelay = EntityManager.CreateTrackedDynamicProp(key.Holder);
            if (modelGlow == null || !modelGlow.IsValid || modelRelay == null || !modelRelay.IsValid) return;

            var relayEntity = modelRelay.CBodyComponent?.SceneNode?.Owner?.Entity;
            if (relayEntity != null) relayEntity.Flags = (uint)(relayEntity.Flags & ~(1 << 2));
            modelRelay.SetModel(modelName);
            modelRelay.Spawnflags = 256u;
            modelRelay.RenderMode = RenderMode_t.kRenderNone;
            modelRelay.DispatchSpawn();

            var glowEntity = modelGlow.CBodyComponent?.SceneNode?.Owner?.Entity;
            if (glowEntity != null) glowEntity.Flags = (uint)(glowEntity.Flags & ~(1 << 2));
            modelGlow.SetModel(modelName);
            modelGlow.Spawnflags = 256u;
            modelGlow.Render = Color.FromArgb(1, 255, 255, 255);
            modelGlow.DispatchSpawn();

            try
            {
                modelGlow.Glow.GlowColorOverride = Color.FromArgb(255, 255, 60, 60);
                modelGlow.Glow.GlowRange = 5000;
                modelGlow.Glow.GlowTeam = -1;
                modelGlow.Glow.GlowType = 3;
                modelGlow.Glow.GlowRangeMin = 50;
            }
            catch { }

            modelRelay.AcceptInput("FollowEntity", enemyPawn, modelRelay, "!activator");
            modelGlow.AcceptInput("FollowEntity", modelRelay, modelGlow, "!activator");

            glows[key] = (modelRelay.Index, modelGlow.Index, DateTime.Now.AddSeconds(seconds));
        }

        private static void RemoveGlow((uint Holder, uint Enemy) key)
        {
            if (!glows.TryRemove(key, out var g)) return;
            SkillUtils.SafeKillEntity<CDynamicProp>(g.Glow);
            SkillUtils.SafeKillEntity<CDynamicProp>(g.Relay);
        }

        public static void OnTick()
        {
            if (glows.IsEmpty || Server.TickCount % 16 != 0) return;
            foreach (var (key, g) in glows)
            {
                var enemy = Utilities.GetPlayerFromIndex((int)key.Enemy);
                if (DateTime.Now > g.Until || enemy == null || !enemy.IsValid || !enemy.PawnIsAlive)
                    RemoveGlow(key);
            }
        }

        // Only the holder who was shot sees the outline.
        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            if (glows.IsEmpty) return;
            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;
                foreach (var (key, g) in glows)
                {
                    if (key.Holder == player.Index) continue;
                    if (info.TransmitEntities.Contains((int)g.Relay)) info.TransmitEntities.Remove((int)g.Relay);
                    if (info.TransmitEntities.Contains((int)g.Glow)) info.TransmitEntities.Remove((int)g.Glow);
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff5e5e", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, float seconds = 5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Seconds { get; set; } = seconds;
        }
    }
}
