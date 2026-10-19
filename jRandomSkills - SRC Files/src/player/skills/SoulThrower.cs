using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Drawing;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class SoulThrower : ISkill
    {
        private const Skills skillName = Skills.SoulThrower;
        private const string nadePrefix = "soulthrower_";
        private const float HitRadius = 40f;
        private const float CoreHeight = 48f;
        private const int LaunchDelayTicks = 12;

        private sealed class Soul
        {
            public uint PropIndex;
            public uint OwnerKey;
            public byte Team;
            public float X, Y, Z;
            public float DirX, DirY, DirZ;
            public float Travelled;
            public int LaunchTick;
        }

        private static readonly List<Soul> souls = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
                souls.Clear();
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = @event.Userid;
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            if (victim == null || !victim.IsValid || attacker == null || !attacker.IsValid) return;
            if (victim.Key() == attacker.Key() || victim.Team == attacker.Team) return;
            if (PlayerManager.GetPlayer(attacker)?.Skill != skillName) return;

            var victimPawn = victim.PlayerPawn?.Value;
            var attackerPawn = attacker.PlayerPawn?.Value;
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.AbsOrigin == null) return;
            if (attackerPawn == null || !attackerPawn.IsValid || attackerPawn.AbsOrigin == null) return;

            string? model = victimPawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState?.ModelName;
            if (string.IsNullOrEmpty(model)) return;

            float toX = victimPawn.AbsOrigin.X, toY = victimPawn.AbsOrigin.Y, toZ = victimPawn.AbsOrigin.Z;
            float dx = toX - attackerPawn.AbsOrigin.X, dy = toY - attackerPawn.AbsOrigin.Y;
            float length = MathF.Sqrt(dx * dx + dy * dy);
            if (length < 1f) return;

            uint ownerKey = attacker.Key();
            byte team = attacker.TeamNum;
            Server.NextFrame(() => Release(ownerKey, team, model, toX, toY, toZ, dx / length, dy / length, 0f));
        }

        private static void Release(uint ownerKey, byte team, string model, float x, float y, float z, float dirX, float dirY, float dirZ)
        {
            var prop = EntityManager.CreateTrackedDynamicProp(ownerKey, "prop_dynamic", "soul");
            if (prop == null || !prop.IsValid) return;

            prop.AnimGraphUpdateEnabled = false;
            using (var keys = new CEntityKeyValues())
            {
                keys.SetString("model", model);
                keys.SetInt("spawnflags", 256);
                keys.SetVector("origin", new Vector(x, y, z));
                prop.DispatchSpawn(keys);
            }
            if (!prop.IsValid) return;
            prop.AcceptInput("DisableCollision");

            var color = SkillsInfo.GetValue<string>(skillName, "soulColor");
            try
            {
                prop.Glow.GlowColorOverride = ColorTranslator.FromHtml(string.IsNullOrEmpty(color) ? "#ffffff" : color);
            }
            catch
            {
                prop.Glow.GlowColorOverride = Color.White;
            }
            prop.Glow.GlowRange = 5000;
            prop.Glow.GlowRangeMin = 0;
            prop.Glow.GlowTeam = -1;
            prop.Glow.GlowType = 3;

            float yaw = MathF.Atan2(dirY, dirX) * 180f / MathF.PI;
            prop.Teleport(new Vector(x, y, z), new QAngle(0, yaw, 0), new Vector(0, 0, 0));

            lock (setLock)
                souls.Add(new Soul { PropIndex = prop.Index, OwnerKey = ownerKey, Team = team, X = x, Y = y, Z = z, DirX = dirX, DirY = dirY, DirZ = dirZ, LaunchTick = Server.TickCount + LaunchDelayTicks });
        }

        public static void OnTick()
        {
            lock (setLock)
            {
                if (souls.Count == 0) return;

                float speed = Math.Max(50f, SkillsInfo.GetValue<float>(skillName, "speed"));
                float maxDistance = Math.Max(100f, SkillsInfo.GetValue<float>(skillName, "maxDistance"));
                float step = speed / 64f;
                int tick = Server.TickCount;
                var alive = PlayerManager.GetAliveSnapshot();

                for (int i = souls.Count - 1; i >= 0; i--)
                {
                    var soul = souls[i];
                    var prop = Utilities.GetEntityFromIndex<CDynamicProp>((int)soul.PropIndex);
                    if (prop == null || !prop.IsValid)
                    {
                        souls.RemoveAt(i);
                        continue;
                    }

                    if (tick < soul.LaunchTick) continue;

                    Vector from = new(soul.X, soul.Y, soul.Z + CoreHeight);
                    Vector to = new(soul.X + soul.DirX * step, soul.Y + soul.DirY * step, soul.Z + CoreHeight + soul.DirZ * step);

                    var trace = Trace.TraceHullShape(from, to, new Vector(-8, -8, -8), new Vector(8, 8, 8), prop, new TraceOptions
                    {
                        InteractsWith = Contents.Solid,
                    });

                    bool hitWorld = trace.Fraction < 1f && Blocks(trace.HitEntity());
                    float nextX = hitWorld ? trace.EndPos.X : to.X;
                    float nextY = hitWorld ? trace.EndPos.Y : to.Y;
                    float nextZ = (hitWorld ? trace.EndPos.Z : to.Z) - CoreHeight;

                    bool hitEnemy = false;
                    foreach (var target in alive)
                    {
                        if ((byte)target.Team == soul.Team) continue;

                        float ex = target.X - nextX, ey = target.Y - nextY, ez = target.Z - nextZ;
                        if (ex * ex + ey * ey <= HitRadius * HitRadius && MathF.Abs(ez) < 64f)
                        {
                            hitEnemy = true;
                            break;
                        }
                    }

                    soul.X = nextX;
                    soul.Y = nextY;
                    soul.Z = nextZ;
                    soul.Travelled += step;

                    if (hitWorld || hitEnemy || soul.Travelled >= maxDistance)
                    {
                        souls.RemoveAt(i);
                        Explode(soul);
                        EntityManager.DestroyEntity(soul.PropIndex, 0f);
                        continue;
                    }

                    prop.Teleport(new Vector(nextX, nextY, nextZ));
                }
            }
        }

        private static bool Blocks(CEntityInstance? entity)
        {
            if (entity == null || !entity.IsValid) return true;

            string name = entity.DesignerName;
            return !name.StartsWith("weapon_") && !name.StartsWith("item_") && !name.EndsWith("_projectile");
        }

        private static void Explode(Soul soul)
        {
            var owner = PlayerManager.GetController(soul.OwnerKey);
            var ownerPawn = owner?.PlayerPawn?.Value;
            if (ownerPawn != null && !ownerPawn.IsValid) ownerPawn = null;

            var grenade = SkillUtils.SpawnGrenade(SkillUtils.GrenadeKind.HE, new Vector(soul.X, soul.Y, soul.Z + CoreHeight), new Vector(0, 0, 0), ownerPawn, soul.Team);
            if (grenade == null || !grenade.IsValid) return;

            grenade.Damage = SkillsInfo.GetValue<int>(skillName, "explosionDamage");
            grenade.DmgRadius = SkillsInfo.GetValue<float>(skillName, "explosionRadius");
            grenade.DetonateTime = 0;
            if (grenade.Entity != null)
                grenade.Entity.Name = $"{nadePrefix}{soul.Team}_{soul.OwnerKey}";
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null) return;

            var nade = damageInfo.Inflictor.Value;
            if (nade == null || !nade.IsValid || nade.DesignerName != "hegrenade_projectile") return;

            string? name = nade.Entity?.Name;
            if (string.IsNullOrEmpty(name) || !name.StartsWith(nadePrefix)) return;

            var parts = name[nadePrefix.Length..].Split('_');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int nadeTeam) || !uint.TryParse(parts[1], out uint ownerKey)) return;

            if (damagedEntity.DesignerName != "player") return;
            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);

            var victim = victimPawn.Controller?.Value?.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid) return;

            if (victim.Key() == ownerKey)
            {
                damageInfo.Damage = 0;
                return;
            }

            if (victimPawn.TeamNum == nadeTeam)
            {
                if (!SkillsInfo.GetValue<bool>(skillName, "friendlyFire"))
                {
                    damageInfo.Damage = 0;
                    return;
                }

                damageInfo.Damage *= SkillUtils.GetTeamDamageMultiplier(skillName);
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e8e8ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, float speed = 900f, float maxDistance = 2500f, float explosionRadius = 300f, int explosionDamage = 60, float dmgReductionForTeamates = .5f, bool friendlyFire = true, string soulColor = "#ffffff") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Speed { get; set; } = speed;
            public float MaxDistance { get; set; } = maxDistance;
            public float ExplosionRadius { get; set; } = explosionRadius;
            public int ExplosionDamage { get; set; } = explosionDamage;
            public float DmgReductionForTeamates { get; set; } = dmgReductionForTeamates;
            public bool FriendlyFire { get; set; } = friendlyFire;
            public string SoulColor { get; set; } = soulColor;
        }
    }
}
