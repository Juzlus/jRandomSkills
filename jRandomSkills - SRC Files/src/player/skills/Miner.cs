using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Miner : ISkill
    {
        private const Skills skillName = Skills.Miner;
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
            float detonationRange = SkillsInfo.GetValue<float>(skillName, "detonationRange");
            float currentTime = Server.CurrentTime;

            foreach (var index in nades.Keys.ToList())
            {
                var nade = Utilities.GetEntityFromIndex<CBaseCSGrenadeProjectile>((int)index);
                if (nade == null || !nade.IsValid || nade.AbsOrigin == null)
                {
                    nades.TryRemove(index, out _);
                    continue;
                }

                if (nade.CreateTime + 3 > currentTime) return;
                Vector currentPos = new(nade.AbsOrigin.X, nade.AbsOrigin.Y, nade.AbsOrigin.Z);
                
                foreach (var enemy in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum != nade.TeamNum))
                {
                    var enemyPawn = enemy.PlayerPawn.Value;
                    if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.AbsOrigin == null) continue;

                    Vector enemyPos = new(enemyPawn.AbsOrigin.X, enemyPawn.AbsOrigin.Y, enemyPawn.AbsOrigin.Z);
                    double distance = SkillUtils.GetDistance(currentPos, enemyPos);

                    if (distance <= detonationRange)
                    {
                        Detonate(nade);
                        nades.TryRemove(index, out _);
                        break;
                    }
                }
            }
        }

        private static void Detonate(CBaseCSGrenadeProjectile grenade)
        {
            if (grenade == null || !grenade.IsValid || grenade.AbsOrigin == null) return;

            Vector position = grenade.AbsOrigin;
            position.Z += 60;
            grenade.Teleport(position);

            grenade.EmitSound("IncGrenade.Bounce_M");

            grenade.DetonateTime = Server.CurrentTime + .5f;
            Utilities.SetStateChanged(grenade, "CBaseGrenade", "m_flDetonateTime");
        }

        public static void OnEntitySpawned(CEntityInstance @event)
        {
            var name = @event.DesignerName;
            if (name != "hegrenade_projectile") return;

            var grenade = @event.As<CBaseCSGrenadeProjectile>();
            if (grenade == null || !grenade.IsValid) return;

            if (grenade.OwnerEntity.Value == null || !grenade.OwnerEntity.Value.IsValid) return;
            var pawn = grenade.OwnerEntity.Value.As<CCSPlayerPawn>();

            if (pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;
            var player = pawn.Controller.Value.As<CCSPlayerController>();

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            nades.TryAdd(grenade.Index, 0);

            Server.NextWorldUpdate(() =>
            {
                if (grenade == null || !grenade.IsValid) return;
                grenade.DetonateTime = float.MaxValue;
                Utilities.SetStateChanged(grenade, "CBaseGrenade", "m_flDetonateTime");
            });
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.HEGrenade);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#adf542", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float detonationRange = 130) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float DetonationRange { get; set; } = detonationRange;
        }
    }
}