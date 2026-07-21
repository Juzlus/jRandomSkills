using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using src.utils;
using CounterStrikeSharp.API;

namespace src.player.skills
{
    public class Aimbot : ISkill
    {
        private const Skills skillName = Skills.Aimbot;
        private static readonly ConcurrentDictionary<nint, int> hitGroups = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || !param.IsValid || param2 == null || param2.Handle == nint.Zero) return;
            if (param2.Attacker == null || !param2.Attacker.IsValid || param2.Attacker.Value == null) return;

            var attackerEnt = param2.Attacker.Value;
            if (attackerEnt == null || !attackerEnt.IsValid) return;

            var attackerPawn = attackerEnt.As<CCSPlayerPawn>();
            if (attackerPawn == null || !attackerPawn.IsValid) return;

            var attackerController = attackerPawn.Controller.Value;
            if (attackerController == null || !attackerController.IsValid) return;

            var attacker = PlayerManager.GetPlayerEvent(attackerController.As<CCSPlayerController>())!;

            var playerInfo = PlayerManager.GetPlayerByIndex(attacker.Index);
            if (playerInfo == null) return;

            int offset = GameData.GetOffset("CTakeDamageInfo_HitGroup");
            if (offset <= 0) return;

            nint hitGroupPointer = Marshal.ReadIntPtr(param2.Handle, offset);
            if (hitGroupPointer == nint.Zero) return;

            nint hitGroupOffset = Marshal.ReadIntPtr(hitGroupPointer, 16);
            if (hitGroupOffset == nint.Zero) return;

            if (playerInfo.Skill == skillName)
            {
                hitGroups.TryAdd(hitGroupOffset, Marshal.ReadInt32(hitGroupOffset, 56));
                Marshal.WriteInt32(hitGroupOffset, 56, (int)HitGroup_t.HITGROUP_HEAD);
            }
            else if (hitGroups.TryGetValue(hitGroupOffset, out var hitGroup))
                Marshal.WriteInt32(hitGroupOffset, 56, hitGroup);
        }

        public static void NewRound()
        {
            hitGroups.Clear();
        }

        public static void DisableSkill(CCSPlayerController _)
        {
            foreach (var hit in hitGroups)
            {
                if (hit.Key != nint.Zero)
                    Marshal.WriteInt32(hit.Key, 56, hit.Value);
            }

            hitGroups.Clear();
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff0000", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}