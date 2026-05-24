using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class Iana : ISkill
    {
        private const Skills skillName = Skills.Iana;
        private static readonly ConcurrentDictionary<uint, PlayerSkill> playersInfo = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var playerSkill in playersInfo.Values)
                KillClone(playerSkill);
            
            lock (setLock)
                playersInfo.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null) return;

            if (playersInfo.TryGetValue(player.Index, out var playerSkill))
                if (playerSkill.CloneProp != null)
                    BlockWeapon(player, true);
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null) return;

            if (playersInfo.TryGetValue(player.Index, out var playerSkill))
                if (playerSkill.CloneProp != null)
                    BlockWeapon(player, true);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (playersInfo.TryGetValue(player.Index, out var playerSkill))
                if (playerSkill.CloneProp == null && playerSkill.NextUse <= Server.TickCount)
                {
                    UpdateWeapons(player, playerSkill);
                    CreateClone(playerSkill);
                    SkillUtils.SetPlayerCollisions(player, false);
                }
                else if (playerSkill.CloneProp != null)
                    KillClone(playerSkill);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.CameraServices == null) return;
            playersInfo.TryAdd(player.Index, new PlayerSkill
            {
                PlayerIndex = player.Index,
                CloneProp = null,
                NextUse = 0,
                UseTime = 0,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            if (playersInfo.TryGetValue(player.Index, out var playerSkill))
                KillClone(playerSkill);
            playersInfo.TryRemove(player.Index, out _);
            EntityManager.DestroyPlayerEntities(player.Index);
        }

        private static void UpdateWeapons(CCSPlayerController player, PlayerSkill playerSkill)
        {
            if (player == null || !player.IsValid) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.WeaponServices == null) return;

            playerSkill.Weapons.Clear();

            foreach (var weapon in playerPawn.WeaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
                    playerSkill.Weapons.Add(weapon.Value.AttributeManager.Item.ItemID);
        }

        private static void KillClone(PlayerSkill playerSkill)
        {
            var player = Utilities.GetPlayerFromIndex((int)playerSkill.PlayerIndex);
            if (player == null || !player.IsValid) return;

            player.EmitSound("SolidMetal.BulletImpact");
            
            CDynamicProp? cloneProp = null;
            if (playerSkill.CloneProp != null)
                cloneProp = Utilities.GetEntityFromIndex<CDynamicProp>((int)playerSkill.CloneProp);
            
            if (cloneProp != null && cloneProp.IsValid && cloneProp.AbsOrigin != null && cloneProp.AbsRotation != null)
            {
                var playerPawn = player.PlayerPawn.Value;
                if (playerPawn != null && playerPawn.IsValid)
                {
                    Vector pos = new(cloneProp.AbsOrigin.X, cloneProp.AbsOrigin.Y, cloneProp.AbsOrigin.Z);
                    Server.NextFrame(() =>
                    {
                        if (playerPawn == null || !playerPawn.IsValid) return;
                        playerPawn.Teleport(pos);
                    });
                }
                EntityManager.DestroyEntity(cloneProp.Index);
                playerSkill.CloneProp = null;
            }

            var eventTarget = PlayerManager.GetPlayerFromEvent(player);
            if (eventTarget != null && eventTarget.IsValid)
                SkillUtils.ApplyScreenColor(eventTarget, 0, 0, 0, 0, 10, 0, 2);

            SkillUtils.SetPlayerCollisions(player, true);

            BlockWeapon(player, false);
            playerSkill.NextUse = Server.TickCount + SkillsInfo.GetValue<float>(skillName, "Cooldown") * 64;
            playerSkill.UseTime = 0;
        }

        public static void OnTick()
        {
            foreach (var playerSkill in playersInfo.Values)
                UpdateHUD(playerSkill);
        }

        private static CDynamicProp? CreateClone(PlayerSkill playerSkill)
        {
            var player = Utilities.GetPlayerFromIndex((int)playerSkill.PlayerIndex);
            if (player == null || !player.IsValid) return null;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null) return null;

            if (!((PlayerFlags)playerPawn.Flags).HasFlag(PlayerFlags.FL_ONGROUND))
            {
                playerSkill.InfoTime = Server.TickCount;
                return null;
            }

            QAngle angle = new(0, playerPawn.EyeAngles.Y, 0);
            Vector pos = playerPawn.AbsOrigin + SkillUtils.GetForwardVector(angle) * 50;
            pos.Z += 2;

            if (!CheckPosition(player, pos))
            {
                pos.Z += 10;
                if (!CheckPosition(player, pos))
                {
                    playerSkill.InfoTime = Server.TickCount;
                    return null;
                }
            }

            var clone = EntityManager.CreateTrackedDynamicProp(player.Index);
            if (clone == null || !clone.IsValid) return null;

            clone.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
            clone.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(clone.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            clone.Entity!.Name = clone.Globalname = $"IanaClone_{Server.TickCount}_{player.Index}";
            clone.DispatchSpawn();

            Server.NextFrame(() =>
            {
                if (player == null || !player.IsValid) return;
                if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null) return;

                if (clone == null || !clone.IsValid) return;

                clone.SetModel(playerPawn!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
                clone.Teleport(playerPawn.AbsOrigin, new QAngle(0, playerPawn.V_angle.Y, 0));

                playerPawn.Teleport(pos);

                var eventTarget = PlayerManager.GetPlayerFromEvent(player);
                if (eventTarget != null && eventTarget.IsValid)
                    SkillUtils.ApplyScreenColor(eventTarget, r: 255, g: 255, b: 0, a: 20, duration: 100, holdTime: 1020);

                BlockWeapon(player, true);

                playerSkill.UseTime = Server.TickCount;
                playerSkill.NextUse = Server.TickCount + 64000;
                playerSkill.CloneProp = clone.Index;

                jRandomSkills.Instance.AddTimer(SkillsInfo.GetValue<float>(skillName, "Duration"), () => 
                {
                    if (playerSkill.CloneProp != null)
                        KillClone(playerSkill);
                }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);

                uint playerIndex = player.Index;

                Timer? cloneTimer = null;
                cloneTimer = jRandomSkills.Instance.AddTimer(2f, () =>
                {
                    var target = Utilities.GetPlayerFromIndex((int)playerIndex);
                    if (target == null || !target.IsValid || target.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                    {
                        cloneTimer?.Kill();
                        return;
                    }

                    if (!playersInfo.TryGetValue(target.Index, out var playerSkill) || playerSkill.CloneProp == null)
                    {
                        cloneTimer?.Kill();
                        return;
                    }

                    var eventTarget = PlayerManager.GetPlayerFromEvent(player);
                    if (eventTarget == null || !eventTarget.IsValid)
                    {
                        cloneTimer?.Kill();
                        return;
                    }

                    SkillUtils.ApplyScreenColor(eventTarget, r: 255, g: 255, b: 0, a: 20, duration: 100, holdTime: 1020);
                }, TimerFlags.STOP_ON_MAPCHANGE | TimerFlags.REPEAT);
            });

            return clone;
        }

        private unsafe static bool CheckPosition(CCSPlayerController player, Vector endPos)
        {
            var playerPawn = player.PlayerPawn.Value;

            Vector start = new(playerPawn!.AbsOrigin!.X, playerPawn!.AbsOrigin!.Y, endPos.Z);
            var result = RayTrace.TraceHullShape(start, endPos, player);

            if (!result.HasValue)
                return false;
            
            return !result.Value.DidHit;
        }

        private static float CalculateDamage(CCSPlayerController player, string weaponName, float damage, bool isHeadshot)
        {
            float calculatedDamage = damage;

            var weapon = Utilities.FindAllEntitiesByDesignerName<CCSWeaponBase>(weaponName).FirstOrDefault();
            if (weapon == null) return calculatedDamage;

            var vdata = weapon.GetVData<CCSWeaponBaseVData>();
            if (vdata == null) return calculatedDamage;

            float penetration = vdata.ArmorRatio * .5f;

            var eventPlayer = PlayerManager.GetPlayerFromEvent(player);
            if (eventPlayer == null || !eventPlayer.IsValid)
                return calculatedDamage;

            if (isHeadshot)
            {
                calculatedDamage *= vdata.HeadshotMultiplier;
                if (eventPlayer.PawnHasHelmet)
                    calculatedDamage *= penetration;
            }
            else
                if (eventPlayer.PawnArmor > 0)
                    calculatedDamage *= penetration;

            return calculatedDamage;
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null)
                return;

            if (string.IsNullOrEmpty(param.Entity.Name)) return;
            if (!param.Entity.Name.StartsWith("IanaClone_")) return;

            var nameParams = param.Entity.Name.Split('_')[2];
            _ = uint.TryParse(nameParams, out uint userIndex);
            if (userIndex == 0) return;

            var player = Utilities.GetPlayerFromIndex((int)userIndex);
            if (player == null || !player.IsValid) return;

            float dealDamage = param2.Damage;

            if (playersInfo.TryGetValue(player.Index, out var playerSkill))
            {
                CDynamicProp? cloneProp = null;
                if (playerSkill.CloneProp != null)
                    cloneProp = Utilities.GetEntityFromIndex<CDynamicProp>((int)playerSkill.CloneProp);

                if (cloneProp != null && cloneProp.IsValid && cloneProp.AbsOrigin != null)
                {
                    Vector pos = param2.DamagePosition;
                    Vector posCl = cloneProp.AbsOrigin;
                    
                    bool isHead = false;
                    float viewOffset = 63.27f;
                    float diff = 2f;

                    if (pos.Z >= posCl.Z + viewOffset - diff)
                        isHead = true;

                    float damage = param2.Damage;
                    string? weapon = param2.Ability?.Value?.DesignerName;
                    if (weapon != null)
                        dealDamage = CalculateDamage(player, weapon, damage, isHead);
                }

                KillClone(playerSkill);

                var playerPawn = player.PlayerPawn.Value;
                if (playerPawn != null)
                    SkillUtils.TakeHealth(playerPawn, (int)dealDamage);
            }
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            if (playersInfo.TryGetValue(victim.Index, out var playerSkill) && playerSkill.CloneProp != null)
            {
                KillClone(playerSkill);
                SkillUtils.RestoreHealth(victim);
            }
        }

        private static void BlockWeapon(CCSPlayerController player, bool block)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            foreach (var weapon in pawn.WeaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
                {
                    weapon.Value.NextPrimaryAttackTick = block ? int.MaxValue : Server.TickCount;
                    weapon.Value.NextSecondaryAttackTick = block ? int.MaxValue : Server.TickCount;

                    Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                    Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
                }
        }

        private static void UpdateHUD(PlayerSkill playerSkill)
        {
            if (playerSkill == null) return;

            var player = Utilities.GetPlayerFromIndex((int)playerSkill.PlayerIndex);
            if (player == null || !player.IsValid) return;

            float cooldown = 0;
            float time1 = (playerSkill.NextUse - Server.TickCount) / 64;
            cooldown = (int)Math.Ceiling(Math.Max(time1, 0));

            float duration = 0;
            float time2 = SkillsInfo.GetValue<float>(skillName, "Duration") - (Server.TickCount - playerSkill.UseTime) / 64;
            duration = (int)Math.Ceiling(Math.Max(time2, 0));

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            if (playerSkill.InfoTime + (64 * 2) > Server.TickCount)
                playerInfo.PrintHTML = $"{player.GetTranslation("shade_nospace")}";
            else if (cooldown == 0 && duration == 0)
                playerInfo.PrintHTML = null;
            else if (duration > 0)
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#00FF00'>{duration}</font>")}";
            else if (cooldown > 0)
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public static bool OnWeaponCanAcquire(DynamicHook hook, CCSPlayerController player, CEconItemView econItem, CCSWeaponBaseVData vdata)
        {
            string weaponName = vdata.Name;
            if (string.IsNullOrEmpty(weaponName)) return false;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return false;

            if (playersInfo.TryGetValue(player.Index, out var playerSkill))
                if (playerSkill.CloneProp == null || playerSkill.Weapons.Contains(econItem.ItemID))
                    return false;

            hook.SetReturn(AcquireResult.InvalidItem);
            return true;
        }

        public static bool WeaponDrop(DynamicHook hook, CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return false;

            if (playersInfo.TryGetValue(player.Index, out var playerSkill))
                if (playerSkill.CloneProp != null)
                {
                    hook.SetReturn(1);
                    return true;
                }

            return false;
        }

        public class PlayerSkill
        {
            public required uint PlayerIndex { get; set; }
            public uint? CloneProp { get; set; }
            public float NextUse { get; set; }
            public float UseTime { get; set; }
            public int InfoTime { get; set; }
            public List<ulong> Weapons { get; set; } = [];
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d0d930", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float cooldown = 30, float duration = 10) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float Duration { get; set; } = duration;
        }
    }
}