using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Phoenix : ISkill
    {
        private const Skills skillName = Skills.Phoenix;
        private static readonly ConcurrentDictionary<uint, int> phoenixTicks = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var user = @event.Userid;
            if (user == null || !user.IsValid) return;

            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            var pawn = victim.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            bool isProtected = phoenixTicks.TryGetValue(victim.Index, out int tick);
            if (isProtected && tick + 4 > Server.TickCount)
            {
                SkillUtils.AddHealth(pawn, 100 - pawn.Health);
                return;
            }

            var playerInfo = PlayerManager.GetPlayerByIndex(victim.Index);
            if (playerInfo?.Skill != skillName || pawn.Health > 0) return;

            if (Instance.Random.NextDouble() > playerInfo.SkillChance) return;
            if (victim.TeamChanged) return;

            phoenixTicks[victim.Index] = Server.TickCount;

            SkillUtils.AddHealth(pawn, 100 - pawn.Health);
            SkillUtils.PrintToChat(user, user.GetTranslation("phoenix_respawn"));

            Server.NextFrame(() =>
            {
                if (victim == null || !victim.IsValid) return;
                if (pawn == null || !pawn.IsValid) return;

                var spawnpoint = SkillUtils.GetSpawnPointVector(victim);
                if (spawnpoint == null) return;

                pawn.Teleport(spawnpoint);
            });
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            float newChance = (float)Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "ChanceTo") - SkillsInfo.GetValue<float>(skillName, "ChanceFrom")) + SkillsInfo.GetValue<float>(skillName, "ChanceFrom");
            playerInfo.SkillChance = newChance;
            
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, newChance)}",
                border: !Utilities.GetPlayers().Any(p => p.Team == player.Team && p != player) ? "tb" : "t");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff5C0A", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float chanceFrom = .2f, float chanceTo = .4f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
        }
    }
}