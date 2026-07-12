using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills
{
    public class ThrowingKnife : ISkill
    {
        private const Skills skillName = Skills.ThrowingKnife;
        private readonly static ConcurrentDictionary<uint, KnifeInfo> knivesInfo = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var knifeInfo in knivesInfo.Values.ToArray())
                DisableKnifeSkill(knifeInfo);
            
            knivesInfo.Clear();
        }

        public static void PlayerMakeSound(UserMessage um)
        {
            var soundevent = um.ReadUInt("soundevent_hash");
            var userIndex = um.ReadUInt("source_entity_index");

            if (soundevent != 3208928088 || userIndex != 0) return;

            byte[] packedParams = um.ReadBytes("packed_params");

            Vector? soundPos = null;
            if (packedParams != null && packedParams.Length >= 19)
            {
                try
                {
                    float x = BitConverter.ToSingle(packedParams, 7);
                    float y = BitConverter.ToSingle(packedParams, 11);
                    float z = BitConverter.ToSingle(packedParams, 15);
                    soundPos = new Vector(x, y, z);
                } catch { }
            }
            if (soundPos == null) return;

            (var knife, var knifeInfo) = GetClosetKnife(soundPos);
            
            if (knife == null || !knife.IsValid || knife.AbsRotation == null)
                return;
            
            if (knifeInfo == null || knifeInfo.InitialLook == null || knife.Collision == null)
                return;
        
            knifeInfo.IsDropped = false;
            QAngle rotation = new(knifeInfo.InitialLook?.X, knifeInfo.InitialLook?.Y, knife.AbsRotation.Z);
            Vector offset = SkillUtils.GetForwardVector(new QAngle(rotation.X - 90, rotation.Y, rotation.Z)) * 2;

            knife.Teleport(
                new Vector(soundPos.X - offset.X, soundPos.Y - offset.Y, soundPos.Z - offset.Z),
                rotation,
                Vector.Zero
            );

            knife.Collision.CollisionAttribute.InteractsWith = knifeInfo.OriginalCollisionMask;
            Utilities.SetStateChanged(knife, "CCollisionProperty", "m_collisionAttribute");
            
            var world = Utilities.GetEntityFromIndex<CBaseEntity>(0);
            if (world != null && world.IsValid)
                knife.AcceptInput("SetParent", world, world, "!activator");
        
            if (knifeInfo.Timer != null)
                knifeInfo.Timer?.Kill();

            knifeInfo.Timer = Instance.AddTimer(3f, () =>
            {
                if (knife == null || !knife.IsValid) return;

                var player = Utilities.GetPlayerFromIndex((int)knifeInfo.PlayerIndex);
                if (player != null && player.IsValid && CheckHasKnife(player)) return;

                knife.AcceptInput("ClearParent");
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        public static bool OnWeaponCanAcquire(DynamicHook hook, CCSPlayerController player, CEconItemView econItem, CCSWeaponBaseVData vdata)
        {
            // vdata can be non-null yet point at freed/invalid schema memory; reading
            // .Name then throws NullReferenceException inside get_Name(). Guard the handle.
            if (vdata == null || vdata.Handle == IntPtr.Zero) return false;

            string weaponName = vdata.Name;
            if (string.IsNullOrEmpty(weaponName)) return false;

            if (!weaponName.Contains("knife") && !weaponName.Contains("bayonet"))
                return false;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return false;

            if (knivesInfo.TryGetValue(player.Index, out KnifeInfo? knifeInfo) && knifeInfo != null && knifeInfo.KnifeId == econItem.ItemID)
            {
                knifeInfo.IsDropped = false;
                if (knifeInfo.Timer != null)
                {
                    knifeInfo.Timer?.Kill();
                    knifeInfo.Timer = null;
                }

                return false;
            }

            hook.SetReturn(AcquireResult.InvalidItem);
            return true;
        }

        public static void EnableSkill(CCSPlayerController _)
        {
            Event.EnableTransmit();
            SkillUtils.ForceFullUpdateToAll();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (knivesInfo.TryRemove(player.Index, out KnifeInfo? knifeInfo) && knifeInfo != null)
                DisableKnifeSkill(knifeInfo);
        }

        public static void DisableKnifeSkill(KnifeInfo knifeInfo)
        {
            if (knifeInfo.Timer != null)
            {
                knifeInfo.Timer?.Kill();
                knifeInfo.Timer = null;
            }

            var trigger = knifeInfo.TriggerIndex;
            var glow = knifeInfo.GlowIndex;
            var relay = knifeInfo.RelayIndex;

            knifeInfo.GlowIndex = null;
            knifeInfo.TriggerIndex = null;
            knifeInfo.RelayIndex = null;

            SkillUtils.SafeKillEntity<CTriggerMultiple>(trigger);
            SkillUtils.SafeKillEntity<CBaseEntity>(glow);
            SkillUtils.SafeKillEntity<CBaseEntity>(relay);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            DropKnife(player);
        }

        private static void DropKnife(CCSPlayerController player)
        {
            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !player.IsValid) return;

            playerEvent.ExecuteClientCommand("slot3");

            Instance.AddTickTimer(8, () =>
            {
                if (player == null || !player.IsValid) return;

                var pawn = player.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

                var weapon = pawn.WeaponServices.ActiveWeapon.Value;
                if (weapon == null || !weapon.IsValid || (!weapon.DesignerName.Contains("knife") && !weapon.DesignerName.Contains("bayonet"))) return;

                player.DropActiveWeapon();
                Server.NextFrame(() => {
                    if (player == null || !player.IsValid) return;
                    if (weapon == null || !weapon.IsValid) return;
                    ThrowKnife(player, weapon);
                });
            });
        }

        private static void ThrowKnife(CCSPlayerController player, CBasePlayerWeapon knife)
        {
            if (player == null || !player.IsValid) return;
            if (knife == null || !knife.IsValid || knife.AbsOrigin == null) return;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            float force = 2000f;

            Vector pos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            QAngle angle = new(0, 0, 0);
            Vector vel = SkillUtils.GetForwardVector(pawn.EyeAngles) * force;

            knife.Teleport(pos, angle, vel);

            if (knife.Collision == null)
                return;

            ulong originalMask = knife.Collision.CollisionAttribute.InteractsWith;
            knife.Collision.CollisionAttribute.InteractsWith = pawn.Collision.CollisionAttribute.InteractsWith;

            if (!knivesInfo.ContainsKey(player.Index))
            {
                uint? triggerIndex = CreateTrigger(player, knife);
                (uint? relayIndex, uint? glowIndex) = CreateGlow(player, knife);

                knivesInfo.TryAdd(player.Index, new KnifeInfo
                {
                    PlayerIndex = player.Index,
                    KnifeIndex = knife.Index,
                    KnifeId = knife.AttributeManager.Item.ItemID,
                    TriggerIndex = triggerIndex,
                    GlowIndex = glowIndex,
                    RelayIndex = relayIndex,
                    OriginalCollisionMask = originalMask
                });
            }

            if (knivesInfo.TryGetValue(player.Index, out KnifeInfo? knifeInfo))
            {
                knifeInfo.IsDropped = true;
                knifeInfo.InitialLook = new QAngle(pawn.V_angle.X + 90, pawn.V_angle.Y, pawn.V_angle.Z);
            }
        }

        private static uint? CreateTrigger(CCSPlayerController player, CBasePlayerWeapon knife)
        {
            if (player == null || !player.IsValid) return null;
            if (knife == null || !knife.IsValid || knife.AbsOrigin == null) return null;
            
            var trigger = SkillUtils.CreateTrigger($"trowingknife", 10, knife.AbsOrigin);
            if (trigger == null || !trigger.IsValid) return null;
            
            trigger.AcceptInput("SetParent", knife, knife, "!activator");
            return trigger.Index;
        }

        private static (uint?, uint?) CreateGlow(CCSPlayerController player, CBaseEntity knife)
        {
            if (player == null || !player.IsValid) return (null, null);
            if (knife == null || !knife.IsValid || knife.AbsOrigin == null) return (null, null);

            var modelRelay = EntityManager.CreateTrackedPhysicsProp(player.Index);
            if (modelRelay == null) return (null, null);

            modelRelay.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(modelRelay.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            modelRelay.SetModel(knife!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
            modelRelay.Spawnflags = 256u;
            modelRelay.Render = Color.FromArgb(1, 255, 255, 255);
            modelRelay.DispatchSpawn();

            modelRelay.Teleport(knife.AbsOrigin, knife.AbsRotation, knife.AbsVelocity);
            modelRelay.AcceptInput("SetParent", knife, modelRelay, "!activator");

            var modelGlow = EntityManager.CreateTrackedPhysicsProp(player.Index);
            if (modelGlow == null) return (null, null);

            modelGlow.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(modelGlow.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));

            modelGlow.SetModel(knife.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
            modelGlow.DispatchSpawn();

            modelGlow.Glow.GlowColorOverride = Color.GreenYellow;
            modelGlow.Glow.GlowRange = 5000;
            modelGlow.Glow.GlowTeam = -1;
            modelGlow.Glow.GlowType = 3;
            modelGlow.Glow.GlowRangeMin = 10;

            modelGlow.Teleport(knife.AbsOrigin, knife.AbsRotation, knife.AbsVelocity);
            modelGlow.AcceptInput("SetParent", modelRelay, modelGlow, "!activator");

            return (modelRelay.Index, modelGlow.Index);
        }

        public static void OnTriggerEnter(CBaseTrigger trigger, CBaseEntity entity)
        {
            if (entity == null || !entity.IsValid || trigger == null || !trigger.IsValid) return;

            string triggerName = trigger.Globalname;
            if (entity.DesignerName != "player" || string.IsNullOrEmpty(triggerName) || !triggerName.StartsWith("trowingknife")) return;

            CCSPlayerPawn victimPawn = entity.As<CCSPlayerPawn>();
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            KnifeInfo? knifeInfo = knivesInfo.Values.FirstOrDefault(ki => ki.TriggerIndex == trigger.Index);

            if (knifeInfo != null)
            {
                var thrower = Utilities.GetPlayerFromIndex((int)knifeInfo.PlayerIndex);
                if (thrower == null || !thrower.IsValid) return;

                var throwerPawn = thrower.Pawn?.Value;
                if (throwerPawn == null || !throwerPawn.IsValid)
                    return;

                if (victimPawn.Index == throwerPawn.Index)
                    return;

                bool friendlyFire = SkillsInfo.GetValue<bool>(skillName, "friendlyFire");
                if (!friendlyFire && thrower.TeamNum == victimPawn.TeamNum) return;

                if (CheckHasKnife(thrower!)) return;

                SkillUtils.TakeHealth(victimPawn, SkillsInfo.GetValue<int>(skillName, "damage"));
            }
        }

        private static bool CheckHasKnife(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return false;

            return pawn.WeaponServices.MyWeapons.Any(w =>
                w != null && w.IsValid && w.Value != null && w.Value.IsValid &&
                (w.Value.DesignerName.Contains("knife") || w.Value.DesignerName.Contains("bayonet")));
        }

        private static (CBaseEntity?, KnifeInfo?) GetClosetKnife(Vector pos)
        {
            double minDistance = double.MaxValue;
            KnifeInfo? minKnifeInfo = null;
            CBaseEntity? closet = null;

            foreach (var knifeInfo in knivesInfo.Values.ToArray())
            {
                if (!knifeInfo.IsDropped) continue;

                var knifeEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)knifeInfo.KnifeIndex);
                if (knifeEntity == null || !knifeEntity.IsValid || knifeEntity.AbsOrigin == null) continue;

                double distance = SkillUtils.GetDistance(knifeEntity.AbsOrigin, pos);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closet = knifeEntity;
                    minKnifeInfo = knifeInfo;
                }
            }

            return (closet, minKnifeInfo);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            var snapshot = knivesInfo.ToArray();

            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;

                uint? observedPlayerIndex = null;
                var playerPawn = player.Pawn?.Value;
                if (playerPawn != null && playerPawn.IsValid)
                {
                    var obs = playerPawn.ObserverServices?.ObserverTarget?.Value;
                    if (obs != null && obs.IsValid)
                    {
                        var observed = Utilities.GetPlayers().FirstOrDefault(p =>
                            p != null && p.IsValid &&
                            p.Pawn?.Value?.Handle == obs.Handle);
                        if (observed != null)
                            observedPlayerIndex = observed.Index;
                    }
                }

                foreach ((uint knifeOwnerIndex, KnifeInfo knifeInfo) in snapshot)
                {
                    if (knifeInfo.GlowIndex == null) continue;

                    var glowEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)knifeInfo.GlowIndex);
                    if (glowEntity == null || !glowEntity.IsValid)
                    {
                        knifeInfo.GlowIndex = null;
                        continue;
                    }

                    bool isOwner = knifeOwnerIndex == (PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index);
                    bool isObservingOwner = observedPlayerIndex.HasValue && observedPlayerIndex.Value == knifeOwnerIndex;

                    if (!isOwner && !isObservingOwner)
                        if (info.TransmitEntities.Contains(glowEntity.Index))
                            info.TransmitEntities.Remove(glowEntity.Index);
                }
            }
        }

        public class KnifeInfo()
        {
            public required uint PlayerIndex { get; set; }
            public required uint KnifeIndex { get; set; }
            public required ulong KnifeId { get; set; }
            public required uint? TriggerIndex { get; set; }
            public required uint? GlowIndex { get; set; }
            public required uint? RelayIndex { get; set; }
            public ulong OriginalCollisionMask { get; set; }
            public bool IsDropped { get; set; } = false;
            public QAngle? InitialLook { get; set; } = null;
            public Timer? Timer { get; set; } = null;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8f108f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = 1, Rarity rarity = Rarity.Common, bool friendlyFire = false, int damage = 9999) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public bool FriendlyFire { get; set; } = friendlyFire;
            public int Damage { get; set; } = damage;
        }
    }
}