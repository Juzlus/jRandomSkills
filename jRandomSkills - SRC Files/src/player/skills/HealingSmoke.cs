using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class HealingSmoke : ISkill
    {
        private const Skills skillName = Skills.HealingSmoke;
        private static readonly ConcurrentDictionary<Vector, byte> smokes = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            smokes.Clear();
        }

        public static void SmokegrenadeDetonate(EventSmokegrenadeDetonate @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;
            smokes.TryAdd(new Vector(@event.X, @event.Y, @event.Z), 0);
        }

        public static void SmokegrenadeExpired(EventSmokegrenadeExpired @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;
            foreach (var smoke in smokes.Keys.Where(v => v.X == @event.X && v.Y == @event.Y && v.Z == @event.Z))    
                smokes.TryRemove(smoke, out _);
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            var name = entity.DesignerName;
            if (name != "smokegrenade_projectile") return;

            var grenade = entity.As<CBaseCSGrenadeProjectile>();
            if (grenade == null || !grenade.IsValid || grenade.OwnerEntity == null || !grenade.OwnerEntity.IsValid || grenade.OwnerEntity.Value == null || !grenade.OwnerEntity.Value.IsValid) return;

            var pawn = grenade.OwnerEntity.Value.As<CCSPlayerPawn>();
            if (pawn == null || !pawn.IsValid || pawn.Controller == null || !pawn.Controller.IsValid || pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;

            var player = pawn.Controller.Value.As<CCSPlayerController>();
            if (player == null || !player.IsValid) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            Server.NextFrame(() =>
            {
                var smoke = entity.As<CSmokeGrenadeProjectile>();
                smoke.SmokeColor.X = 0;
                smoke.SmokeColor.Y = 255;
                smoke.SmokeColor.Z = 0;
            });
        }

        public static void OnTick()
        {
            foreach (Vector smokePos in smokes.Keys)
                foreach (var player in Utilities.GetPlayers())
                    if (Server.TickCount % 17 == 0)
                        if (player.PlayerPawn.Value != null && player.PlayerPawn.Value.IsValid && player.PlayerPawn.Value.AbsOrigin != null)
                            if (SkillUtils.GetDistance(smokePos, player.PlayerPawn.Value.AbsOrigin) <= SkillsInfo.GetValue<float>(skillName, "smokeRadius"))
                                AddHealth(player.PlayerPawn.Value, SkillsInfo.GetValue<int>(skillName, "smokeHeal"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.SmokeGrenade);
        }

        private static void AddHealth(CCSPlayerPawn player, int health)
        {
            if (player.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                return;

            if (player.Health != player.MaxHealth)
                player.EmitSound("Healthshot.Success");

            player.Health = Math.Min(player.Health + health, player.MaxHealth);
            Utilities.SetStateChanged(player, "CBaseEntity", "m_iHealth");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#1fe070", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int smokeHeal = 1, float smokeRadius = 180) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int SmokeHeal { get; set; } = smokeHeal;
            public float SmokeRadius { get; set; } = smokeRadius;
        }
    }
}