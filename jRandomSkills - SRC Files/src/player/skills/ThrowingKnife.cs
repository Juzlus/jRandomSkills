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
    public class ThrowingKnife : ISkill
    {
        private const Skills skillName = Skills.ThrowingKnife;
        private readonly static ConcurrentDictionary<uint, uint> triggers = [];
        private readonly static ConcurrentDictionary<uint, uint> glows = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
                DisableSkill(player);
            triggers.Clear();
            glows.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var playersTriggers = triggers.Where(t => t.Value == player.Index).Select(t => t.Key).ToList();
            foreach (var triggerIndex in playersTriggers)
                if (triggers.TryRemove(triggerIndex, out _))
                {
                    var entity = Utilities.GetEntityFromIndex<CTriggerMultiple>((int)triggerIndex);
                    if (entity != null && entity.IsValid)
                        Server.NextFrame(() =>
                        {
                            if (entity.IsValid)
                                entity.AcceptInput("Kill");
                        });
                }

            if (glows.TryRemove(player.Index, out uint glowIndex))
            {
                var entity = Utilities.GetEntityFromIndex<CTriggerMultiple>((int)glowIndex);
                if (entity != null && entity.IsValid)
                    Server.NextFrame(() =>
                    {
                        if (entity.IsValid)
                            entity.AcceptInput("Kill");
                    });
            }
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;
            DropKnife(player);
        }

        private static void DropKnife(CCSPlayerController player)
        {
            player.ExecuteClientCommand("slot3");
            Instance.AddTickTimer(8, () =>
            {
                if (player == null || !player.IsValid) return;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

                var weapon = pawn.WeaponServices.ActiveWeapon.Value;
                if (weapon == null || !weapon.IsValid || (!weapon.DesignerName.Contains("knife") && !weapon.DesignerName.Contains("bayonet"))) return;

                player.DropActiveWeapon();
                Server.NextFrame(() => ThrowKnife(player, weapon) );
            });
        }

        private static void ThrowKnife(CCSPlayerController player, CBasePlayerWeapon knife)
        {
            if (player == null || !player.IsValid) return;
            
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;
            if (knife == null || !knife.IsValid || knife.AbsOrigin == null) return;

            float force = 2000;

            Vector pos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            QAngle angle = new(0, 0, 0);
            Vector vel = SkillUtils.GetForwardVector(pawn.EyeAngles) * force;

            knife.Collision.CollisionAttribute.InteractsWith = pawn.Collision.CollisionAttribute.InteractsWith;

            knife.Teleport(pos, angle, vel);
            CreateTrigger(player, knife);
            CreateGlow(player, knife);
        }

        private static void CreateTrigger(CCSPlayerController player, CBaseEntity knife)
        {
            if (player == null || !player.IsValid) return;
            if (triggers.Any(t => t.Value == player.Index)) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            if (knife == null || !knife.IsValid || knife.AbsOrigin == null) return;

            var trigger = SkillUtils.CreateTrigger($"trowingknife", 10, knife.AbsOrigin);
            if (trigger == null || !trigger.IsValid) return;
            
            trigger.AcceptInput("SetParent", knife, knife, "!activator");
            triggers.TryAdd(trigger.Index, player.Index);
        }

        private static void CreateGlow(CCSPlayerController player, CBaseEntity knife)
        {
            if (player == null || !player.IsValid) return;
            if (glows.ContainsKey(player.Index)) return;
            if (knife == null || !knife.IsValid || knife.AbsOrigin == null) return;

            var modelGlow = Utilities.CreateEntityByName<CPhysicsPropMultiplayer>("prop_physics_multiplayer");
            if (modelGlow == null) return;

            modelGlow.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(modelGlow.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));

            modelGlow.SetModel(knife.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
            modelGlow.DispatchSpawn();

            modelGlow.Glow.GlowColorOverride = Color.GreenYellow;
            modelGlow.Glow.GlowRange = 5000;
            modelGlow.Glow.GlowTeam = -1;
            modelGlow.Glow.GlowType = 3;
            modelGlow.Glow.GlowRangeMin = 10;

            modelGlow.Teleport(knife.AbsOrigin, knife.AbsRotation, knife.AbsVelocity);
            modelGlow.AcceptInput("SetParent", knife, modelGlow, "!activator");

            glows.TryAdd(player.Index, modelGlow.Index);
        }

        public static void OnTriggerEnter(CBaseTrigger trigger, CBaseEntity entity)
        {
            if (entity == null || !entity.IsValid || trigger == null || !trigger.IsValid) return;

            string triggerName = trigger.Globalname;
            if (entity.DesignerName != "player" || string.IsNullOrEmpty(triggerName) || !triggerName.StartsWith("trowingknife")) return;
            
            if (triggers.TryGetValue(trigger.Index, out var throwerIndex))
            {
                var thrower = Utilities.GetPlayerFromIndex((int)throwerIndex);
                if (thrower == null || !thrower.IsValid) return;

                CCSPlayerPawn victimPawn = entity.As<CCSPlayerPawn>();
                if (victimPawn == null || !victimPawn.IsValid || victimPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

                if (thrower != null && thrower.Pawn.Value != null && victimPawn.Index == thrower.Pawn.Index) return;
                if (CheckHasKnife(thrower!)) return;

                SkillUtils.TakeHealth(victimPawn, 99999);
                Server.PrintToChatAll($"HIT");
            }
        }

        private static bool CheckHasKnife(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return false;
            return pawn.WeaponServices.MyWeapons.Any(w => w.IsValid && w != null && w.Value != null && w.Value.IsValid && (w.Value.DesignerName.Contains("knife") || w.Value.DesignerName.Contains("bayonet")));
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;
                var observedPlayer = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == player?.Pawn?.Value?.ObserverServices?.ObserverTarget?.Value?.Handle);

                foreach (var glow in glows)
                {
                    if (glow.Key == player.Index || (observedPlayer != null && observedPlayer.IsValid && glow.Key == observedPlayer.Index)) continue;

                    var glowEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)glow.Value);
                    if (glowEntity == null || !glowEntity.IsValid) continue;
                    info.TransmitEntities.Remove(glowEntity.Index);
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8f108f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}