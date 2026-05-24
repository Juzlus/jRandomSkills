using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class RadarHack : ISkill
    {
        private const Skills skillName = Skills.RadarHack;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                var playerEvent = PlayerManager.GetPlayerEvent(player);
                if (!Instance.IsPlayerValid(playerEvent)) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(playerEvent!.Index);
                if (playerInfo?.Skill == skillName)
                    SetEnemiesVisibleOnRadar(player);
            }
        }
        
        private static void SetEnemiesVisibleOnRadar(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.PlayerPawn?.Value == null) return;
            int playerIndex = (int)player.Index - 1;

            foreach (var enemy in Utilities.GetPlayers().FindAll(p => p.Team != player.Team))
            {
                var enemyEvent = PlayerManager.GetPlayerEvent(enemy);
                if (enemyEvent == null || !enemyEvent.IsValid) continue;

                var enemyPawn = enemyEvent.PlayerPawn.Value;
                if (enemyPawn == null) continue;

                enemyPawn.EntitySpottedState.SpottedByMask[0] |= (1u << (int)(playerIndex % 32));
            }
            
            var bombEntities = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").ToList();
            if (bombEntities.Count != 0)
            {
                var bomb = bombEntities.FirstOrDefault();
                if (bomb != null && bomb.IsValid)
                    bomb.EntitySpottedState.SpottedByMask[0] |= (1u << (int)(playerIndex % 32));
            }
        }

        public class SkillConfig : SkillsInfo.DefaultSkillInfo
        {
            public SkillConfig(Skills skill = skillName, bool active = true, string color = "#2effcb", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = utils.Rarity.Common) : base(skill, active, color, onlyTeam, needsTeammates)
            {
            }
        }
    }
}