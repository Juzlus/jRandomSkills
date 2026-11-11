using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Hunter : ISkill
    {
        private const Skills skillName = Skills.Hunter;
        private static readonly Skills[] hidingSkills = [Skills.Ghost, Skills.Ninja, Skills.C4Camouflage];
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            Localization.RegisterDescriptionArgs(skillName, () => [(object)(int)MathF.Round(SkillsInfo.GetValue<float>(skillName, "lowHpPercent") * 100f)]);
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            if (holderBuffer.Count == 0) return;

            Span<uint> tHolders = stackalloc uint[2];
            Span<uint> ctHolders = stackalloc uint[2];
            Span<uint> allHolders = stackalloc uint[2];

            foreach (var playerEvent in holderBuffer)
            {
                if (!Instance.IsPlayerValid(playerEvent)) continue;

                var player = PlayerManager.GetPlayerFromEvent(playerEvent) ?? playerEvent;
                if (player == null || !player.IsValid) continue;

                int slot = player.Slot;
                if (slot is < 0 or > 63) continue;

                uint bit = 1u << (slot & 31);
                allHolders[slot >> 5] |= bit;
                if (player.Team == CsTeam.Terrorist) tHolders[slot >> 5] |= bit;
                else if (player.Team == CsTeam.CounterTerrorist) ctHolders[slot >> 5] |= bit;
            }

            if (allHolders[0] == 0 && allHolders[1] == 0) return;

            bool ffa = SkillUtils.CvarValue("mp_teammates_are_enemies", false);
            float threshold = SkillsInfo.GetValue<float>(skillName, "lowHpPercent");

            foreach (var enemy in PlayerManager.GetTickPlayers())
            {
                if (enemy == null || !enemy.IsValid) continue;

                uint s0, s1;
                if (ffa)
                {
                    s0 = allHolders[0];
                    s1 = allHolders[1];
                    int enemySlot = enemy.Slot;
                    if (enemySlot is >= 0 and <= 63)
                    {
                        if (enemySlot < 32) s0 &= ~(1u << enemySlot);
                        else s1 &= ~(1u << (enemySlot - 32));
                    }
                }
                else if (enemy.Team == CsTeam.Terrorist) { s0 = ctHolders[0]; s1 = ctHolders[1]; }
                else if (enemy.Team == CsTeam.CounterTerrorist) { s0 = tHolders[0]; s1 = tHolders[1]; }
                else continue;

                if (s0 == 0 && s1 == 0) continue;

                if (!enemy.ControllingBot)
                {
                    var quickPawn = enemy.PlayerPawn.Value;
                    if (quickPawn == null || !quickPawn.IsValid || quickPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;
                    int quickMax = quickPawn.MaxHealth > 0 ? quickPawn.MaxHealth : 100;
                    if (quickPawn.Health > quickMax * threshold) continue;
                }

                var enemyEvent = PlayerManager.GetPlayerEvent(enemy);
                if (enemyEvent == null || !enemyEvent.IsValid) continue;

                var enemyPawn = enemyEvent.PlayerPawn.Value;
                if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                int maxHealth = enemyPawn.MaxHealth > 0 ? enemyPawn.MaxHealth : 100;
                if (enemyPawn.Health > maxHealth * threshold) continue;

                var enemyInfo = PlayerManager.GetPlayer(enemyEvent);
                if (enemyInfo != null && Array.IndexOf(hidingSkills, enemyInfo.Skill) >= 0 && enemyPawn.Render.A < 200)
                    continue;

                var mask = enemyPawn.EntitySpottedState.SpottedByMask;
                mask[0] |= s0;
                mask[1] |= s1;
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e53935", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float lowHpPercent = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float LowHpPercent { get; set; } = lowHpPercent;
        }
    }
}
