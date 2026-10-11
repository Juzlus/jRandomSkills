using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Dwarf : ISkill
    {
        private const Skills skillName = Skills.Dwarf;
        // Own copy of the rolled size: SkillChance is shared with any other skill the player holds.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, float> rolledScale = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void NewRound()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (!Instance.IsPlayerValid(player)) continue;
                DisableSkill(player);
            }
        }

        public static unsafe void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn != null && player.IsValid)
            {
                float newSize = (float)Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "maxScale") - SkillsInfo.GetValue<float>(skillName, "minScale")) + SkillsInfo.GetValue<float>(skillName, "minScale");
                newSize = (float)Math.Round(newSize, 2);
                playerInfo.SkillChance = newSize;
                rolledScale[player.Index] = newSize;

                SkillUtils.ChangePlayerScale(player, newSize);
                SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, newSize)}",
                    border: !PlayerManager.GetTickPlayers().Any(p => p.Team == player.Team && p != player) ? "tb" : "t");
            }
        }

        // Agent-model plugins and respawns reset the model scale; put it back every half second.
        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (!Instance.IsPlayerValid(player)) continue;
                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo?.HasSkill(skillName) != true || !rolledScale.TryGetValue(player.Index, out float expected) || expected <= 0f) continue;

                var pawn = player.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                var skeleton = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance();
                if (skeleton == null || Math.Abs(skeleton.Scale - expected) < 0.01f) continue;

                SkillUtils.ChangePlayerScale(player, expected);
            }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            rolledScale.TryRemove(player.Index, out _);

            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn != null && playerPawn?.CBodyComponent != null)
            {
                SkillUtils.ChangePlayerScale(player, 1);
                Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_CBodyComponent");
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffff00", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float minScale = .6f, float maxScale = .95f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float MinScale { get; set; } = minScale;
            public float MaxScale { get; set; } = maxScale;
        }
    }
}