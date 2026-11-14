using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Globalization;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class Airstrike : ISkill
    {
        private const Skills skillName = Skills.Airstrike;

        private const string missileModel = "models/generic/missile_01/missile_01.vmdl";
        private const string trailParticle = "particles/weapons/cs_weapon_fx/weapon_molotov_thrown.vpcf";

        private const float modelPitchOffset = 0f;
        private const float modelYawOffset = 0f;
        private const float modelRollOffset = 0f;

        private const float tickDt = 1f / 64f;
        private const float maxCameraRange = 3000f;
        private const float markerMoveThreshold = 32f;
        private const int markerRefreshTicks = 16;
        private const float groundTraceDepth = 6000f;

        private const float skyHeight = 1000f;
        private const float approachDistance = 1000f;
        private static readonly float[] skyScales = [1f, .65f, .4f];
        private const float skyJitter = 80f;
        private const int missileIntervalTicks = 16;
        private const float missileTrailSeconds = 10f;

        private const int weaponResumeTicks = 24;
        private const int confirmGuardTicks = 16;

        private const int nadeTrackTicks = 48;
        private const int infernoTrackTicks = 640;
        private const int impactTrackTicks = 96;
        private const float infernoMatchRadius = 220f;

        private static readonly Vector traceMins = new(-3f, -3f, -3f);
        private static readonly Vector traceMaxs = new(3f, 3f, 3f);

        private sealed class Aim
        {
            public uint Key;
            public int EndTick;
            public float CamX, CamY, CamZ;
            public uint CameraIndex;
            public uint CameraRaw;
            public uint OriginalView;
            public int StartTick;
            public int NextBlockTick;
            public bool HasTarget;
            public float TargetX, TargetY, TargetZ;
            public bool MarkerShown;
            public float MarkerX, MarkerY, MarkerZ;
            public int NextMarkerTick;
            public float CameraSpeed;
            public float Cooldown;
        }

        private sealed class Cd
        {
            public int NextUse;
        }

        private sealed class Strike
        {
            public uint Key;
            public byte Team;
            public float SkyX, SkyY, SkyZ;
            public float TargetX, TargetY, TargetZ;
            public int Remaining;
            public int NextTick;
            public float Spread;
            public float Speed;
            public float Scale;
        }

        private sealed class Missile
        {
            public uint Prop;
            public uint PropRaw;
            public uint Particle;
            public uint ParticleRaw;
            public uint Key;
            public byte Team;
            public float X, Y, Z;
            public float DirX, DirY, DirZ;
            public float ImpactX, ImpactY, ImpactZ;
            public float Remaining;
            public float Speed;
        }

        private sealed class Impact
        {
            public float X, Y, Z;
            public uint ThrowerRaw;
            public byte Team;
            public int Expiry;
        }

        private static readonly List<Aim> aims = [];
        private static readonly Dictionary<uint, Cd> cooldowns = [];
        private static readonly List<Strike> strikes = [];
        private static readonly List<Missile> missiles = [];
        private static readonly List<Impact> impacts = [];
        private static readonly Dictionary<uint, (uint ThrowerRaw, byte Team, int Expiry)> nades = [];
        private static readonly Dictionary<uint, int> infernos = [];
        private static readonly List<uint> pruneBuffer = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            jRandomSkills.Instance.AddToManifest(missileModel);
            jRandomSkills.Instance.AddToManifest(trailParticle);
            DecoyRing.PreloadAssets();
        }

        public static void NewRound()
        {
            for (int i = aims.Count - 1; i >= 0; i--)
                ExitAim(aims[i], false);

            foreach (var missile in missiles)
                DestroyMissileEntities(missile);

            missiles.Clear();
            strikes.Clear();
            cooldowns.Clear();
            impacts.Clear();
            nades.Clear();
            infernos.Clear();
            DecoyRing.ClearAll(skillName);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var aim = FindAim(player.Key());
            if (aim != null)
                ExitAim(aim, false);
        }

        public static void PlayerDisconnect(uint playerKey)
        {
            var aim = FindAim(playerKey);
            if (aim != null)
                ExitAim(aim, false);

            cooldowns.Remove(playerKey);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;

            uint key = player.Key();
            var aim = FindAim(key);
            if (aim != null)
                ExitAim(aim, false);

            for (int i = strikes.Count - 1; i >= 0; i--)
                if (strikes[i].Key == key) strikes.RemoveAt(i);

            for (int i = missiles.Count - 1; i >= 0; i--)
            {
                if (missiles[i].Key != key) continue;
                DestroyMissileEntities(missiles[i]);
                missiles.RemoveAt(i);
            }

            cooldowns.Remove(key);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.CameraServices == null) return;

            uint key = player.Key();
            int tick = Server.TickCount;

            var activeAim = FindAim(key);
            if (activeAim != null)
            {
                if (tick - activeAim.StartTick >= confirmGuardTicks)
                    FinishAim(activeAim, player, pawn);
                return;
            }
            if (cooldowns.TryGetValue(key, out var cd) && tick < cd.NextUse) return;

            float height = SkillsInfo.GetValue<float>(skillName, "cameraHeight");
            float yaw = pawn.V_angle.Y;
            Vector pos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + height);

            uint originalView = pawn.CameraServices.ViewEntity.Raw;
            var camera = PlayerCamera.Create(key, pawn, CustomCameraMode_t.CUSTOM_CAMERA_MODE_CONTROLLED, pos, new QAngle(90f, yaw, 0f));
            if (camera == null || !camera.IsValid) return;

            pawn.CameraServices.ViewEntity.Raw = camera.EntityHandle.Raw;
            NetStateFast.Changed(pawn, "CBasePlayerPawn", "m_pCameraServices");
            BlockWeapon(player, true);

            aims.Add(new Aim
            {
                Key = key,
                EndTick = tick + (int)MathF.Ceiling(SkillsInfo.GetValue<float>(skillName, "aimSeconds") * 64f),
                CamX = pos.X,
                CamY = pos.Y,
                CamZ = pos.Z,
                CameraIndex = camera.Index,
                CameraRaw = camera.EntityHandle.Raw,
                OriginalView = originalView,
                StartTick = tick,
                NextBlockTick = tick + 16,
                CameraSpeed = SkillsInfo.GetValue<float>(skillName, "cameraSpeed"),
                Cooldown = SkillsInfo.GetValue<float>(skillName, "cooldown"),
            });
        }

        public static void OnTick()
        {
            if (aims.Count == 0 && missiles.Count == 0 && strikes.Count == 0 && cooldowns.Count == 0 && nades.Count == 0 && infernos.Count == 0 && impacts.Count == 0)
                return;

            int tick = Server.TickCount;

            for (int i = aims.Count - 1; i >= 0; i--)
                TickAim(aims[i], tick);

            if (strikes.Count > 0)
                TickStrikes(tick);

            if (missiles.Count > 0)
                TickMissiles();

            if (cooldowns.Count > 0 && SkillUtils.IsHudFrame())
                TickCooldownHud(tick);

            if (tick % 64 == 0)
                Prune(tick);
        }

        private static Aim? FindAim(uint key)
        {
            for (int i = 0; i < aims.Count; i++)
                if (aims[i].Key == key) return aims[i];
            return null;
        }

        private static void TickAim(Aim aim, int tick)
        {
            var player = PlayerManager.GetController(aim.Key);
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            {
                ExitAim(aim, false);
                return;
            }

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            {
                ExitAim(aim, false);
                return;
            }

            var camera = Utilities.GetEntityFromIndex<CCSCustomPlayerCamera>((int)aim.CameraIndex);
            if (camera == null || !camera.IsValid || camera.EntityHandle.Raw != aim.CameraRaw)
            {
                ExitAim(aim, false);
                return;
            }

            var buttons = player.Buttons;

            if (tick >= aim.EndTick)
            {
                FinishAim(aim, player, pawn);
                return;
            }

            float yaw = pawn.V_angle.Y;
            float yawRad = yaw * (MathF.PI / 180f);
            float fx = MathF.Cos(yawRad), fy = MathF.Sin(yawRad);
            float rx = fy, ry = -fx;

            float forward = ((buttons & PlayerButtons.Forward) != 0 ? 1f : 0f) - ((buttons & PlayerButtons.Back) != 0 ? 1f : 0f);
            float right = ((buttons & PlayerButtons.Moveright) != 0 ? 1f : 0f) - ((buttons & PlayerButtons.Moveleft) != 0 ? 1f : 0f);

            if (forward != 0f || right != 0f)
            {
                float mx = fx * forward + rx * right;
                float my = fy * forward + ry * right;
                float len = MathF.Sqrt(mx * mx + my * my);
                if (len > 0.001f)
                {
                    float step = aim.CameraSpeed * tickDt / len;
                    aim.CamX += mx * step;
                    aim.CamY += my * step;
                }
            }

            float dx = aim.CamX - pawn.AbsOrigin.X;
            float dy = aim.CamY - pawn.AbsOrigin.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > maxCameraRange)
            {
                float scale = maxCameraRange / dist;
                aim.CamX = pawn.AbsOrigin.X + dx * scale;
                aim.CamY = pawn.AbsOrigin.Y + dy * scale;
            }

            camera.Teleport(new Vector(aim.CamX, aim.CamY, aim.CamZ), new QAngle(90f, yaw, 0f), null);

            FreezePawn(pawn);

            if (tick >= aim.NextBlockTick)
            {
                aim.NextBlockTick = tick + 16;
                BlockWeapon(player, true);
            }

            UpdateTarget(aim, pawn, tick);

            if (SkillUtils.IsHudFrame())
            {
                int secondsLeft = (int)MathF.Ceiling(Math.Max(aim.EndTick - tick, 0) / 64f);
                SetHud(player, "airstrike_hud", secondsLeft);
            }
        }

        private static void FinishAim(Aim aim, CCSPlayerController player, CCSPlayerPawn pawn)
        {
            if (!aim.HasTarget)
            {
                ExitAim(aim, false);
                return;
            }

            Vector target = new(aim.TargetX, aim.TargetY, aim.TargetZ);
            byte team = player.TeamNum;
            ExitAim(aim, true);
            LaunchStrike(player.Key(), team, pawn, target);
        }

        private static void SetHud(CCSPlayerController player, string key, int seconds)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;
            playerInfo.PrintHTML = player.GetTranslation(key, $"<font color='#FF0000'>{seconds}</font>");
        }

        private static void UpdateTarget(Aim aim, CCSPlayerPawn pawn, int tick)
        {
            Vector from = new(aim.CamX, aim.CamY, aim.CamZ);
            Vector to = new(aim.CamX, aim.CamY, aim.CamZ - groundTraceDepth);

            if (!TraceSolid(from, to, pawn, out var trace) || trace.Fraction >= 1f)
            {
                aim.HasTarget = false;
                return;
            }

            if (trace.IsAllSolid || trace.Fraction <= 0f)
            {
                aim.HasTarget = false;
                return;
            }

            aim.HasTarget = true;
            aim.TargetX = trace.EndPos.X;
            aim.TargetY = trace.EndPos.Y;
            aim.TargetZ = trace.EndPos.Z;

            if (tick < aim.NextMarkerTick) return;

            if (aim.MarkerShown)
            {
                float mx = aim.TargetX - aim.MarkerX, my = aim.TargetY - aim.MarkerY, mz = aim.TargetZ - aim.MarkerZ;
                if (mx * mx + my * my + mz * mz < markerMoveThreshold * markerMoveThreshold) return;
                DecoyRing.Hide(skillName, aim.Key);
            }

            DecoyRing.Show(skillName, aim.Key, new Vector(aim.TargetX, aim.TargetY, aim.TargetZ), SkillsInfo.GetValue<float>(skillName, "missileSpread") + SkillsInfo.GetValue<float>(skillName, "explosionRadius"));
            aim.MarkerShown = true;
            aim.MarkerX = aim.TargetX;
            aim.MarkerY = aim.TargetY;
            aim.MarkerZ = aim.TargetZ;
            aim.NextMarkerTick = tick + markerRefreshTicks;
        }

        private static void FreezePawn(CCSPlayerPawn pawn)
        {
            pawn.VelocityModifier = 0f;
            var velocity = pawn.AbsVelocity;
            velocity.X = 0f;
            velocity.Y = 0f;
            if (((PlayerFlags)pawn.Flags & PlayerFlags.FL_ONGROUND) != 0)
                velocity.Z = 0f;
        }

        private static void ExitAim(Aim aim, bool startCooldown)
        {
            aims.Remove(aim);

            var player = PlayerManager.GetController(aim.Key);
            if (player != null && player.IsValid)
            {
                var pawn = player.PlayerPawn.Value;
                if (pawn != null && pawn.IsValid)
                {
                    if (pawn.CameraServices != null)
                    {
                        pawn.CameraServices.ViewEntity.Raw = aim.OriginalView;
                        NetStateFast.Changed(pawn, "CBasePlayerPawn", "m_pCameraServices");
                    }
                    pawn.VelocityModifier = 1f;
                }

                BlockWeapon(player, false, Server.TickCount + weaponResumeTicks);
                SkillUtils.ResetPrintHTML(player);
            }

            DestroyChecked(aim.CameraIndex, aim.CameraRaw);
            DecoyRing.Hide(skillName, aim.Key);

            if (startCooldown)
            {
                int start = Server.TickCount + (int)MathF.Ceiling(aim.Cooldown * 64f);
                cooldowns[aim.Key] = new Cd { NextUse = start };
            }
        }

        private static void BlockWeapon(CCSPlayerController player, bool block, int resumeTick = 0)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null) return;

            foreach (var weapon in weaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
                {
                    weapon.Value.NextPrimaryAttackTick = block ? int.MaxValue : Math.Max(resumeTick, Server.TickCount);
                    weapon.Value.NextSecondaryAttackTick = block ? int.MaxValue : Math.Max(resumeTick, Server.TickCount);

                    NetStateFast.Changed(weapon.Value, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                    NetStateFast.Changed(weapon.Value, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
                }
        }

        private static void TickCooldownHud(int tick)
        {
            pruneBuffer.Clear();

            foreach (var (key, cd) in cooldowns)
            {
                var player = PlayerManager.GetController(key);
                if (player == null || !player.IsValid)
                {
                    pruneBuffer.Add(key);
                    continue;
                }

                if (tick >= cd.NextUse)
                {
                    pruneBuffer.Add(key);
                    SkillUtils.ResetPrintHTML(player);
                    continue;
                }

                if (player.LifeState != (byte)LifeState_t.LIFE_ALIVE || FindAim(key) != null) continue;

                SetHud(player, "hud_info", (int)MathF.Ceiling((cd.NextUse - tick) / 64f));
            }

            foreach (var key in pruneBuffer)
                cooldowns.Remove(key);
        }

        private static void LaunchStrike(uint key, byte team, CCSPlayerPawn ownerPawn, Vector target)
        {
            Vector sky = FindSkyPoint(ownerPawn, target);

            strikes.Add(new Strike
            {
                Key = key,
                Team = team,
                SkyX = sky.X,
                SkyY = sky.Y,
                SkyZ = sky.Z,
                TargetX = target.X,
                TargetY = target.Y,
                TargetZ = target.Z,
                Remaining = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "missileCount")),
                NextTick = Server.TickCount + 1,
                Spread = SkillsInfo.GetValue<float>(skillName, "missileSpread"),
                Speed = Math.Max(100f, SkillsInfo.GetValue<float>(skillName, "missileSpeed")),
                Scale = SkillsInfo.GetValue<float>(skillName, "modelScale"),
            });
        }

        private static Vector FindSkyPoint(CCSPlayerPawn ownerPawn, Vector target)
        {
            Vector start = new(target.X, target.Y, target.Z + 16f);
            float startAngle = Random.Shared.NextSingle() * MathF.Tau;

            foreach (float scale in skyScales)
                for (int i = 0; i < 8; i++)
                {
                    float angle = startAngle + i * (MathF.Tau / 8f);
                    Vector candidate = new(
                        target.X + MathF.Cos(angle) * approachDistance * scale,
                        target.Y + MathF.Sin(angle) * approachDistance * scale,
                        target.Z + skyHeight * scale);

                    if (TraceSolid(start, candidate, ownerPawn, out var trace) && trace.Fraction < 1f)
                        continue;

                    return candidate;
                }

            Vector top = new(target.X, target.Y, target.Z + skyHeight);
            if (TraceSolid(start, top, ownerPawn, out var up) && up.Fraction < 1f)
                return new Vector(target.X, target.Y, Math.Max(up.EndPos.Z - 40f, target.Z + 100f));

            return top;
        }

        private static void TickStrikes(int tick)
        {
            for (int i = strikes.Count - 1; i >= 0; i--)
            {
                var strike = strikes[i];
                if (tick < strike.NextTick) continue;

                SpawnMissile(strike);
                strike.Remaining--;
                strike.NextTick = tick + missileIntervalTicks;

                if (strike.Remaining <= 0)
                    strikes.RemoveAt(i);
            }
        }

        private static void SpawnMissile(Strike strike)
        {
            float angle = Random.Shared.NextSingle() * MathF.Tau;
            float radius = MathF.Sqrt(Random.Shared.NextSingle()) * strike.Spread;

            float ix = strike.TargetX + MathF.Cos(angle) * radius;
            float iy = strike.TargetY + MathF.Sin(angle) * radius;
            float iz = strike.TargetZ;

            if (TraceSolid(new Vector(ix, iy, strike.TargetZ + 200f), new Vector(ix, iy, strike.TargetZ - 400f), null, out var ground) && ground.Fraction < 1f)
                iz = ground.EndPos.Z;

            float sx = strike.SkyX + (Random.Shared.NextSingle() * 2f - 1f) * skyJitter;
            float sy = strike.SkyY + (Random.Shared.NextSingle() * 2f - 1f) * skyJitter;
            float sz = strike.SkyZ;

            float dx = ix - sx, dy = iy - sy, dz = iz - sz;
            float length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 1f) return;

            dx /= length;
            dy /= length;
            dz /= length;

            var missile = new Missile
            {
                Key = strike.Key,
                Team = strike.Team,
                X = sx,
                Y = sy,
                Z = sz,
                DirX = dx,
                DirY = dy,
                DirZ = dz,
                ImpactX = ix,
                ImpactY = iy,
                ImpactZ = iz,
                Remaining = length,
                Speed = strike.Speed,
            };

            float pitch = -MathF.Asin(Math.Clamp(dz, -1f, 1f)) * 180f / MathF.PI + modelPitchOffset;
            float yaw = MathF.Atan2(dy, dx) * 180f / MathF.PI + modelYawOffset;
            QAngle rotation = new(pitch, yaw, modelRollOffset);
            Vector spawn = new(sx, sy, sz);

            var prop = EntityManager.CreateTrackedDynamicProp(EntityManager.SystemOwnerIndex, "prop_dynamic", "airstrike_missile");
            if (prop == null || !prop.IsValid)
            {
                Detonate(missile, ix, iy, iz + 16f, true);
                return;
            }

            prop.AnimGraphUpdateEnabled = false;
            using (var keys = new CEntityKeyValues())
            {
                keys.SetString("model", missileModel);
                keys.SetInt("spawnflags", 256);
                keys.SetVector("origin", spawn);
                keys.SetAngle("angles", rotation);
                prop.DispatchSpawn(keys);
            }

            if (!prop.IsValid)
            {
                Detonate(missile, ix, iy, iz + 16f, true);
                return;
            }

            prop.AcceptInput("DisableCollision");
            prop.Teleport(spawn, rotation, new Vector(0, 0, 0));
            missile.Prop = prop.Index;
            missile.PropRaw = prop.EntityHandle.Raw;

            float scale = strike.Scale;
            uint propIndex = prop.Index;
            uint propRaw = prop.EntityHandle.Raw;
            Server.NextFrame(() =>
            {
                var p = Utilities.GetEntityFromIndex<CDynamicProp>((int)propIndex);
                if (p == null || !p.IsValid || p.EntityHandle.Raw != propRaw) return;

                var skeleton = p.CBodyComponent?.SceneNode?.GetSkeletonInstance();
                if (skeleton != null)
                {
                    skeleton.Scale = scale;
                    p.AcceptInput("SetScale", null, null, scale.ToString(CultureInfo.InvariantCulture));
                }

                NetStateFast.Changed(p, "CBaseEntity", "m_CBodyComponent");
            });

            var particle = EntityManager.CreateEntityParticle(EntityManager.SystemOwnerIndex, trailParticle, prop, missileTrailSeconds);
            if (particle != null && particle.IsValid)
            {
                missile.Particle = particle.Index;
                missile.ParticleRaw = particle.EntityHandle.Raw;
            }

            missiles.Add(missile);
        }

        private static void TickMissiles()
        {
            for (int i = missiles.Count - 1; i >= 0; i--)
            {
                var missile = missiles[i];
                var prop = Utilities.GetEntityFromIndex<CDynamicProp>((int)missile.Prop);
                if (prop == null || !prop.IsValid || prop.EntityHandle.Raw != missile.PropRaw)
                {
                    DestroyMissileEntities(missile);
                    missiles.RemoveAt(i);
                    continue;
                }

                float move = Math.Min(missile.Speed * tickDt, missile.Remaining);
                float nx = missile.X + missile.DirX * move;
                float ny = missile.Y + missile.DirY * move;
                float nz = missile.Z + missile.DirZ * move;

                bool hit = false;
                float hx = nx, hy = ny, hz = nz;
                float nxs = 0f, nys = 0f, nzs = 1f;

                if (TraceSolid(new Vector(missile.X, missile.Y, missile.Z), new Vector(nx, ny, nz), prop, out var trace)
                    && trace.Fraction < 1f && Blocks(trace.HitEntity()))
                {
                    hit = true;
                    hx = trace.EndPos.X;
                    hy = trace.EndPos.Y;
                    hz = trace.EndPos.Z;
                    nxs = trace.Normal.X;
                    nys = trace.Normal.Y;
                    nzs = trace.Normal.Z;
                }

                missile.Remaining -= move;
                bool arrived = missile.Remaining <= 0.01f;

                if (hit || arrived)
                {
                    missiles.RemoveAt(i);
                    DestroyMissileEntities(missile);

                    if (!hit)
                    {
                        hx = missile.ImpactX;
                        hy = missile.ImpactY;
                        hz = missile.ImpactZ;
                    }

                    Detonate(missile, hx + nxs * 16f, hy + nys * 16f, hz + nzs * 16f, false);
                    continue;
                }

                missile.X = nx;
                missile.Y = ny;
                missile.Z = nz;
                prop.Teleport(new Vector(nx, ny, nz), null, null);
            }
        }

        private static bool Blocks(CEntityInstance? entity)
        {
            if (entity == null || !entity.IsValid) return true;

            string name = entity.DesignerName;
            return !name.StartsWith("weapon_") && !name.StartsWith("item_") && !name.EndsWith("_projectile");
        }

        private static void DestroyMissileEntities(Missile missile)
        {
            if (missile.Particle != 0)
            {
                DestroyChecked(missile.Particle, missile.ParticleRaw);
                missile.Particle = 0;
                missile.ParticleRaw = 0;
            }

            if (missile.Prop != 0)
            {
                DestroyChecked(missile.Prop, missile.PropRaw);
                missile.Prop = 0;
                missile.PropRaw = 0;
            }
        }

        private static void DestroyChecked(uint index, uint raw)
        {
            var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)index);
            if (entity == null || !entity.IsValid || entity.EntityHandle.Raw != raw) return;
            EntityManager.DestroyEntity(index, 0f);
        }

        private static void Detonate(Missile missile, float x, float y, float z, bool unused)
        {
            var owner = PlayerManager.GetController(missile.Key);
            var ownerPawn = owner?.PlayerPawn?.Value;
            if (ownerPawn != null && !ownerPawn.IsValid) ownerPawn = null;

            uint throwerRaw = ownerPawn != null ? ownerPawn.EntityHandle.Raw : 0;
            Vector pos = new(x, y, z);
            int tick = Server.TickCount;

            var he = SkillUtils.SpawnGrenade(SkillUtils.GrenadeKind.HE, pos, new Vector(0, 0, 0), ownerPawn, missile.Team);
            if (he != null && he.IsValid)
            {
                he.Damage = SkillsInfo.GetValue<float>(skillName, "explosionDamage");
                he.DmgRadius = SkillsInfo.GetValue<float>(skillName, "explosionRadius");
                he.DetonateTime = 0;
                nades[he.Index] = (throwerRaw, missile.Team, tick + nadeTrackTicks);
            }

            var inc = SkillUtils.SpawnGrenade(SkillUtils.GrenadeKind.Incendiary, pos, new Vector(0, 0, 0), ownerPawn, missile.Team);
            if (inc != null && inc.IsValid)
            {
                inc.DetonateTime = 0;
                NetStateFast.Changed(inc, "CBaseGrenade", "m_flDetonateTime");
                nades[inc.Index] = (throwerRaw, missile.Team, tick + nadeTrackTicks);
            }

            impacts.Add(new Impact { X = x, Y = y, Z = z, ThrowerRaw = throwerRaw, Team = missile.Team, Expiry = tick + impactTrackTicks });
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            if (nades.Count == 0 && impacts.Count == 0) return;
            if (entity.DesignerName != "inferno") return;

            var inferno = entity.As<CInferno>();
            if (inferno == null || !inferno.IsValid) return;

            var ownerHandle = inferno.OwnerEntity;
            var source = ownerHandle?.Value;
            if (ownerHandle != null && source != null && source.IsValid && nades.TryGetValue(source.Index, out var origin))
            {
                inferno.TeamNum = origin.Team;
                if (origin.ThrowerRaw != 0) ownerHandle.Raw = origin.ThrowerRaw;
                infernos[inferno.Index] = Server.TickCount + infernoTrackTicks;
                return;
            }

            var pos = inferno.AbsOrigin;
            if (pos == null) return;

            int tick = Server.TickCount;
            foreach (var impact in impacts)
            {
                if (tick > impact.Expiry) continue;

                float dx = pos.X - impact.X, dy = pos.Y - impact.Y;
                if (dx * dx + dy * dy > infernoMatchRadius * infernoMatchRadius) continue;

                if (source != null && source.IsValid && source.DesignerName == "player" && impact.ThrowerRaw != 0)
                {
                    var sourcePawn = source.As<CCSPlayerPawn>();
                    if (sourcePawn != null && sourcePawn.IsValid && sourcePawn.EntityHandle.Raw != impact.ThrowerRaw) continue;
                }

                impacts.Remove(impact);
                inferno.TeamNum = impact.Team;
                if (impact.ThrowerRaw != 0 && ownerHandle != null) ownerHandle.Raw = impact.ThrowerRaw;
                infernos[inferno.Index] = tick + infernoTrackTicks;
                return;
            }
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damageInfo == null) return;
            if (nades.Count == 0 && infernos.Count == 0) return;

            var inflictor = damageInfo.Inflictor.Value;
            if (inflictor == null || !inflictor.IsValid) return;

            uint index = inflictor.Index;
            string designer = inflictor.DesignerName;

            bool ours = designer == "inferno"
                ? infernos.ContainsKey(index)
                : (designer == "hegrenade_projectile" || designer == "molotov_projectile") && nades.ContainsKey(index);

            if (!ours) return;
            if (damagedEntity.DesignerName != "player") return;

            var victimPawn = damagedEntity.As<CCSPlayerPawn>();
            if (victimPawn == null || !victimPawn.IsValid) return;

            if (SkillUtils.IsFriendlyFireBlocked(skillName, damageInfo, victimPawn))
                damageInfo.Damage = 0;
        }

        private static void Prune(int tick)
        {
            if (nades.Count > 0)
            {
                pruneBuffer.Clear();
                foreach (var (index, info) in nades)
                    if (tick > info.Expiry) pruneBuffer.Add(index);
                foreach (var index in pruneBuffer)
                    nades.Remove(index);
            }

            if (infernos.Count > 0)
            {
                pruneBuffer.Clear();
                foreach (var (index, expiry) in infernos)
                    if (tick > expiry) pruneBuffer.Add(index);
                foreach (var index in pruneBuffer)
                    infernos.Remove(index);
            }

            for (int i = impacts.Count - 1; i >= 0; i--)
                if (tick > impacts[i].Expiry) impacts.RemoveAt(i);
        }

        private static bool TraceSolid(Vector from, Vector to, CBaseEntity? ignore, out TraceResult result)
        {
            try
            {
                result = Trace.TraceHullShape(from, to, traceMins, traceMaxs, ignore, new TraceOptions
                {
                    InteractsWith = Contents.Solid,
                });
                return true;
            }
            catch
            {
                result = default;
                return false;
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff7043", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Epic, float aimSeconds = 5f, float cooldown = 30f, int missileCount = 4, float missileSpread = 150f, float explosionDamage = 80f, float explosionRadius = 200f, float missileSpeed = 700f, float modelScale = 1f, float cameraHeight = 1000f, float cameraSpeed = 700f, bool friendlyFire = false) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float AimSeconds { get; set; } = aimSeconds;
            public float Cooldown { get; set; } = cooldown;
            public int MissileCount { get; set; } = missileCount;
            public float MissileSpread { get; set; } = missileSpread;
            public float ExplosionDamage { get; set; } = explosionDamage;
            public float ExplosionRadius { get; set; } = explosionRadius;
            public float MissileSpeed { get; set; } = missileSpeed;
            public float ModelScale { get; set; } = modelScale;
            public float CameraHeight { get; set; } = cameraHeight;
            public float CameraSpeed { get; set; } = cameraSpeed;
            public bool FriendlyFire { get; set; } = friendlyFire;
        }
    }
}
