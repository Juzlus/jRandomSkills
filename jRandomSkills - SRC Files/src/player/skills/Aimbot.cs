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

            if (param == null || param.Entity == null || param2 == null || param2.Attacker == null || param2.Attacker.Value == null)
                return;

            CCSPlayerPawn attackerPawn = new(param2.Attacker.Value.Handle);
            CCSPlayerPawn victimPawn = new(param.Handle);

            if (attackerPawn.DesignerName != "player" || victimPawn.DesignerName != "player")
                return;

            if (attackerPawn == null || attackerPawn.Controller?.Value == null || victimPawn == null || victimPawn.Controller?.Value == null)
                return;

            CCSPlayerController attacker = attackerPawn.Controller.Value.As<CCSPlayerController>();
            CCSPlayerController victim = victimPawn.Controller.Value.As<CCSPlayerController>();

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker.SteamID);
            if (playerInfo == null) return;

            if (attacker.PawnIsAlive)
            {
                nint hitGroupPointer = Marshal.ReadIntPtr(param2.Handle, GameData.GetOffset("CTakeDamageInfo_HitGroup"));
                if (hitGroupPointer != nint.Zero)
                {
                    nint hitGroupOffset = Marshal.ReadIntPtr(hitGroupPointer, 16);
                    if (hitGroupOffset != nint.Zero)
                    {
                        if (playerInfo.Skill == skillName)
                        {
                            int oldValue = Marshal.ReadInt32(hitGroupOffset, 56);
                            hitGroups.TryAdd(hitGroupOffset, Marshal.ReadInt32(hitGroupOffset, 56));
                            Marshal.WriteInt32(hitGroupOffset, 56, (int)HitGroup_t.HITGROUP_HEAD);
                        } else if (hitGroups.TryGetValue(hitGroupOffset, out var hitGroup))
                                Marshal.WriteInt32(hitGroupOffset, 56, hitGroup);
                    }
                }
            }
        }

        public static void DisableSkill(CCSPlayerController _)
        {
            foreach (var hit in hitGroups)
                Marshal.WriteInt32(hit.Key, 56, hit.Value);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff0000", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}