using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class FragileBomb : ISkill
    {
        private const Skills skillName = Skills.FragileBomb;
        private static int bombHealth = 1000;
        private static int maxBombHealth = 1000;

        private static int lastTick = 0;
        private static Vector? plantedC4;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            bombHealth = SkillsInfo.GetValue<int>(skillName, "maxBombHealth");
            maxBombHealth = SkillsInfo.GetValue<int>(skillName, "maxBombHealth");
            plantedC4 = null;
        }

        public static void BombPlanted(EventBombPlanted _)
        {
            var plantedBomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
            if (plantedBomb == null || !plantedBomb.IsValid || plantedBomb.AbsOrigin == null) return;
            plantedC4 = new(plantedBomb.AbsOrigin.X, plantedBomb.AbsOrigin.Y, plantedBomb.AbsOrigin.Z);
        }

        private static void RemoveBomb()
        {
            plantedC4 = null;
            var plantedBomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
            if (plantedBomb != null && plantedBomb.IsValid)
                plantedBomb.AddEntityIOEvent("Kill", plantedBomb, delay: 0.1f);
            SkillUtils.TerminateRound(CsTeam.CounterTerrorist);
        }

        public static void BulletImpact(EventBulletImpact @event)
        {
            if (lastTick == Server.TickCount) return;

            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid || plantedC4 == null) return;

            var pos = new Vector(@event.X, @event.Y, @event.Z);

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null || playerInfo.Skill != skillName) return;

            if (SkillUtils.Distance(plantedC4, pos) >= 8)
                return;

            lastTick = Server.TickCount;
            bombHealth -= Instance.Random.Next(25, 42);

            if (bombHealth <= 0)
            {
                RemoveBomb();
                return;
            }

            Localization.PrintTranslationToChatAll($" {ChatColors.Gold}{{0}}: {ChatColors.Red}{bombHealth}{ChatColors.Gold}/{ChatColors.Green}{maxBombHealth}", ["fragilebomb_bomb_health"]);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5d00ff", CsTeam onlyTeam = CsTeam.CounterTerrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Common, int maxBombHealth = 1000) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int MaxBombHealth { get; set; } = maxBombHealth;
        }
    }
}