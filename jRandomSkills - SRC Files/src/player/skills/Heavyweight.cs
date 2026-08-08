using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Heavyweight : ISkill
    {
        private const Skills skillName = Skills.Heavyweight;

        private static readonly ConcurrentDictionary<uint, byte> holders = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            holders.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            holders[player.Index] = 0;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            holders.TryRemove(player.Index, out _);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            holders.TryRemove(playerIndex, out _);
        }

        public static bool Resists(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid || holders.IsEmpty) return false;

            if (holders.ContainsKey(player.Index)) return true;

            uint routedIndex = PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index;
            return routedIndex != player.Index && holders.ContainsKey(routedIndex);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8d6e63", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
