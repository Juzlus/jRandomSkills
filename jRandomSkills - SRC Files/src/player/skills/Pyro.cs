using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Pyro : ISkill
    {
        private const Skills skillName = Skills.Pyro;
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            int damage = @event.DmgHealth;
            string weapon = @event.Weapon;

            if (weapon != "inferno" || !Instance.IsPlayerValid(victim)) return;
            var victimInfo = PlayerManager.GetPlayerByIndex(victim!.Index);

            if (victimInfo == null || victimInfo.Skill != skillName) return;

            var pawn = victim!.PlayerPawn.Value;
            SkillUtils.AddHealth(pawn, (int)(damage * SkillsInfo.GetValue<float>(skillName, "regenerationMultiplier")));
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Weapon;
            if (weapon != "molotov" && weapon != "incgrenade") return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Index] = grenadesLeft - 1;
                player!.GiveNamedItem($"weapon_{weapon}");
                SkillUtils.UpdateGrenadeCount(player, CsItem.Molotov, grenadesLeft - 1);
                SkillUtils.UpdateGrenadeCount(player, CsItem.IncendiaryGrenade, grenadesLeft - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
            {
                SkillUtils.UpdateGrenadeCount(player, CsItem.Molotov, grenadesLeft);
                SkillUtils.UpdateGrenadeCount(player, CsItem.IncendiaryGrenade, grenadesLeft);
            }
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

            var item = player.Team == CsTeam.CounterTerrorist ? CsItem.IncendiaryGrenade : CsItem.Molotov;

            SkillUtils.TryGiveWeapon(player, item);
            SkillUtils.UpdateGrenadeCount(player, item, grenadeLimit);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Index, out _);
            SkillUtils.UpdateGrenadeCount(player, CsItem.Molotov, 1);
            SkillUtils.UpdateGrenadeCount(player, CsItem.IncendiaryGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#3c47de", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float regenerationMultiplier = 1.5f, int grenadeLimit = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float RegenerationMultiplier { get; set; } = regenerationMultiplier;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}