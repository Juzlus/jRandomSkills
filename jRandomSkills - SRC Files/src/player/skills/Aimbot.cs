using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using System.Runtime.InteropServices;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

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

            if (param == null || !param.IsValid || param.Entity == null)
                return;

            if (param2 == null || param2.Handle == nint.Zero || param2.Attacker == null || !param2.Attacker.IsValid)
                return;

            var attackerHandle = param2.Attacker;
            if (attackerHandle.Value == null || !attackerHandle.IsValid)
                return;

            var attackerEnt = attackerHandle.Value;
            var victimEnt = param;

            if (attackerEnt == null || victimEnt == null || !victimEnt.IsValid || !attackerEnt.IsValid)
                return;

            CCSPlayerPawn attackerPawn = new(attackerEnt.Handle);
            CCSPlayerPawn victimPawn = new(victimEnt.Handle);

            if (attackerPawn == null || !attackerPawn.IsValid || victimPawn == null || !victimPawn.IsValid)
                return;

            if (attackerPawn.DesignerName != "player" || victimPawn.DesignerName != "player")
                return;

            var attackerController = attackerPawn.Controller?.Value;
            var victimController = victimPawn.Controller?.Value;

            if (!attackerController.IsValid() || !victimController.IsValid())
                return;

            CCSPlayerController attacker = attackerController!.As<CCSPlayerController>();
            CCSPlayerController victim = victimController!.As<CCSPlayerController>();

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker.SteamID);
            if (playerInfo == null) return;

            if (!attacker.CheckPlayer())
                return;

            int offset = GameData.GetOffset("CTakeDamageInfo_HitGroup");
            if (offset <= 0)
                return;

            nint hitGroupPointer = Marshal.ReadIntPtr(param2.Handle, offset);
            if (hitGroupPointer == nint.Zero)
                return;

            nint hitGroupOffset = Marshal.ReadIntPtr(hitGroupPointer, 16);
            if (hitGroupOffset == nint.Zero)
                return;

            if (playerInfo.Skill == skillName)
            {
                hitGroups[hitGroupOffset] = Marshal.ReadInt32(hitGroupOffset, 56);
                Marshal.WriteInt32(hitGroupOffset, 56, (int)HitGroup_t.HITGROUP_HEAD);
            }
            else if (hitGroups.TryGetValue(hitGroupOffset, out var hitGroup))
                Marshal.WriteInt32(hitGroupOffset, 56, hitGroup);
        }

        public static void DisableSkill(CCSPlayerController _)
        {
            foreach (var hit in hitGroups)
                Marshal.WriteInt32(hit.Key, 56, hit.Value);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff0000", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Epic) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}