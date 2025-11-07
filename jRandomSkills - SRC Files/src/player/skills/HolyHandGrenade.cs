using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class HolyHandGrenade : ISkill
    {
        private const Skills skillName = Skills.HolyHandGrenade;

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
                var hegrenade = @event.As<CHEGrenadeProjectile>();
                if (hegrenade == null || !hegrenade.IsValid) return;

                var playerPawn = hegrenade.Thrower.Value;
                if (playerPawn == null || !playerPawn.IsValid) return;

                var player = Utilities.GetPlayers().FirstOrDefault(p => p.PlayerPawn.Index == playerPawn.Index);
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
                if (playerInfo?.Skill != skillName) return;

                hegrenade.Damage *= SkillsInfo.GetValue<float>(skillName, "damageMultiplier");
                hegrenade.DmgRadius *= SkillsInfo.GetValue<float>(skillName, "damageRadiusMultiplier");
            });
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.HEGrenade);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffdd00", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float damageMultiplier = 2f, float damageRadiusMultiplier = 2f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float DamageMultiplier { get; set; } = damageMultiplier;
            public float DamageRadiusMultiplier { get; set; } = damageRadiusMultiplier;
        }
    }
}