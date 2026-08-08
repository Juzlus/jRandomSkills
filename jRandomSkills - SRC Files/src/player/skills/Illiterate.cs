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
        private static int lastChange = 0;
        private static readonly object offsetLock = new();

        private static int holdersTick = int.MinValue;
        private static readonly HashSet<uint> holders = [];

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

        public static void EnableSkill(CCSPlayerController _)
        {
            isActive = true;
            SendAlertToAll();
        }

        public static void Enable()
        {
            isActive = true;
        }

        public static void Disable()
        {
            isActive = false;
        }

        private static void SendAlertToAll()
        {
            holdersTick = int.MinValue;

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (!CheckIlliterateSkill(player)) continue;

                var playerEvent = PlayerManager.GetPlayerFromEvent(player);
                if (playerEvent == null || !playerEvent.IsValid) continue;

                string alert = playerEvent.GetTranslationWithoutIlliterate("illiterate_alert");
                playerEvent.PrintToCenterAlert(alert);
                SkillUtils.PrintToChat(playerEvent, $"{ChatColors.Red}{alert}", ignoreIlliterate: true);
            }
        }

        public static bool CheckIlliterateSkill(CCSPlayerController? player)
        {
            if (!isActive || player == null || !player.IsValid) return false;
            if (player.Team == CsTeam.Spectator) return false;

            int tick = Server.TickCount;
            if (tick != holdersTick)
            {
                holdersTick = tick;
                holders.Clear();
                foreach (var p in jRandomSkills.Instance.SkillPlayer)
                    if (p.Skill == skillName) holders.Add(p.PlayerIndex);
            }

            if (holders.Count == 0) return false;

            foreach (var p in PlayerManager.GetTickPlayers())
            {
                if (p == null || !p.IsValid) continue;
                if (p.Team == player.Team || !holders.Contains(p.Index)) continue;
                if (p.Pawn?.Value == null) continue;

                var pawn = p.PlayerPawn?.Value;
                if (pawn != null && pawn.Health > 0) return true;
            }

            return false;
        }

        public static string? GetRandomText(string? input)
        {
            if (string.IsNullOrEmpty(input)) return null;

            if (Server.TickCount - lastChange > 64 || offset == 0)
            {
                EnsureOffset();
                lastChange = Server.TickCount;
            }

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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#1466F5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}