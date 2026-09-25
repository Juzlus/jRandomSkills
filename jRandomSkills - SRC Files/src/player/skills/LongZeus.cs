using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class LongZeus : ISkill
    {
        private const Skills skillName = Skills.LongZeus;
        private const string shockParticle = "particles/blood_impact/impact_taser_bodyfx.vpcf";

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(shockParticle);
        }

        public unsafe static void WeaponFire(EventWeaponFire @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            var pawn = player!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.WeaponServices == null) return;

            var activeWeapon = pawn.WeaponServices.ActiveWeapon.Value;
            if (activeWeapon == null || !activeWeapon.IsValid || activeWeapon.DesignerName != "weapon_taser") return;

            var result = RayTrace.EyeTrace(player);
            if (result == null || !result.HasValue)
                return;

            if (!result.Value.HitPlayer(out CCSPlayerController? target) || target == null)
                return;

            if (target.Handle == player.Handle) return;
            if (!SkillsInfo.GetValue<bool>(skillName, "friendlyFire") && player.Team == target.Team) return;

            var targetPawn = target.PlayerPawn.Value;
            if (targetPawn == null || !targetPawn.IsValid) return;

            EntityManager.CreateEntityParticle(player.Index, shockParticle, targetPawn);
            SkillUtils.RegisterNativeKill(targetPawn, pawn, activeWeapon, DamageTypes_t.DMG_SHOCK);
            SkillUtils.TakeHealth(targetPawn, 9999);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.Zeus);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#6effc7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon, float maxDistance = 4096f, bool friendlyFire = false) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float MaxDistance { get; set; } = maxDistance;
            public bool FriendlyFire { get; set; } = friendlyFire;
        }
    }
}