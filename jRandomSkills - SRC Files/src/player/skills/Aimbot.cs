using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using System.Runtime.InteropServices;
using src.utils;
using CounterStrikeSharp.API;

namespace src.player.skills
{
    public class Aimbot : ISkill
    {
        private const Skills skillName = Skills.Aimbot;
        private static readonly ThreadLocal<Stack<(nint Address, HitGroup_t OldValue)>> _restoreStack = new(() => new Stack<(nint, HitGroup_t)>());

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

            var attacker = PlayerManager.GetPlayerEvent(attackerController.As<CCSPlayerController>());
            if (attacker == null) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(attacker.Index);
            if (playerInfo == null || playerInfo.Skill != skillName) return;

            if (!SkillUtils.IsBulletDamage(param2)) return;

            int offset = GameData.GetOffset("CTakeDamageInfo_HitGroup");
            if (offset <= 0) return;

            nint hitGroupPointer = Marshal.ReadIntPtr(param2.Handle, offset);
            if (hitGroupPointer == nint.Zero) return;

            nint hitGroupData = Marshal.ReadIntPtr(hitGroupPointer, 16);
            if (hitGroupData == nint.Zero) return;

            nint address = hitGroupData + 56;
            HitGroup_t oldValue = (HitGroup_t)Marshal.ReadInt32(address);

            if (oldValue == HitGroup_t.HITGROUP_HEAD || oldValue == HitGroup_t.HITGROUP_INVALID) return;

            _restoreStack.Value!.Push((address, oldValue));
            Marshal.WriteInt32(address, (int)HitGroup_t.HITGROUP_HEAD);
        }

        public static void OnTakeDamagePost(DynamicHook h)
        {
            var info = h.GetParam<CTakeDamageInfo>(1);

            if (info == null || info.Handle == nint.Zero)
                return;

            if (!SkillUtils.IsBulletDamage(info)) return;

            if (_restoreStack.Value!.Count > 0)
            {
                var (address, oldValue) = _restoreStack.Value.Pop();

                if (address == nint.Zero)
                    return;

                Marshal.WriteInt32(address, (int)oldValue);
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff0000", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}