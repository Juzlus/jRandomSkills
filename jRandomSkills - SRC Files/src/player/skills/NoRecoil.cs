using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class NoRecoil : ISkill
    {
        private const Skills skillName = Skills.NoRecoil;

        private static readonly ConcurrentDictionary<uint, byte> holders = [];
        private static bool noSpreadActive;

        private static bool defaultNoSpread;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            defaultNoSpread = SkillUtils.CvarValue("weapon_accuracy_nospread", false);
        }

        public static void NewRound()
        {
            holders.Clear();
            ApplyNoSpread(false);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            holders.TryAdd(player.Index, 0);
            ApplyNoSpread(true);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            holders.TryRemove(player.Index, out _);
            if (holders.IsEmpty)
                ApplyNoSpread(false);
        }

        private static void ApplyNoSpread(bool enabled)
        {
            if (noSpreadActive == enabled) return;

            noSpreadActive = enabled;
            bool value = enabled || defaultNoSpread;
            Server.ExecuteCommand($"weapon_accuracy_nospread {(value ? 1 : 0)}");
        }

        public static void OnTick()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (!Instance.IsPlayerValid(player)) continue;
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);

                if (playerInfo?.Skill == skillName)
                {
                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) continue;

                    if (pawn.AimPunchServices != null)
                    {
                        pawn.AimPunchServices.PredictableBaseTick = 0;
                        pawn.AimPunchServices.PredictableBaseTickInterpAmount = 0;
                        pawn.AimPunchServices.UnpredictableBaseTick = 0;
                    }

                    if (pawn.CameraServices != null)
                    {
                        pawn.CameraServices.CsViewPunchAngleTick = 0;
                        pawn.CameraServices.CsViewPunchAngleTickRatio = 0f;
                    }
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8a42f5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}