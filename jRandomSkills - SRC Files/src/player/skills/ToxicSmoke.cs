using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class ToxicSmoke : ISkill
    {
        private const Skills skillName = Skills.ToxicSmoke;
        private static readonly ConcurrentDictionary<CCSPlayerController, byte> players = [];
        private static readonly ConcurrentDictionary<CTriggerMultiple, byte> triggers = [];

        private const string triggerName = "toxic_smoke";

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            players.Clear();
            triggers.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.SmokeGrenade);
        }

        public static void OnTriggerEnter(CBaseTrigger trigger, CBaseEntity entity)
        {
            if (!trigger.Globalname.StartsWith(triggerName) || entity.DesignerName != "player") return;

            CCSPlayerPawn playerPawn = new(entity.Handle);
            if (playerPawn == null || playerPawn.Controller?.Value == null)return;
            CCSPlayerController player = playerPawn.Controller.Value.As<CCSPlayerController>();
            if (player == null) return;

            players.TryAdd(player, 0);
            Server.PrintToChatAll($"Enter: {player?.PlayerName}");
        }

        public static void OnTriggerExit(CBaseTrigger trigger, CBaseEntity entity)
        {
            if (!trigger.Globalname.StartsWith(triggerName) || entity.DesignerName != "player") return;
            var player = entity.As<CCSPlayerController>();
            if (player == null) return;
            players.TryRemove(player, out _);
            Server.PrintToChatAll($"Exit: {player?.PlayerName}");
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
                smoke.SmokeColor.X = 255;
                smoke.SmokeColor.Y = 0;
                smoke.SmokeColor.Z = 255;
            });
        }

        public static void SmokegrenadeDetonate(EventSmokegrenadeDetonate @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            var trigger = SkillUtils.CreateTrigger(triggerName, SkillsInfo.GetValue<float>(skillName, "smokeRadius"), new Vector(@event.X, @event.Y, @event.Z));
            if (trigger == null) return;
            triggers.TryAdd(trigger, 0);

            new VirtualFunctionVoid<CBaseEntity>(trigger.Handle, 153);
            
            
        }

        public static void SmokegrenadeExpired(EventSmokegrenadeExpired @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            foreach (var trigger in triggers.Keys.Where(t => t.AbsOrigin?.X == @event.X && t.AbsOrigin?.Y == @event.Y && t.AbsOrigin?.Z == @event.Z))
            {
                triggers.TryRemove(trigger, out _);
                trigger.AcceptInput("Kill");
            }
        }

        private static void AddHealth(CCSPlayerPawn player, int health)
        {
            if (player.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                return;

            player.Health += health;
            Utilities.SetStateChanged(player, "CBaseEntity", "m_iHealth");

            player.EmitSound("Player.DamageBody.Onlooker");
            if (player.Health <= 0)
                player.CommitSuicide(false, true);
        }

        public static void OnTick()
        {
            if (Server.TickCount % 17 != 0) return;
            foreach (var player in players.Keys)
            {
                if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid || !player.PawnIsAlive) return;
                AddHealth(player.PlayerPawn.Value, -SkillsInfo.GetValue<int>(skillName, "smokeDamage"));
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = false, string color = "#507529", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int smokeDamage = 2, float smokeRadius = 180) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int SmokeDamage { get; set; } = smokeDamage;
            public float SmokeRadius { get; set; } = smokeRadius;
        }
    }
}