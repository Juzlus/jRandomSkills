using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class LastStand : ISkill
    {
        private const Skills skillName = Skills.LastStand;

        private static readonly ConcurrentDictionary<uint, byte> triggered = [];
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            Localization.RegisterDescriptionArgs(skillName, () =>
            [
                SkillsInfo.GetValue<int>(skillName, "healthToAdd"),
                SkillsInfo.GetValue<int>(skillName, "armor")
            ]);
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            triggered.Clear();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            triggered.TryRemove(player.Key(), out _);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            triggered.TryRemove(playerIndex, out _);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            foreach (var holder in holderBuffer)
            {
                if (holder == null || !holder.IsValid || !holder.PawnIsAlive) continue;
                if (holder.Key() == victim.Key() || holder.Team != victim.Team) continue;
                if (triggered.ContainsKey(holder.Key())) continue;
                if (AnotherTeammateAlive(holder, victim.Key())) continue;
                if (!triggered.TryAdd(holder.Key(), 0)) continue;

                var pawn = holder.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid) continue;

                SkillUtils.AddHealth(pawn, SkillsInfo.GetValue<int>(skillName, "healthToAdd"));

                int armor = SkillsInfo.GetValue<int>(skillName, "armor");
                if (armor > pawn.ArmorValue)
                {
                    pawn.ArmorValue = armor;
                    NetStateFast.Changed(pawn, "CCSPlayerPawn", "m_ArmorValue");
                }

                holder.PrintToChat($" {ChatColors.Green}{holder.GetTranslation("laststand_triggered")}");
            }
        }

        // The victim of this very event may still look alive while the death is being processed, so it is skipped explicitly.
        private static bool AnotherTeammateAlive(CCSPlayerController holder, uint dyingKey)
        {
            foreach (var other in Utilities.GetPlayers())
            {
                if (other == null || !other.IsValid || other.IsHLTV) continue;
                if (other.Team != holder.Team) continue;
                uint key = other.Key();
                if (key == holder.Key() || key == dyingKey) continue;
                if (other.PawnIsAlive) return true;
            }
            return false;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d94b4b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = true, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int healthToAdd = 50, int armor = 100) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int HealthToAdd { get; set; } = healthToAdd;
            public int Armor { get; set; } = armor;
        }
    }
}
