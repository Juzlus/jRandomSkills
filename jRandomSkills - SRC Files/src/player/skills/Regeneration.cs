using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Regeneration : ISkill
    {
        private const Skills skillName = Skills.Regeneration;
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            int cooldown = Math.Max(1, (int)(64 * SkillsInfo.GetValue<float>(skillName, "cooldown")));
            if (Server.TickCount % cooldown != 0) return;
            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            if (holderBuffer.Count == 0) return;

            foreach (var player in holderBuffer)
            {
                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;
                SkillUtils.AddHealth(pawn, SkillsInfo.GetValue<int>(skillName, "healthToAdd"));
            }
        }

        public class SkillConfig : SkillsInfo.DefaultSkillInfo
        {
            public int HealthToAdd { get; set; }
            public float Cooldown { get; set; }
            public SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff462e", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = utils.Rarity.Common, int healthToAdd = 1, float cooldown = .25f) : base(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
            {
                HealthToAdd = healthToAdd;
                Cooldown = cooldown;
            }
        }
    }
}