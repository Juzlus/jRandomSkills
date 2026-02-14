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
        private static readonly ConcurrentDictionary<ulong, byte> playersInAction = [];
        private static readonly ConcurrentBag<(CDynamicProp, CDynamicProp, CsTeam, uint)> glows = [];

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

                foreach (var glow in glows)
                {
                    var enemy = Utilities.GetPlayers().FirstOrDefault(e => e.IsValid && e.Index == glow.Item4);

                    if (enemy != null && enemy.PawnIsAlive)
                        if (glow.Item3 != player.Team && (playerInfo?.Skill == skillName || (observerInfo != null && observerInfo?.Skill == skillName)))
                            continue;

                    var glowEntity1 = Utilities.GetEntityFromIndex<CBaseEntity>((int)glow.Item1.Index);
                    if (glowEntity1 == null || !glowEntity1.IsValid) continue;

                    var glowEntity2 = Utilities.GetEntityFromIndex<CBaseEntity>((int)glow.Item2.Index);
                    if (glowEntity2 == null || !glowEntity2.IsValid) continue;

                    info.TransmitEntities.Remove(glowEntity1.Index);
                    info.TransmitEntities.Remove(glowEntity2.Index);
                }
            }
        }

        public static void NewRound()
        {
            foreach (var glow in glows)
            {
                if (glow.Item1 != null && glow.Item1.IsValid)
                    glow.Item1.AcceptInput("Kill");
                if (glow.Item2 != null && glow.Item2.IsValid)
                    glow.Item2.AcceptInput("Kill");
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
            foreach (var enemy in Utilities.GetPlayers().FindAll(p => p.PawnIsAlive && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist))
            {
                var enemyInfo = Instance.SkillPlayer.FirstOrDefault(e => e.SteamID == enemy.SteamID);
                if (enemyInfo?.Skill == Skills.Ghost)
                    return;

                var enemyPawn = enemy.PlayerPawn.Value;
                var modelGlow = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
                var modelRelay = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");

                if (modelGlow == null || modelRelay == null)
                    return;

                modelRelay.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(modelRelay.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
                modelRelay.SetModel(enemyPawn!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
                modelRelay.Spawnflags = 256u;
                modelRelay.RenderMode = RenderMode_t.kRenderNone;
                modelRelay.DispatchSpawn();

                modelGlow.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(modelGlow.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
                modelGlow.SetModel(enemyPawn!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
                modelGlow.Spawnflags = 256u;
                modelGlow.Render = Color.FromArgb(1, 255, 255, 255);
                modelGlow.DispatchSpawn();

                modelGlow.Glow.GlowColorOverride = enemy.Team == CsTeam.Terrorist ? Color.FromArgb(255, 255, 165, 0) : Color.FromArgb(255, 173, 216, 230);
                modelGlow.Glow.GlowRange = 5000;
                modelGlow.Glow.GlowTeam = -1;
                modelGlow.Glow.GlowType = 3;
                modelGlow.Glow.GlowRangeMin = 100;

                modelRelay.AcceptInput("FollowEntity", enemyPawn, modelRelay, "!activator");
                modelGlow.AcceptInput("FollowEntity", modelRelay, modelGlow, "!activator");
                glows.Add((modelRelay, modelGlow, enemy.Team, enemy.Index));
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5d00ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}