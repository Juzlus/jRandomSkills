using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class LongKnife : ISkill
    {
        private const Skills skillName = Skills.LongKnife;

        private static bool hooked = false;
        private const int actionCode = 503;
        private static readonly ConcurrentDictionary<ulong, byte> playersInAction = [];
        private static readonly MemoryFunctionVoid<IntPtr, short> Shoot_Secondary = new(GameData.GetSignature("Shoot_Secondary"));

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            playersInAction.Clear();
            hooked = false;
            Shoot_Secondary.Unhook(ShootSecondary, HookMode.Pre);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (hooked) return;
            hooked = true;
            Shoot_Secondary.Hook(ShootSecondary, HookMode.Pre);
            playersInAction.TryAdd(player.SteamID, 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            playersInAction.TryRemove(player.SteamID, out _);
            if (playersInAction.IsEmpty)
            {
                Shoot_Secondary.Unhook(ShootSecondary, HookMode.Pre);
                hooked = false;
            }
        }

        public static HookResult ShootSecondary(DynamicHook hook)
        {
            var weapon = hook.GetParam<CBasePlayerWeapon>(0);
            var action = hook.GetParam<short>(1);

            if (action != actionCode || (weapon?.DesignerName != "weapon_knife" && weapon?.DesignerName != "weapon_bayonet")) return HookResult.Continue;
            if (weapon.OwnerEntity.Value == null || !weapon.OwnerEntity.Value.IsValid) return HookResult.Continue;

            var pawn = weapon.OwnerEntity.Value.As<CCSPlayerPawn>();
            if (pawn == null || !pawn.IsValid || pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return HookResult.Continue;

            var player = pawn.Controller.Value.As<CCSPlayerController>();
            if (player == null || !player.IsValid) return HookResult.Continue;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null || playerInfo.Skill != skillName) return HookResult.Continue;

            KnifeHit(player, true);
            return HookResult.Continue;
        }

        public static void WeaponFire(EventWeaponFire @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            if (playerInfo?.Skill != skillName) return;

            var pawn = player!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.WeaponServices == null) return;

            var activeWeapon = pawn.WeaponServices.ActiveWeapon.Value;
            if (activeWeapon == null || !activeWeapon.IsValid || (activeWeapon.DesignerName != "weapon_knife" && activeWeapon.DesignerName != "weapon_bayonet")) return;

            KnifeHit(player, false);
        }

        private unsafe static void KnifeHit(CCSPlayerController player, bool heavyHit)
        {
            var pawn = player!.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
                return;

            var result = RayTrace.EyeTrace(player);
            if (result == null || !result.HasValue)
                return;

            if (!result.Value.HitPlayer(out CCSPlayerController? target) || target == null)
                return;
            
            if (target.Handle == player.Handle || result.Value.Distance() <= 70)
                return;
                
            if (target.PlayerPawn.Value == null || !target.PlayerPawn.Value.IsValid)
                return;

            if (!SkillsInfo.GetValue<bool>(skillName, "friendlyFire") && player.Team == target.Team) return;

            target.PlayerPawn.Value.EmitSound("Player.DamageBody.Onlooker");
            SkillUtils.TakeHealth(target.PlayerPawn.Value, heavyHit ? Instance.Random.Next(45, 55) : Instance.Random.Next(21, 34));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#c9f8ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float maxDistance = 4096f, bool friendlyFire = true) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float MaxDistance { get; set; } = maxDistance;
            public bool FriendlyFire {  get; set; } = friendlyFire;
        }
    }
}