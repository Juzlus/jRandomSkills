using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class ClutchMaster : ISkill
    {
        private const Skills skillName = Skills.ClutchMaster;
        private static readonly ConcurrentDictionary<uint, byte> clutching = [];
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            clutching.Clear();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            clutching.TryRemove(player.Index, out _);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            // Count once the death has gone through.
            Server.NextFrame(CheckForClutch);
        }

        private static void CheckForClutch()
        {
            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            if (holderBuffer.Count == 0) return;

            var alive = Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist).ToList();

            foreach (var player in holderBuffer)
            {
                if (!Instance.IsPlayerValid(player) || clutching.ContainsKey(player.Index)) continue;
                if (alive.Count(p => p.Team == player.Team) != 1) continue;
                if (!alive.Any(p => p.Team != player.Team)) continue;

                clutching.TryAdd(player.Index, 0);

                var pawn = player.PlayerPawn.Value!;
                int bonus = SkillsInfo.GetValue<int>(skillName, "bonusHealth");
                SkillUtils.AddHealth(pawn, bonus, Math.Max(pawn.MaxHealth, pawn.Health + bonus));

                SkillUtils.ApplyScreenColor(player, 255, 200, 0, 60, 200, 300);
                player.PrintToChat($" {ChatColors.Gold}{player.GetTranslation("clutchmaster_active_info")}");
            }
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null || clutching.IsEmpty) return;
            if (damagedEntity.DesignerName != "player") return;

            var attackerEnt = damageInfo.Attacker?.Value;
            if (attackerEnt == null || !attackerEnt.IsValid || attackerEnt.DesignerName != "player") return;
            if (attackerEnt.Handle == damagedEntity.Handle || attackerEnt.TeamNum == damagedEntity.TeamNum) return;

            var attacker = PlayerManager.GetPlayerEvent(new CCSPlayerPawn(attackerEnt.Handle).Controller?.Value?.As<CCSPlayerController>());
            if (attacker == null || !clutching.ContainsKey(attacker.Index)) return;

            damageInfo.Damage *= SkillsInfo.GetValue<float>(skillName, "damageMultiplier");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffc400", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = true, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int bonusHealth = 50, float damageMultiplier = 1.3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int BonusHealth { get; set; } = bonusHealth;
            public float DamageMultiplier { get; set; } = damageMultiplier;
        }
    }
}
