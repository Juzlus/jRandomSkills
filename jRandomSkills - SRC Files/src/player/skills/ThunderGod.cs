using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class ThunderGod : ISkill
    {
        private const Skills skillName = Skills.ThunderGod;
        private const string lightningParticle = "particles/ui/status_levels/ui_status_level7_lightning.vpcf";
        private const string shockParticle = "particles/blood_impact/impact_taser_bodyfx.vpcf";
        private const string tracerParticle = "particles/weapons/cs_weapon_fx/weapon_tracers_taser.vpcf";
        private const string zapSound = "Weapon_Taser.Single";
        private const float lightningLifetime = 1f;

        private sealed class Storm(uint ownerKey, byte team, float x, float y, float z)
        {
            public uint OwnerKey { get; } = ownerKey;
            public byte Team { get; } = team;
            public float X { get; } = x;
            public float Y { get; } = y;
            public float Z { get; } = z;
            public List<uint> Particles { get; } = [];
        }

        private static readonly ConcurrentDictionary<uint, uint> pendingDecoys = [];
        private static readonly ConcurrentDictionary<uint, Storm> storms = [];
        private static readonly ConcurrentDictionary<uint, int> decoysLeft = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            DecoyRing.PreloadAssets();
            Instance.AddToManifest(lightningParticle);
            Instance.AddToManifest(shockParticle);
            Instance.AddToManifest(tracerParticle);

            Instance.RegisterEventHandler<EventDecoyFiring>((@event, _) => { Strike(@event.Entityid); return HookResult.Continue; });
        }

        public static void NewRound()
        {
            pendingDecoys.Clear();
            decoysLeft.Clear();
            foreach (var index in storms.Keys)
                EndStorm(index);
            DecoyRing.ClearAll(skillName);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            decoysLeft.TryRemove(playerIndex, out _);

            foreach (var kvp in pendingDecoys)
                if (kvp.Value == playerIndex)
                    pendingDecoys.TryRemove(kvp.Key, out _);

            foreach (var kvp in storms)
                if (kvp.Value.OwnerKey == playerIndex)
                    EndStorm(kvp.Key);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            int grenadeLimit = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "grenadeLimit"));
            decoysLeft[player.Key()] = grenadeLimit;

            SkillUtils.TryGiveWeapon(player, CsItem.Decoy);
            SkillUtils.UpdateGrenadeCount(player, CsItem.Decoy, grenadeLimit);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            if (decoysLeft.TryRemove(player.Key(), out _))
                SkillUtils.UpdateGrenadeCount(player, CsItem.Decoy, 1);
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            if (@event.Weapon != "decoy") return;

            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid || PlayerManager.GetPlayer(player)?.Skill != skillName) return;

            if (decoysLeft.TryGetValue(player.Key(), out int left) && left > 1)
            {
                decoysLeft[player.Key()] = left - 1;
                SkillUtils.UpdateGrenadeCount(player, CsItem.Decoy, left - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            if (decoysLeft.TryGetValue(player.Key(), out int left) && left > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.Decoy, left);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            if (@event.Item != "decoy") return;

            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            if (decoysLeft.TryGetValue(player.Key(), out int left) && left > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.Decoy, left);
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            if (entity.DesignerName != "decoy_projectile") return;

            var decoy = entity.As<CDecoyProjectile>();
            uint index = decoy.Index;

            Server.NextFrame(() =>
            {
                if (decoy == null || !decoy.IsValid) return;

                var pawn = decoy.Thrower.Value;
                if (pawn == null || !pawn.IsValid) return;

                var owner = PlayerManager.GetPlayerEvent(PlayerManager.GetControllerByPawn(pawn.Handle));
                if (owner == null || PlayerManager.GetPlayer(owner)?.Skill != skillName) return;

                pendingDecoys[index] = owner.Key();
            });
        }

        public static void DecoyStarted(EventDecoyStarted @event)
        {
            if (!pendingDecoys.TryRemove((uint)@event.Entityid, out uint ownerKey)) return;

            var decoy = Utilities.GetEntityFromIndex<CDecoyProjectile>(@event.Entityid);
            byte team = decoy != null && decoy.IsValid ? decoy.TeamNum : (byte)0;
            storms[(uint)@event.Entityid] = new Storm(ownerKey, team, @event.X, @event.Y, @event.Z);

            DecoyRing.Show(skillName, (uint)@event.Entityid, new Vector(@event.X, @event.Y, @event.Z), Radius());
        }

        public static void DecoyDetonate(EventDecoyDetonate @event)
        {
            EndStorm((uint)@event.Entityid);
        }

        private static void EndStorm(uint index)
        {
            DecoyRing.Hide(skillName, index);
            if (!storms.TryRemove(index, out var storm)) return;

            lock (storm.Particles)
            {
                foreach (uint particle in storm.Particles)
                    EntityManager.DestroyEntity(particle, 0f);
                storm.Particles.Clear();
            }
        }

        private static float Radius() => Math.Max(1f, SkillsInfo.GetValue<float>(skillName, "radius"));

        private static void Strike(int entityId)
        {
            if (storms.IsEmpty || entityId <= 0) return;
            if (!storms.TryGetValue((uint)entityId, out var storm)) return;

            var decoy = Utilities.GetEntityFromIndex<CDecoyProjectile>(entityId);
            var center = decoy != null && decoy.IsValid && decoy.AbsOrigin != null
                ? new Vector(decoy.AbsOrigin.X, decoy.AbsOrigin.Y, decoy.AbsOrigin.Z)
                : new Vector(storm.X, storm.Y, storm.Z);

            float height = SkillsInfo.GetValue<float>(skillName, "lightningHeight");
            float pitch = SkillsInfo.GetValue<float>(skillName, "lightningPitch");
            var lightning = EntityManager.CreateTrackedParticleSystem(storm.OwnerKey, lightningParticle, lightningLifetime,
                new Vector(center.X, center.Y, center.Z + height), new QAngle(pitch, 0, 0));
            if (lightning != null)
                lock (storm.Particles)
                {
                    storm.Particles.RemoveAll(index => Utilities.GetEntityFromIndex<CParticleSystem>((int)index) is not { IsValid: true });
                    storm.Particles.Add(lightning.Index);
                }

            if (decoy != null && decoy.IsValid)
                decoy.EmitSound(zapSound, volume: SkillsInfo.GetValue<float>(skillName, "soundVolume"));

            var owner = PlayerManager.GetController(storm.OwnerKey);
            var ownerPawn = owner?.PlayerPawn?.Value;
            if (ownerPawn != null && !ownerPawn.IsValid) ownerPawn = null;

            float radius = Radius();
            float radiusSq = radius * radius;
            int damage = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "damage"));
            Vector start = new(center.X, center.Y, center.Z + 8f);

            foreach (var target in PlayerManager.GetAliveSnapshot())
            {
                if ((byte)target.Team == storm.Team || target.Key == storm.OwnerKey) continue;

                float dx = target.X - center.X, dy = target.Y - center.Y, dz = target.Z + 36f - center.Z;
                if (dx * dx + dy * dy + dz * dz > radiusSq) continue;

                var pawn = target.Pawn;
                EntityManager.CreateTracer(storm.OwnerKey, tracerParticle, start, new Vector(target.X, target.Y, target.Z + 40f), .5f);
                EntityManager.CreateEntityParticle(storm.OwnerKey, shockParticle, pawn, 1.5f);

                if (pawn.Health <= damage && ownerPawn != null && owner != null)
                    Electrocute(pawn, ownerPawn);
                else
                    SkillUtils.TakeHealth(pawn, damage, owner, KillfeedIcons.Taser);
            }
        }

        private static void Electrocute(CCSPlayerPawn victim, CCSPlayerPawn attacker)
        {
            var taser = Utilities.CreateEntityByName<CBasePlayerWeapon>("weapon_taser");
            if (taser == null || !taser.IsValid)
            {
                SkillUtils.TakeHealth(victim, 9999, attacker.Controller.Value?.As<CCSPlayerController>(), KillfeedIcons.Taser);
                return;
            }

            SkillUtils.RegisterNativeKill(victim, attacker, taser, DamageTypes_t.DMG_SHOCK);
            SkillUtils.TakeHealth(victim, 9999);
            taser.AddEntityIOEvent("Kill", taser, taser, "", 1f);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7ac7ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Epic, float radius = 130f, int damage = 25, int grenadeLimit = 3, float soundVolume = .8f, float lightningHeight = 40f, float lightningPitch = 90f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Radius { get; set; } = radius;
            public int Damage { get; set; } = damage;
            public int GrenadeLimit { get; set; } = grenadeLimit;
            public float SoundVolume { get; set; } = soundVolume;
            public float LightningHeight { get; set; } = lightningHeight;
            public float LightningPitch { get; set; } = lightningPitch;
        }
    }
}
