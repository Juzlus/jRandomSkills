using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class Illiterate : ISkill
    {
        private const Skills skillName = Skills.Illiterate;
        private static bool isActive = false;
        private static int offset = 5;
        private static readonly object offsetLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            EnsureOffset();
        }

        public static void NewRound()
        {
            isActive = false;
            lock (offsetLock)
            {
                offset = jRandomSkills.Instance?.Random?.Next(1, 26) ?? new Random().Next(1, 26);
                if (offset == 13) offset = 14;
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            isActive = true;
        }

        public static void Enable()
        {
            isActive = true;
        }

        public static void Disable()
        {
            isActive = false;
        }

        public static bool CheckIlliterateSkill(CCSPlayerController? player)
        {
            if (!isActive || player == null || !player.IsValid) return false;
            if (player.Team == CsTeam.Spectator) return false;

            var playersWithSkill = jRandomSkills.Instance.SkillPlayer.Where(p => p.Skill == skillName).Select(p => p.SteamID).ToHashSet();
            if (playersWithSkill.Count == 0) return false;

            return Utilities.GetPlayers().Any(
                p => p != null &&
                     p.IsValid &&
                     p.Pawn?.Value != null &&
                     p.PawnIsAlive &&
                     p.Team != player.Team &&
                     playersWithSkill.Contains(p.SteamID));
        }

        public static string? GetRandomText(string? input)
        {
            if (string.IsNullOrEmpty(input)) return null;

            if (Server.TickCount % 64 == 0 || offset == 0)
                EnsureOffset();

            var chars = input.Select(c =>
            {
                if (char.IsDigit(c)) return '?';
                if (!char.IsLetter(c)) return c;

                char baseChar = char.IsUpper(c) ? 'A' : 'a';
                int shifted = (c - baseChar + offset) % 26;
                return (char)(baseChar + shifted);
            }).ToArray();

            return new string(chars);
        }

        private static void EnsureOffset()
        {
            lock (offsetLock)
            {
                if (offset != 0) return;
                try
                {
                    offset = jRandomSkills.Instance?.Random?.Next(1, 26) ?? new Random().Next(1, 26);
                }
                catch
                {
                    offset = new Random().Next(1, 26);
                }

                if (offset == 13) offset = 14;
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#1466F5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = 1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}