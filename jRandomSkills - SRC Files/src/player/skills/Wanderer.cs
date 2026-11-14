using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Wanderer : ISkill
    {
        private const Skills skillName = Skills.Wanderer;
        private const float teleportThreshold = 400f;
        private static readonly List<CCSPlayerController> holderBuffer = [];
        private static readonly ConcurrentDictionary<uint, (float x, float y, float acc)> trackers = [];

        public static void LoadSkill()
        {
            Localization.RegisterDescriptionArgs(skillName, () => [(object)SkillsInfo.GetValue<float>(skillName, "unitsPerHeal"), (object)SkillsInfo.GetValue<int>(skillName, "healAmount")]);
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            trackers.TryRemove(player.Key(), out _);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            trackers.TryRemove(player.Key(), out _);
        }

        public static void NewRound()
        {
            trackers.Clear();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            trackers.TryRemove(playerIndex, out _);
        }

        public static void OnTick()
        {
            if (Server.TickCount % 16 != 0) return;

            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            if (holderBuffer.Count == 0) return;

            float unitsPerHeal = Math.Max(1f, SkillsInfo.GetValue<float>(skillName, "unitsPerHeal"));
            int healAmount = SkillsInfo.GetValue<int>(skillName, "healAmount");

            foreach (var player in holderBuffer)
            {
                if (player == null || !player.IsValid) continue;
                uint key = player.Key();

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || pawn.AbsOrigin == null)
                {
                    trackers.TryRemove(key, out _);
                    continue;
                }

                float x = pawn.AbsOrigin.X, y = pawn.AbsOrigin.Y;
                if (!trackers.TryGetValue(key, out var last))
                {
                    trackers[key] = (x, y, 0f);
                    continue;
                }

                float dx = x - last.x, dy = y - last.y;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                float acc = distance > teleportThreshold ? last.acc : last.acc + distance;

                if (acc >= unitsPerHeal)
                {
                    int chunks = (int)(acc / unitsPerHeal);
                    acc -= chunks * unitsPerHeal;
                    if (healAmount > 0)
                        SkillUtils.AddHealth(pawn, healAmount * chunks);
                }

                trackers[key] = (x, y, acc);
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8bc34a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float unitsPerHeal = 200f, int healAmount = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float UnitsPerHeal { get; set; } = unitsPerHeal;
            public int HealAmount { get; set; } = healAmount;
        }
    }
}
