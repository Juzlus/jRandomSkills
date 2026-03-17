using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Weightless : ISkill
    {
        private const Skills skillName = Skills.Weightless;
        private readonly static ConcurrentDictionary<uint, byte> nades = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            nades.Clear();
        }

        public static void OnTick()
        {
            if (Server.TickCount % 10 != 0) return;

            foreach ((var index, _) in nades)
            {
                var nade = Utilities.GetEntityFromIndex<CBaseCSGrenadeProjectile>((int)index);
                if (nade == null || !nade.IsValid)
                {
                    nades.TryRemove(index, out _);
                    continue;
                }

                if (nade.Bounces != 0)
                {
                    nade.ActualGravityScale = 2;
                    
                    nade.DetonateTime = 0;
                    Utilities.SetStateChanged(nade, "CBaseGrenade", "m_flDetonateTime");

                    nades.TryRemove(index, out _);
                }
            }
        }

        public static void OnEntitySpawned(CEntityInstance @event)
        {
            var name = @event.DesignerName;
            if (!name.EndsWith("_projectile")) return;

            var grenade = @event.As<CBaseCSGrenadeProjectile>();
            if (grenade == null || !grenade.IsValid) return;

            if (grenade.OwnerEntity.Value == null || !grenade.OwnerEntity.Value.IsValid) return;
            var pawn = grenade.OwnerEntity.Value.As<CCSPlayerPawn>();

            if (pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;
            var player = pawn.Controller.Value.As<CCSPlayerController>();

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            Server.NextFrame(() => {
                if (grenade == null || !grenade.IsValid) return;
                grenade.ActualGravityScale = .0001f;
                
                Vector currentVelocity = new(grenade.AbsVelocity.X, grenade.AbsVelocity.Y, grenade.AbsVelocity.Z);
                float speed = currentVelocity.Length() * 2;

                Vector forward = SkillUtils.GetForwardVector(pawn.EyeAngles);
                Vector newVelocity = forward * speed;

                grenade.Teleport(null, null, newVelocity);
                nades.TryAdd(grenade.Index, 0);
            });
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.HEGrenade);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8f6dc9", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}