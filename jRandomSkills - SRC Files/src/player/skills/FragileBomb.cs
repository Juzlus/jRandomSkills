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
            plantedC4 = FindPlantedC4();
        }

        private static Vector? FindPlantedC4()
        {
            var plantedBomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
            if (plantedBomb == null || !plantedBomb.IsValid || plantedBomb.AbsOrigin == null) return null;
            if (!plantedBomb.BombTicking || plantedBomb.BombDefused) return null;
            return new(plantedBomb.AbsOrigin.X, plantedBomb.AbsOrigin.Y, plantedBomb.AbsOrigin.Z);
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
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null || playerInfo.Skill != skillName) return;

            plantedC4 ??= FindPlantedC4();
            if (plantedC4 == null) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            var eyePos = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            var impactPos = new Vector(@event.X, @event.Y, @event.Z);

            if (DistanceToSegment(plantedC4, eyePos, impactPos) >= 8)
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

        private static float DistanceToSegment(Vector point, Vector start, Vector end)
        {
            float lineX = end.X - start.X, lineY = end.Y - start.Y, lineZ = end.Z - start.Z;
            float lengthSquared = lineX * lineX + lineY * lineY + lineZ * lineZ;
            float t = lengthSquared <= .0001f ? 0f : Math.Clamp(((point.X - start.X) * lineX + (point.Y - start.Y) * lineY + (point.Z - start.Z) * lineZ) / lengthSquared, 0f, 1f);

            float dx = start.X + lineX * t - point.X, dy = start.Y + lineY * t - point.Y, dz = start.Z + lineZ * t - point.Z;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5d00ff", CsTeam onlyTeam = CsTeam.CounterTerrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Common, int maxBombHealth = 1000) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int MaxBombHealth { get; set; } = maxBombHealth;
        }
    }
}