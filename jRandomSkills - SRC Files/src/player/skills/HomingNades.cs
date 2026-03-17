using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class HomingNades : ISkill
    {
        private const Skills skillName = Skills.HomingNades;
        private readonly static ConcurrentDictionary<uint, (Vector, double)> nades = [];

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
            
            foreach (var index in nades.Keys.ToList())
            {
                if (!nades.TryGetValue(index, out var data)) continue;
                (Vector oldPos, double createdTime) = data;
                
                var nade = Utilities.GetEntityFromIndex<CBaseCSGrenadeProjectile>((int)index);
                if (nade == null || !nade.IsValid || nade.AbsOrigin == null)
                {
                    nades.TryRemove(index, out _);
                    continue;
                }

                Vector currentPos = new(nade.AbsOrigin.X, nade.AbsOrigin.Y, nade.AbsOrigin.Z);
                double distanceMoved = SkillUtils.GetDistance(currentPos, oldPos);
                Vector calculatedVelocity = CalculateVelocity(nade, nade.TeamNum);

                Server.PrintToChatAll($"Dist: {distanceMoved}, vel: {calculatedVelocity}");

                if (distanceMoved < 4 || calculatedVelocity.IsZero())
                {
                    Server.PrintToChatAll($"Short, {Server.TickedTime} -> {createdTime + 3}, {createdTime + 3 - Server.TickedTime}s");

                    nade.DetonateTime = (float)createdTime + 3;
                    Utilities.SetStateChanged(nade, "CBaseGrenade", "m_flDetonateTime");
                    nades.TryRemove(index, out _);
                    continue;
                }

                Vector currentVel = new(nade.Velocity.X, nade.Velocity.Y, nade.Velocity.Z);
                float maxVelocity = SkillsInfo.GetValue<float>(skillName, "maxVelocity");
                Vector newVelocity = currentVel + calculatedVelocity;

                float speed = newVelocity.Length();
                if (speed > maxVelocity)
                    newVelocity *= (maxVelocity / speed);

                nades[index] = (currentPos, createdTime);
                nade.Teleport(null, null, newVelocity);
            }
        }

        private static Vector CalculateVelocity(CBaseCSGrenadeProjectile nade, int team)
        {
            if (nade.AbsOrigin == null) return Vector.Zero;

            Vector? closetEnemyPos = null;
            double minDistance = int.MaxValue;
            Vector nadePos = nade.AbsOrigin;

            foreach (var enemy in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum != team))
            {
                var pawn = enemy.PlayerPawn.Value;
                if (pawn?.IsValid != true || pawn.AbsOrigin == null) continue;
                
                double dist = SkillUtils.GetDistance(nadePos, pawn.AbsOrigin);
                if (dist < SkillsInfo.GetValue<float>(skillName, "detonationRange"))
                {
                    nade.DetonateTime = 0;
                    Utilities.SetStateChanged(nade, "CBaseGrenade", "m_flDetonateTime");

                    nades.TryRemove(nade.Index, out _);
                    return Vector.Zero;
                }

                if (dist < minDistance)
                {
                    minDistance = dist;
                    closetEnemyPos = pawn.AbsOrigin;
                }
            }

            if (closetEnemyPos == null)
                return Vector.Zero;

            Vector direction = closetEnemyPos - nadePos;
            float length = direction.Length();

            if (length > 0)
            {
                float strength = SkillsInfo.GetValue<float>(skillName, "strength");
                return new Vector(
                    (direction.X / length) * strength,
                    (direction.Y / length) * strength,
                    (direction.Z / length) * strength
                );
            }

            return Vector.Zero;
        }

        public static void OnEntitySpawned(CEntityInstance @event)
        {
            var name = @event.DesignerName;
            if (!name.EndsWith("_projectile") || name == "smokegrenade_projectile") return;

            var grenade = @event.As<CBaseCSGrenadeProjectile>();
            if (grenade == null || !grenade.IsValid) return;

            if (grenade.OwnerEntity.Value == null || !grenade.OwnerEntity.Value.IsValid) return;
            var pawn = grenade.OwnerEntity.Value.As<CCSPlayerPawn>();

            if (pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;
            var player = pawn.Controller.Value.As<CCSPlayerController>();

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            grenade.DetonateTime = (float)Server.TickedTime + 6;

            Vector pos = new(grenade.AbsOrigin?.X, grenade.AbsOrigin?.Y, grenade.AbsOrigin?.Z);
            nades.TryAdd(grenade.Index, (pos, Server.TickedTime));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.HEGrenade);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#384728", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float strength = 150, float maxVelocity = 2000, float detonationRange = 130) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Strength { get; set; } = strength;
            public float MaxVelocity { get; set; } = maxVelocity;
            public float DetonationRange { get; set; } = detonationRange;
        }
    }
}