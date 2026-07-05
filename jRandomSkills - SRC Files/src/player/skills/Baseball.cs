using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Baseball : ISkill
    {
        private const Skills skillName = Skills.Baseball;
        private static readonly ConcurrentDictionary<uint, byte> decoys = [];
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var weapon = @event.Weapon;

            if (weapon != "decoy")
                return;

            if (victim == null || !victim.IsValid || victim.PlayerPawn?.Value == null || !victim.PlayerPawn.Value.IsValid || victim.PlayerPawn.Value.Health <= 0)
                return;

            if (attacker == null || !attacker.IsValid || attacker.PlayerPawn?.Value == null || !attacker.PlayerPawn.Value.IsValid || attacker.PlayerPawn.Value.Health <= 0)
                return;

            if (victim.Index == attacker.Index || victim.Team == attacker.Team)
                return;

            var attackerInfo = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(attacker)!.Index);
            if (attackerInfo?.Skill != skillName) return;

            SkillUtils.TakeHealth(victim!.PlayerPawn.Value, SkillsInfo.GetValue<int>(skillName, "damageDeal"));
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            var name = entity.DesignerName;
            if (name != "decoy_projectile")
                return;

            var decoy = entity.As<CDecoyProjectile>();
            if (decoy == null || !decoy.IsValid || decoy.OwnerEntity == null || decoy.OwnerEntity.Value == null || !decoy.OwnerEntity.Value.IsValid) return;
            
            var pawn = decoy.OwnerEntity.Value.As<CCSPlayerPawn>();
            if (pawn == null || !pawn.IsValid || pawn.Controller == null || pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;
            
            var player = pawn.Controller.Value.As<CCSPlayerController>();
            if (player == null || !player.IsValid) return;
            
            var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));
            if (playerInfo?.Skill != skillName) return;
            decoys.TryAdd(decoy.Index, 0);
            
            decoy.Collision.CollisionAttribute.InteractsWith = pawn.Collision.CollisionAttribute.InteractsWith;
            decoy.Collision.CollisionGroup = pawn.Collision.CollisionGroup;
        }

        public static void DecoyStarted(EventDecoyStarted @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;
            
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            uint key = (uint)@event.Entityid;
            if (decoys.ContainsKey(key))
            {
                var decoy = Utilities.GetEntityFromIndex<CDecoyProjectile>(@event.Entityid);
                if (decoy != null && decoy.IsValid)
                    decoy.AddEntityIOEvent("Kill", decoy, delay: 0.1f);
                decoys.TryRemove(key, out _);
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 8 != 0) return;

            var keys = decoys.Keys.ToArray();

            foreach (var decoyIndex in keys)
            {
                var decoy = Utilities.GetEntityFromIndex<CDecoyProjectile>((int)decoyIndex);

                if (decoy == null || !decoy.IsValid)
                {
                    decoys.TryRemove(decoyIndex, out _);
                    continue;
                }

                decoy.Bounces = 0;
                
                var vel = decoy.AbsVelocity;
                float speed = vel.Length();
                float targetSpeed = Math.Min(speed * SkillsInfo.GetValue<float>(skillName, "speedMultipier"), SkillsInfo.GetValue<float>(skillName, "maxSpeed"));

                if (speed > .01f)
                {
                    var dir = vel / speed;
                    var newVelocity = dir * targetSpeed;

                    decoy.AbsVelocity.X = newVelocity.X;
                    decoy.AbsVelocity.Y = newVelocity.Y;
                    decoy.AbsVelocity.Z = newVelocity.Z;
                }
            }
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Weapon;
            if (weapon != "decoy") return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Index] = grenadesLeft - 1;
                player!.GiveNamedItem($"weapon_{weapon}");
                SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadesLeft - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadesLeft);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Item;
            if (string.IsNullOrEmpty(weapon) || weapon != "decoy") return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadesLeft);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            playersWithSkill.TryAdd(player.Index, grenadeLimit);

            SkillUtils.TryGiveWeapon(player, CsItem.DecoyGrenade);
            SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadeLimit);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Index, out _);
            SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#2effc7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float speedMultipier = 2f, float maxSpeed = 900f, int damageDeal = 9999, int grenadeLimit = 3) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float SpeedMultipier { get; set; } = speedMultipier;
            public float MaxSpeed { get; set; } = maxSpeed;
            public float DamageDeal { get; set; } = damageDeal;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}