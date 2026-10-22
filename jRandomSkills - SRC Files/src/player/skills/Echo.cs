using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Echo : ISkill
    {
        private const Skills skillName = Skills.Echo;
        private const float molotovMatchRadius = 96f;

        private readonly record struct Throw(SkillUtils.GrenadeKind Kind, float X, float Y, float Z, float VX, float VY, float VZ, uint OwnerKey, byte Team);

        private static readonly ConcurrentDictionary<uint, Throw> throws = [];
        private static bool spawningEcho;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));

            Instance.RegisterEventHandler<EventHegrenadeDetonate>((@event, _) => { Detonated(@event.Entityid); return HookResult.Continue; });
            Instance.RegisterEventHandler<EventFlashbangDetonate>((@event, _) => { Detonated(@event.Entityid); return HookResult.Continue; });
            Instance.RegisterEventHandler<EventMolotovDetonate>((@event, _) => { DetonatedMolotov(@event.Userid, @event.X, @event.Y, @event.Z); return HookResult.Continue; });
        }

        public static void NewRound()
        {
            throws.Clear();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            foreach (var kvp in throws)
                if (kvp.Value.OwnerKey == playerIndex)
                    throws.TryRemove(kvp.Key, out _);
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            if (spawningEcho) return;

            var kind = SkillUtils.GetGrenadeKind(entity.DesignerName);
            if (kind == null || !entity.DesignerName.EndsWith("_projectile")) return;

            var grenade = entity.As<CBaseCSGrenadeProjectile>();
            uint index = grenade.Index;

            Server.NextFrame(() =>
            {
                if (grenade == null || !grenade.IsValid) return;

                var pawn = grenade.Thrower.Value;
                if (pawn == null || !pawn.IsValid) return;

                var owner = PlayerManager.GetPlayerEvent(PlayerManager.GetControllerByPawn(pawn.Handle));
                if (owner == null || PlayerManager.GetPlayer(owner)?.Skill != skillName) return;

                var resolved = kind.Value;
                if (resolved == SkillUtils.GrenadeKind.Molotov && grenade.As<CMolotovProjectile>().IsIncGrenade)
                    resolved = SkillUtils.GrenadeKind.Incendiary;

                var pos = grenade.InitialPosition;
                var vel = grenade.InitialVelocity;
                bool hasInitial = pos != null && vel != null && (pos.X != 0 || pos.Y != 0 || pos.Z != 0);
                var origin = hasInitial ? pos! : grenade.AbsOrigin;
                var velocity = hasInitial ? vel! : grenade.AbsVelocity;
                if (origin == null || velocity == null) return;

                throws[index] = new Throw(resolved, origin.X, origin.Y, origin.Z, velocity.X, velocity.Y, velocity.Z, owner.Key(), grenade.TeamNum);
            });
        }

        public static void SmokegrenadeDetonate(EventSmokegrenadeDetonate @event) => Detonated(@event.Entityid);

        public static void DecoyStarted(EventDecoyStarted @event) => Detonated(@event.Entityid);

        private static void Detonated(int entityId)
        {
            if (throws.IsEmpty || entityId <= 0) return;
            if (!throws.TryRemove((uint)entityId, out var thrown)) return;

            Server.NextFrame(() => Repeat(thrown));
        }

        private static void DetonatedMolotov(CCSPlayerController? thrower, float x, float y, float z)
        {
            if (throws.IsEmpty) return;

            uint? ownerKey = PlayerManager.GetPlayerEvent(thrower)?.Key();
            uint? best = null;
            float bestDistance = molotovMatchRadius * molotovMatchRadius;

            foreach (var (index, entry) in throws)
            {
                if (entry.Kind is not (SkillUtils.GrenadeKind.Molotov or SkillUtils.GrenadeKind.Incendiary)) continue;
                if (ownerKey != null && entry.OwnerKey != ownerKey) continue;

                var projectile = Utilities.GetEntityFromIndex<CBaseEntity>((int)index);
                if (projectile == null || !projectile.IsValid || projectile.DesignerName != "molotov_projectile" || projectile.AbsOrigin == null) continue;

                float dx = projectile.AbsOrigin.X - x, dy = projectile.AbsOrigin.Y - y, dz = projectile.AbsOrigin.Z - z;
                float distance = dx * dx + dy * dy + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }

            if (best != null && throws.TryRemove(best.Value, out var thrown))
                Server.NextFrame(() => Repeat(thrown));
        }

        private static void Repeat(Throw thrown)
        {
            var owner = PlayerManager.GetController(thrown.OwnerKey);
            var pawn = owner?.PlayerPawn?.Value;
            if (pawn != null && !pawn.IsValid) pawn = null;

            spawningEcho = true;
            try
            {
                SkillUtils.SpawnGrenade(thrown.Kind, new Vector(thrown.X, thrown.Y, thrown.Z), new Vector(thrown.VX, thrown.VY, thrown.VZ), pawn, thrown.Team);
            }
            finally
            {
                spawningEcho = false;
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7fd3c8", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}
