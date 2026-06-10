using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class ToxicSmoke : ISkill
    {
        private const Skills skillName = Skills.ToxicSmoke;
        private static readonly ConcurrentDictionary<Vector, byte> smokes = [];
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];

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
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            smokes.TryAdd(new Vector(@event.X, @event.Y, @event.Z), 0);
        }

        public static void SmokegrenadeExpired(EventSmokegrenadeExpired @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
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

            var playerInfo = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(player)!.Index);
            if (playerInfo?.Skill != skillName) return;

            Server.NextFrame(() =>
            {
                if (entity == null || !entity.IsValid) return;
                var smoke = entity.As<CSmokeGrenadeProjectile>();
                smoke.SmokeColor.X = 255;
                smoke.SmokeColor.Y = 0;
                smoke.SmokeColor.Z = 255;
            });
        }

        public static void OnTick()
        {
            int tick = SkillsInfo.GetValue<int>(skillName, "tickCooldown");
            if (Server.TickCount % tick != 0) return;

            float smokeRadius = SkillsInfo.GetValue<float>(skillName, "smokeRadius");
            int smokeDamage = SkillsInfo.GetValue<int>(skillName, "smokeDamage");

            foreach (Vector smokePos in smokes.Keys)
                foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid))
                {
                    var eventPlayer = PlayerManager.GetPlayerEvent(player);
                    if (eventPlayer == null || !eventPlayer.IsValid) continue;

                    var pawn = eventPlayer.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) continue;

                    if (SkillUtils.GetDistance(smokePos, pawn.AbsOrigin) <= smokeRadius)
                        if (SkillUtils.TakeHealth(pawn, smokeDamage))
                            player.EmitSound("Player.DamageBody.Onlooker");
                }
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Weapon;
            if (weapon != "smokegrenade") return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Index] = grenadesLeft - 1;
                player!.GiveNamedItem($"weapon_{weapon}");
                SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadesLeft - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadesLeft);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Item;
            if (string.IsNullOrEmpty(weapon) || weapon != "smokegrenade") return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadesLeft);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            playersWithSkill.TryAdd(player.Index, grenadeLimit);

            SkillUtils.TryGiveWeapon(player, CsItem.SmokeGrenade);
            SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadeLimit);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Index, out _);
            SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#507529", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, int smokeDamage = 2, float smokeRadius = 180, int tickCooldown = 17, int grenadeLimit = 1) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public int SmokeDamage { get; set; } = smokeDamage;
            public float SmokeRadius { get; set; } = smokeRadius;
            public int TickCooldown { get; set; } = tickCooldown;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}