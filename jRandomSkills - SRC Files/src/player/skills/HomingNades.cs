using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class HomingNades : ISkill
    {
        private const Skills skillName = Skills.HomingNades;
        private readonly static ConcurrentDictionary<uint, Vector> nades = [];
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            nades.Clear();
        }

        public static void OnTick()
        {
            if (Server.TickCount % 10 != 0) return;
            
            foreach (var index in nades.Keys.ToList())
            {
                if (!nades.TryGetValue(index, out var data)) continue;
                Vector oldPos = data;
                
                var nade = Utilities.GetEntityFromIndex<CBaseCSGrenadeProjectile>((int)index);
                if (nade == null || !nade.IsValid || nade.AbsOrigin == null)
                {
                    nades.TryRemove(index, out _);
                    continue;
                }

                Vector currentPos = new(nade.AbsOrigin.X, nade.AbsOrigin.Y, nade.AbsOrigin.Z);
                double distanceMoved = SkillUtils.GetDistance(currentPos, oldPos);
                Vector? calculatedVelocity = CalculateVelocity(nade, nade.TeamNum);

                bool isZero = calculatedVelocity?.IsZero() == true;

                if (distanceMoved < 4 || calculatedVelocity == null || isZero)
                {
                    nade.DetonateTime = isZero ? 0f : nade.CreateTime + 1.5f;
                    Utilities.SetStateChanged(nade, "CBaseGrenade", "m_flDetonateTime");
   
                    nades.TryRemove(index, out _);
                    continue;
                }

                Vector currentVel = new(nade.Velocity.X, nade.Velocity.Y, nade.Velocity.Z);
                float maxVelocity = SkillsInfo.GetValue<float>(skillName, "maxVelocity");
                Vector newVelocity = currentVel + calculatedVelocity;

                float speed = newVelocity.Length();
                if (speed > maxVelocity)
                    newVelocity *= (maxVelocity / speed);

                nades[index] = currentPos;
                nade.Teleport(null, null, newVelocity);
            }
        }

        private static Vector? CalculateVelocity(CBaseCSGrenadeProjectile nade, int team)
        {
            if (nade.AbsOrigin == null) return null;

            Vector? closetEnemyPos = null;
            double minDistance = int.MaxValue;
            Vector nadePos = nade.AbsOrigin;

            foreach (var enemy in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum != team))
            {
                var pawn = enemy.PlayerPawn.Value;
                if (pawn?.IsValid != true || pawn.AbsOrigin == null) continue;
                
                double dist = SkillUtils.GetDistance(nadePos, pawn.AbsOrigin);
                if (dist < SkillsInfo.GetValue<float>(skillName, "detonationRange"))
                {
                    nades.TryRemove(nade.Index, out _);
                    return Vector.Zero;
                }

                if (dist < minDistance)
                {
                    minDistance = dist;
                    closetEnemyPos = pawn.AbsOrigin;
                }
            }

            if (closetEnemyPos == null)
                return null;

            Vector direction = closetEnemyPos - nadePos;
            float length = direction.Length();

            if (length > 0)
            {
                float strength = SkillsInfo.GetValue<float>(skillName, "strength");
                return new Vector(
                    (direction.X / length) * strength,
                    (direction.Y / length) * strength,
                    (direction.Z / length) * strength
                );
            }

            return Vector.Zero;
        }

        public static void OnEntitySpawned(CEntityInstance @event)
        {
            var name = @event.DesignerName;
            if (!name.EndsWith("_projectile") || name == "smokegrenade_projectile") return;

            var grenade = @event.As<CBaseCSGrenadeProjectile>();
            if (grenade == null || !grenade.IsValid) return;

            if (grenade.OwnerEntity.Value == null || !grenade.OwnerEntity.Value.IsValid) return;
            var pawn = grenade.OwnerEntity.Value.As<CCSPlayerPawn>();

            if (pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;
            var player = pawn.Controller.Value.As<CCSPlayerController>();

            var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));
            if (playerInfo?.Skill != skillName) return;

            Vector pos = new(grenade.AbsOrigin?.X, grenade.AbsOrigin?.Y, grenade.AbsOrigin?.Z);
            nades.TryAdd(grenade.Index, pos);

            Server.NextWorldUpdate(() =>
            {
                if (grenade == null || !grenade.IsValid) return;
                grenade.DetonateTime += 30f;
                Utilities.SetStateChanged(grenade, "CBaseGrenade", "m_flDetonateTime");
            });
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Weapon;
            if (weapon != "hegrenade") return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Index] = grenadesLeft - 1;
                player!.GiveNamedItem($"weapon_{weapon}");
                SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, grenadesLeft - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, grenadesLeft);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Item;
            if (string.IsNullOrEmpty(weapon) || weapon != "hegrenade") return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, grenadesLeft);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            playersWithSkill.TryAdd(player.Index, grenadeLimit);

            SkillUtils.TryGiveWeapon(player, CsItem.HEGrenade);
            SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, grenadeLimit);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Index, out _);
            SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#384728", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float strength = 150, float maxVelocity = 2000, float detonationRange = 130, int grenadeLimit = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Strength { get; set; } = strength;
            public float MaxVelocity { get; set; } = maxVelocity;
            public float DetonationRange { get; set; } = detonationRange;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}