using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class HolyHandGrenade : ISkill
    {
        private const Skills skillName = Skills.HolyHandGrenade;
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnEntitySpawned(CEntityInstance @event)
        {
            var name = @event.DesignerName;
            if (!name.EndsWith("hegrenade_projectile"))
                return;

            Server.NextFrame(() =>
            {
                if (@event == null || !@event.IsValid) return;
                var hegrenade = @event.As<CHEGrenadeProjectile>();
                if (hegrenade == null || !hegrenade.IsValid) return;

                var playerPawn = hegrenade.Thrower.Value;
                if (playerPawn == null || !playerPawn.IsValid) return;

                var player = Utilities.GetPlayers().FirstOrDefault(p => p.PlayerPawn.Index == playerPawn.Index);
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill != skillName) return;

                hegrenade.Damage *= SkillsInfo.GetValue<float>(skillName, "damageMultiplier");
                hegrenade.DmgRadius *= SkillsInfo.GetValue<float>(skillName, "damageRadiusMultiplier");
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffdd00", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float damageMultiplier = 2f, float damageRadiusMultiplier = 2f, int grenadeLimit = 1) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float DamageMultiplier { get; set; } = damageMultiplier;
            public float DamageRadiusMultiplier { get; set; } = damageRadiusMultiplier;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}