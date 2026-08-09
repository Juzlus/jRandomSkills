using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class DemonEye : ISkill
    {
        private const Skills skillName = Skills.DemonEye;
        private static readonly Skills[] hidingSkills = [Skills.Ghost, Skills.Ninja, Skills.C4Camouflage];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            int tickCooldown = (int)(64 * SkillsInfo.GetValue<float>(skillName, "SecondCooldown"));
            if (Server.TickCount % tickCooldown != 0) return;

            int damage = SkillsInfo.GetValue<int>(skillName, "damage");

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                var playerEvent = PlayerManager.GetPlayerEvent(player);
                if (!Instance.IsPlayerValid(playerEvent)) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(playerEvent!.Index);
                if (playerInfo?.Skill == skillName)
                    HitEnemies(playerEvent, damage);
            }
        }

        private static void HitEnemies(CCSPlayerController player, int damage)
        {
            if (player == null || !player.IsValid || player.PlayerPawn?.Value == null) return;
            
            int playerSlot = player.Slot;
            int playerBit = playerSlot % 32;

            if (playerSlot < 0)
                return;

            foreach (var enemy in PlayerManager.GetTickPlayers().FindAll(p => p != null && p.IsValid && p.Team != player.Team))
            {
                var enemyEvent = PlayerManager.GetPlayerEvent(enemy);
                if (enemyEvent == null || !enemyEvent.IsValid) continue;

                var enemyPawn = enemyEvent.PlayerPawn?.Value;
                if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.Health <= 0) continue;

                int enemySlot = enemy.Slot;
                if (enemySlot < 0)
                    return;

                int enemyBit = enemySlot % 32;
                uint playerMask = 1u << playerBit;
                uint enemyMask = 1u << enemyBit;

                bool playerSeesEnemy = (enemyPawn.EntitySpottedState.SpottedByMask[0] & playerMask) != 0;

                if (playerSeesEnemy)
                {
                    SkillUtils.TakeHealth(enemyPawn, damage, player, KillfeedIcons.Fist);
                    SkillUtils.EmitSoundToPlayer(enemyEvent, "Player.DamageBody.Victim", SkillsInfo.GetValue<float>(skillName, "soundVolume"));
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#c91243", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float secondCooldown = 1f, int damage = 5, float soundVolume = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float SoundVolume { get; set; } = soundVolume;
            public float SecondCooldown { get; set; } = secondCooldown;
            public int Damage { get; set; } = damage;
        }
    }
}