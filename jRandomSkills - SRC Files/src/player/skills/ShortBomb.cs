using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class ShortBomb : ISkill
    {
        private const Skills skillName = Skills.ShortBomb;
        // mp_c4timer is an Int32 cvar; captured at load so restore never picks up another skill's override.
        private static int defaultC4Timer = 40;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            defaultC4Timer = ConVar.Find("mp_c4timer")?.GetPrimitiveValue<int>() ?? 40;
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            // At round start (not at plant) so the client HUD/alert countdown is right before the plant completes.
            Server.ExecuteCommand($"mp_c4timer {SkillsInfo.GetValue<int>(skillName, "detonationTime")}");
        }

        public static void NewRound()
        {
            Server.ExecuteCommand($"mp_c4timer {defaultC4Timer}");
        }

        public static void BombPlanted(EventBombPlanted @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            var plantedBomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
            if (plantedBomb != null)
                Server.NextFrame(() => {
                    if (plantedBomb != null && plantedBomb.IsValid)
                        plantedBomb.C4Blow = Server.CurrentTime + SkillsInfo.GetValue<int>(skillName, "detonationTime");
                });

            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
                p.PrintToCenterAlert(p.GetTranslation("bombplanted", SkillsInfo.GetValue<int>(skillName, "detonationTime")));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#f5b74c", CsTeam onlyTeam = CsTeam.Terrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Common, int detonationTime = 20) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int DetonationTime { get; set; } = detonationTime;
        }
    }
}