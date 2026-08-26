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

        private const string infernoDamage = "inferno";

        private static bool IsInfernoDamage(CTakeDamageInfo damageInfo)
        {
            var ability = damageInfo.Ability?.Value;
            if (ability != null && ability.IsValid && ability.DesignerName == infernoDamage) return true;

            var inflictor = damageInfo.Inflictor?.Value;
            return inflictor != null && inflictor.IsValid && inflictor.DesignerName == infernoDamage;
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || !damagedEntity.IsValid || damageInfo == null) return;
            if (damageInfo.Damage <= 0 || !IsInfernoDamage(damageInfo)) return;

            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);
            if (!victimPawn.IsValid || victimPawn.DesignerName != "player") return;

            var victimController = victimPawn.Controller?.Value;
            if (victimController == null || !victimController.IsValid) return;

            var victim = PlayerManager.GetPlayerEvent(victimController.As<CCSPlayerController>());
            if (victim == null || !victim.IsValid) return;

            if (PlayerManager.GetPlayerByIndex(victim.Index)?.Skill != skillName) return;

            float damage = damageInfo.Damage;
            float net = damage * (SkillsInfo.GetValue<float>(skillName, "regenerationMultiplier") - 1f);

            if (net < 0)
            {
                damageInfo.Damage = -net;
                return;
            }

            damageInfo.Damage = 0;

            int heal = (int)MathF.Round(net);
            if (heal > 0) SkillUtils.AddHealth(victimPawn, heal);
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