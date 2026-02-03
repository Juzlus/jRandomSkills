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
        private static readonly string[] noReflectionWeapon = ["inferno", "flashbang", "smokegrenade", "decoy", "hegrenade", "knife", "taser"];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!Instance.IsPlayerValid(player)) continue;

                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill != skillName) continue;

                var playerPawn = player.PlayerPawn?.Value;
                if (playerPawn == null || !playerPawn.IsValid || playerPawn.VelocityModifier == 0) continue;

                var weaponServices = playerPawn.WeaponServices;
                if (weaponServices == null) return;
                if (weaponServices.ActiveWeapon == null || !weaponServices.ActiveWeapon.IsValid || weaponServices.ActiveWeapon.Value == null || !weaponServices.ActiveWeapon.Value.IsValid || weaponServices.ActiveWeapon.Value.DesignerName != "weapon_knife")
                    return;

                playerPawn.VelocityModifier = SkillsInfo.GetValue<float>(skillName, "velocityModifier");
            }
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = @event.Userid;
            int damage = @event.DmgHealth;
            HitGroup_t hitGroup = (HitGroup_t)@event.Hitgroup;
            string weapon = @event.Weapon;

            if (noReflectionWeapon.Contains(weapon) || !Instance.IsPlayerValid(victim)) return;
            var victimInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == victim?.SteamID);
            if (victimInfo == null || victimInfo.Skill != skillName) return;

            int chance = Instance.Random.Next(0, 101);
            if (hitGroup == HitGroup_t.HITGROUP_LEFTLEG || hitGroup == HitGroup_t.HITGROUP_RIGHTLEG)
            {
                if (chance > SkillsInfo.GetValue<float>(skillName, "legReflectionChance") * 100)
                    return;
            }
            else
                if (chance > SkillsInfo.GetValue<float>(skillName, "torseReflectionChance") * 100)
                    return;

            var pawn = victim!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null) return;
            if (weaponServices.ActiveWeapon == null || !weaponServices.ActiveWeapon.IsValid || weaponServices.ActiveWeapon.Value == null || !weaponServices.ActiveWeapon.Value.IsValid || weaponServices.ActiveWeapon.Value.DesignerName != "weapon_knife")
                return;

            SkillUtils.RestoreHealth(victim);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#cc7504", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float torseReflectionChance = .95f, float legReflectionChance = .80f, float velocityModifier = .85f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float TorseReflectionChance { get; set; } = torseReflectionChance;
            public float LegReflectionChance { get; set; } = legReflectionChance;
            public float VelocityModifier { get; set; } = velocityModifier;
        }
    }
}