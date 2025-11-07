using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Baseball : ISkill
    {
        private const Skills skillName = Skills.Baseball;
        private static readonly ConcurrentDictionary<CDecoyProjectile, byte> decoys = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = @event.Userid;
            var attacker = @event.Attacker;
            var weapon = @event.Weapon;

            if (weapon != "decoy") return;
            if (!Instance.IsPlayerValid(victim) || !Instance.IsPlayerValid(attacker)) return;

            var attackerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);
            if (attackerInfo?.Skill != skillName) return;

            SkillUtils.TakeHealth(victim!.PlayerPawn.Value, SkillsInfo.GetValue<int>(skillName, "damageDeal"));
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            var name = entity.DesignerName;
            if (name != "decoy_projectile")
                return;

            var decoy = entity.As<CDecoyProjectile>();
            if (decoy == null || !decoy.IsValid || decoy.OwnerEntity == null || decoy.OwnerEntity.Value == null || !decoy.OwnerEntity.Value.IsValid) return;

            var pawn = decoy.OwnerEntity.Value.As<CCSPlayerPawn>();
            if (pawn == null || !pawn.IsValid || pawn.Controller == null || pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;

            var player = pawn.Controller.Value.As<CCSPlayerController>();
            if (player == null || !player.IsValid) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;
            decoys.TryAdd(decoy, 0);
        }

        public static void DecoyStarted(EventDecoyStarted @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            var decoy = decoys.FirstOrDefault(d => d.Key.Index == @event.Entityid).Key;
            if (decoy != null && decoy.IsValid)
                decoy.AcceptInput("Kill");
        }

        public static void OnTick()
        {
            foreach (var decoy in decoys.Keys)
            {
                if (!decoy.IsValid)
                {
                    decoys.TryRemove(decoy, out _);
                    continue;
                }
                decoy.Bounces = 0;
                if (Server.TickCount % 8 != 0) continue;
                var vel = decoy.AbsVelocity;
                float speed = vel.Length();
                float targetSpeed = Math.Min(speed * SkillsInfo.GetValue<float>(skillName, "speedMultipier"), SkillsInfo.GetValue<float>(skillName, "maxSpeed"));

                if (speed > .01f)
                {
                    var dir = vel / speed;
                    var newVelocity = dir * targetSpeed;

                    decoy.AbsVelocity.X = newVelocity.X;
                    decoy.AbsVelocity.Y = newVelocity.Y;
                    decoy.AbsVelocity.Z = newVelocity.Z;
                }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.DecoyGrenade);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#2effc7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float speedMultipier = 2f, float maxSpeed = 900f, int damageDeal = 9999) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float SpeedMultipier { get; set; } = speedMultipier;
            public float MaxSpeed { get; set; } = maxSpeed;
            public float DamageDeal { get; set; } = damageDeal;
        }
    }
}