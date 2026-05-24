using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class BladeMaster : ISkill
    {
        private const Skills skillName = Skills.BladeMaster;
        private static readonly string[] noReflectionWeapon = ["inferno", "flashbang", "smokegrenade", "decoy", "hegrenade", "knife", "taser", "bayonet"];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            var modifier = SkillsInfo.GetValue<float>(skillName, "velocityModifier");

            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill != skillName) continue;

                var playerPawn = player.PlayerPawn?.Value;
                if (playerPawn == null || !playerPawn.IsValid || playerPawn.VelocityModifier == 0) continue;

                var weaponServices = playerPawn.WeaponServices;
                if (weaponServices == null) continue;

                if (weaponServices.ActiveWeapon == null
                    || !weaponServices.ActiveWeapon.IsValid
                    || weaponServices.ActiveWeapon.Value == null
                    || !weaponServices.ActiveWeapon.Value.IsValid
                    || (weaponServices.ActiveWeapon.Value.DesignerName != "weapon_knife"
                        && weaponServices.ActiveWeapon.Value.DesignerName != "weapon_bayonet"))
                    continue;

                playerPawn.VelocityModifier = modifier;
            }
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var damage = @event.DmgHealth;
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Weapon;
            int hitgroup = @event.Hitgroup;

            if (victim == null || !victim.IsValid || attacker == null || !attacker.IsValid || attacker == victim) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(victim.Index);
            if (playerInfo?.Skill != skillName) return;

            if (string.IsNullOrEmpty(weapon) || noReflectionWeapon.Contains(weapon)) return;

            float chance = (hitgroup == (int)HitGroup_t.HITGROUP_LEFTLEG || hitgroup == (int)HitGroup_t.HITGROUP_RIGHTLEG)
                ? SkillsInfo.GetValue<float>(skillName, "legReflectionChance")
                : SkillsInfo.GetValue<float>(skillName, "torseReflectionChance");

            var victimPawn = victim.PlayerPawn?.Value;
            if (victimPawn == null || !victimPawn.IsValid || Instance.Random.NextDouble() > chance)
                return;

            var weaponServices = victimPawn.WeaponServices;
            if (weaponServices == null) return;

            if (weaponServices.ActiveWeapon == null
                || !weaponServices.ActiveWeapon.IsValid
                || weaponServices.ActiveWeapon.Value == null
                || !weaponServices.ActiveWeapon.Value.IsValid
                || (weaponServices.ActiveWeapon.Value.DesignerName != "weapon_knife"
                    && weaponServices.ActiveWeapon.Value.DesignerName != "weapon_bayonet"))
                return;

            SkillUtils.RestoreHealth(victim);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#cc7504", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float torseReflectionChance = .95f, float legReflectionChance = .70f, float velocityModifier = .85f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float TorseReflectionChance { get; set; } = torseReflectionChance;
            public float LegReflectionChance { get; set; } = legReflectionChance;
            public float VelocityModifier { get; set; } = velocityModifier;
        }
    }
}