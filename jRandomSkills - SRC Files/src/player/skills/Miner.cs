using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Miner : ISkill
    {
        private const Skills skillName = Skills.Miner;
        private readonly static ConcurrentDictionary<uint, byte> nades = [];
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            nades.Clear();
            playersWithSkill.Clear();
        }

        public static void OnTick()
        {
            if (Server.TickCount % 10 != 0) return;
            float detonationRange = SkillsInfo.GetValue<float>(skillName, "detonationRange");
            float currentTime = Server.CurrentTime;

            foreach (var index in nades.Keys.ToList())
            {
                var nade = Utilities.GetEntityFromIndex<CBaseCSGrenadeProjectile>((int)index);
                if (nade == null || !nade.IsValid || nade.AbsOrigin == null)
                {
                    nades.TryRemove(index, out _);
                    continue;
                }

                if (nade.CreateTime + 3 > currentTime) return;
                Vector currentPos = new(nade.AbsOrigin.X, nade.AbsOrigin.Y, nade.AbsOrigin.Z);

                foreach (var enemy in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum != nade.TeamNum))
                {
                    var enemyPawn = enemy.PlayerPawn.Value;
                    if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.AbsOrigin == null) continue;

                    Vector enemyPos = new(enemyPawn.AbsOrigin.X, enemyPawn.AbsOrigin.Y, enemyPawn.AbsOrigin.Z);
                    double distance = SkillUtils.GetDistance(currentPos, enemyPos);

                    if (distance <= detonationRange)
                    {
                        Detonate(nade);
                        nades.TryRemove(index, out _);
                        break;
                    }
                }
            }
        }

        private static void Detonate(CBaseCSGrenadeProjectile grenade)
        {
            if (grenade == null || !grenade.IsValid || grenade.AbsOrigin == null) return;

            Vector position = grenade.AbsOrigin;
            position.Z += 60;
            grenade.Teleport(position);

            grenade.EmitSound("IncGrenade.Bounce_M");

            grenade.DetonateTime = Server.CurrentTime + .5f;
            Utilities.SetStateChanged(grenade, "CBaseGrenade", "m_flDetonateTime");
        }

        public static void OnEntitySpawned(CEntityInstance @event)
        {
            var name = @event.DesignerName;
            if (name != "hegrenade_projectile") return;

            var grenade = @event.As<CBaseCSGrenadeProjectile>();
            if (grenade == null || !grenade.IsValid) return;

            if (grenade.OwnerEntity.Value == null || !grenade.OwnerEntity.Value.IsValid) return;
            var pawn = grenade.OwnerEntity.Value.As<CCSPlayerPawn>();

            if (pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;
            var player = pawn.Controller.Value.As<CCSPlayerController>();

            var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));
            if (playerInfo?.Skill != skillName) return;

            nades.TryAdd(grenade.Index, 0);

            Server.NextWorldUpdate(() =>
            {
                if (grenade == null || !grenade.IsValid) return;
                grenade.DetonateTime = float.MaxValue;
                Utilities.SetStateChanged(grenade, "CBaseGrenade", "m_flDetonateTime");
            });
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            playersWithSkill.TryAdd(player.Index, grenadeLimit);

            SkillUtils.TryGiveWeapon(player, CsItem.HEGrenade);
            SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, grenadeLimit);
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

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Index, out _);
            SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#adf542", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float detonationRange = 130, int grenadeLimit = 3) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float DetonationRange { get; set; } = detonationRange;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}