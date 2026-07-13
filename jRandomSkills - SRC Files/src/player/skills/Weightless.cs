using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Weightless : ISkill
    {
        private const Skills skillName = Skills.Weightless;
        private readonly static ConcurrentDictionary<uint, byte> nades = [];
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

            foreach ((var index, _) in nades)
            {
                var nade = Utilities.GetEntityFromIndex<CBaseCSGrenadeProjectile>((int)index);
                if (nade == null || !nade.IsValid)
                {
                    nades.TryRemove(index, out _);
                    continue;
                }

                if (nade.Bounces != 0)
                {
                    nade.ActualGravityScale = 2;
                    
                    nade.DetonateTime = 0;
                    Utilities.SetStateChanged(nade, "CBaseGrenade", "m_flDetonateTime");

                    nades.TryRemove(index, out _);
                }
            }
        }

        public static void OnEntitySpawned(CEntityInstance @event)
        {
            var name = @event.DesignerName;
            if (!name.EndsWith("_projectile")) return;

            var grenade = @event.As<CBaseCSGrenadeProjectile>();
            if (grenade == null || !grenade.IsValid) return;

            if (grenade.OwnerEntity.Value == null || !grenade.OwnerEntity.Value.IsValid) return;
            var pawn = grenade.OwnerEntity.Value.As<CCSPlayerPawn>();

            if (pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;
            var player = pawn.Controller.Value.As<CCSPlayerController>();

            var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));
            if (playerInfo?.Skill != skillName) return;

            Server.NextFrame(() => {
                if (grenade == null || !grenade.IsValid) return;
                grenade.ActualGravityScale = .0001f;
                
                Vector currentVelocity = new(grenade.AbsVelocity.X, grenade.AbsVelocity.Y, grenade.AbsVelocity.Z);
                float speed = currentVelocity.Length() * 2;

                Vector forward = SkillUtils.GetForwardVector(pawn.EyeAngles);
                Vector newVelocity = forward * speed;

                grenade.Teleport(null, null, newVelocity);
                nades.TryAdd(grenade.Index, 0);
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8f6dc9", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int grenadeLimit = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}