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

            Span<uint> tHolders = stackalloc uint[2];
            Span<uint> ctHolders = stackalloc uint[2];

            foreach (var playerEvent in holderBuffer)
            {
                if (!Instance.IsPlayerValid(playerEvent)) continue;

                var player = PlayerManager.GetPlayerFromEvent(playerEvent) ?? playerEvent;
                if (player == null || !player.IsValid) continue;

                // SpottedByMask is indexed by player slot (0-63), not entity index.
                int slot = player.Slot;
                if (slot is < 0 or > 63) continue;

                var holders = player.Team == CsTeam.Terrorist ? tHolders : player.Team == CsTeam.CounterTerrorist ? ctHolders : default;
                if (holders.IsEmpty) continue;
                holders[slot >> 5] |= 1u << (slot & 31);
            }

            uint all0 = tHolders[0] | ctHolders[0], all1 = tHolders[1] | ctHolders[1];
            if (all0 == 0 && all1 == 0) return;

            foreach (var enemy in PlayerManager.GetTickPlayers())
            {
                if (enemy == null || !enemy.IsValid) continue;

                var spotters = enemy.Team == CsTeam.Terrorist ? ctHolders : enemy.Team == CsTeam.CounterTerrorist ? tHolders : default;
                if (spotters.IsEmpty || (spotters[0] == 0 && spotters[1] == 0)) continue;

                var enemyEvent = PlayerManager.GetPlayerEvent(enemy);
                if (enemyEvent == null || !enemyEvent.IsValid) continue;

                var enemyPawn = enemyEvent.PlayerPawn.Value;
                if (enemyPawn == null || !enemyPawn.IsValid) continue;

                var enemyInfo = PlayerManager.GetPlayer(enemyEvent);
                if (enemyInfo != null && Array.IndexOf(hidingSkills, enemyInfo.Skill) >= 0 && enemyPawn.Render.A < 200)
                    continue;

                // Only the holders' slot bits — the Spotted bool would reveal to the whole team.
                var mask = enemyPawn.EntitySpottedState.SpottedByMask;
                mask[0] |= spotters[0];
                mask[1] |= spotters[1];
            }

            var bomb = PlayerManager.GetTickBomb();
            if (bomb != null && bomb.IsValid)
            {
                var bombMask = bomb.EntitySpottedState.SpottedByMask;
                bombMask[0] |= all0;
                bombMask[1] |= all1;
            }
        }

        public class SkillConfig : SkillsInfo.DefaultSkillInfo
        {
            public SkillConfig(Skills skill = skillName, bool active = true, string color = "#2effcb", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = utils.Rarity.Common) : base(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
            {
            }
        }
    }
}