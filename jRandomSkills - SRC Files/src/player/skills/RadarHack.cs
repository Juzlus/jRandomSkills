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
        private static readonly Skills[] hidingSkills = [Skills.Ghost, Skills.Ninja, Skills.C4Camouflage];
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            if (holderBuffer.Count == 0) return;

            foreach (var playerEvent in holderBuffer)
            {
                if (!Instance.IsPlayerValid(playerEvent)) continue;

                var player = PlayerManager.GetPlayerFromEvent(playerEvent) ?? playerEvent;
                SetEnemiesVisibleOnRadar(player);
            }
        }

        private static void SetEnemiesVisibleOnRadar(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.PlayerPawn?.Value == null) return;

            // SpottedByMask is indexed by player slot (0-63), not entity index.
            int slot = player.Slot;

            foreach (var enemy in PlayerManager.GetTickPlayers())
            {
                if (enemy == null || enemy.Team == player.Team) continue;

                var enemyEvent = PlayerManager.GetPlayerEvent(enemy);
                if (enemyEvent == null || !enemyEvent.IsValid) continue;

                var enemyPawn = enemyEvent.PlayerPawn.Value;
                if (enemyPawn == null || !enemyPawn.IsValid) continue;

                var enemyInfo = PlayerManager.GetPlayerByIndex(enemyEvent.Index);
                if (enemyInfo != null && Array.IndexOf(hidingSkills, enemyInfo.Skill) >= 0 && enemyPawn.Render.A < 200)
                    continue;

                // Only the observer's slot bit — the Spotted bool would reveal to the whole team.
                enemyPawn.EntitySpottedState.SpottedByMask[0] |= (1u << (slot % 32));
            }

            var bomb = PlayerManager.GetTickBomb();
            if (bomb != null && bomb.IsValid)
                bomb.EntitySpottedState.SpottedByMask[0] |= (1u << (slot % 32));
        }

        public class SkillConfig : SkillsInfo.DefaultSkillInfo
        {
            public SkillConfig(Skills skill = skillName, bool active = true, string color = "#2effcb", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = utils.Rarity.Common) : base(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
            {
            }
        }
    }
}