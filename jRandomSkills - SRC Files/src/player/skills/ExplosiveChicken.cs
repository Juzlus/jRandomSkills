using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;

namespace src.player.skills
{
    public class ExplosiveChicken : ISkill
    {
        private const Skills skillName = Skills.ExplosiveChicken;
        private const string nadePrefix = "exchicken_";
        private const string chickenParticle = "particles/critters/chicken/chicken_goop.vpcf";
        private static readonly QAngle angle = new(20, -15, 43);

        private class PlayerSkillInfo
        {
            public uint? ChickenIndex { get; set; }
            public Vector? LastOrigin { get; set; }
            public DateTime SpawnTime { get; set; }
            public DateTime Cooldown { get; set; }
            public DateTime InfoMessageTime { get; set; }
            public int InfoMessageType { get; set; }
        }

        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly ConcurrentDictionary<uint, uint> chickenOwners = [];
        private static readonly ConcurrentDictionary<int, (byte Team, uint Owner)> nades = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            jRandomSkills.Instance.AddToManifest(chickenParticle);
        }

        public static void NewRound()
        {
            SkillPlayerInfo.Clear();
            chickenOwners.Clear();
            nades.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            SkillPlayerInfo[player.Index] = new PlayerSkillInfo
            {
                Cooldown = DateTime.MinValue,
                InfoMessageTime = DateTime.MinValue,
            };
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            if (SkillPlayerInfo.TryRemove(player.Index, out var skillInfo))
                RemoveChicken(skillInfo);

            SkillUtils.ResetPrintHTML(player);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) return;

            if (skillInfo.ChickenIndex != null)
            {
                SetInfoMessage(skillInfo, 1);
                return;
            }

            if (GetCooldown(skillInfo) > 0) return;
            SpawnChicken(player, skillInfo);
        }

        private static void SpawnChicken(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.EyeAngles == null || pawn.Health <= 0) return;

            Vector pawnPos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + 10);
            Vector forward = SkillUtils.GetForwardVector(new QAngle(0, pawn.EyeAngles.Y, 0));
            Vector chickenPos = new(pawnPos.X + forward.X * 50, pawnPos.Y + forward.Y * 50, pawnPos.Z);

            var trace = RayTrace.TraceShape(player, pawnPos, chickenPos);
            if (trace != null && trace.Value.DidHit)
            {
                SetInfoMessage(skillInfo, 2);
                return;
            }

            CChicken? chicken = EntityManager.CreateTrackedChicken(player.Index);
            if (chicken == null || !chicken.IsValid) return;

            int health = SkillsInfo.GetValue<int>(skillName, "chickenHealth");
            chicken.Render = Color.Red;
            Utilities.SetStateChanged(chicken, "CBaseModelEntity", "m_clrRender");
            chicken.MaxHealth = health;
            chicken.Health = health;
            chicken.Teleport(chickenPos);

            chickenOwners[chicken.Index] = player.Index;
            skillInfo.ChickenIndex = chicken.Index;
            skillInfo.LastOrigin = null;
            skillInfo.SpawnTime = DateTime.Now;
            skillInfo.Cooldown = DateTime.Now;
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null) return;
            HandleChickenHit(damagedEntity, damageInfo);
            HandleExplosionHit(damagedEntity, damageInfo);
        }

        private static void HandleChickenHit(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (!chickenOwners.TryGetValue(damagedEntity.Index, out var ownerIndex)) return;

            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex);
            if (owner == null || !owner.IsValid) return;

            var attackerEnt = damageInfo.Attacker?.Value;
            if (attackerEnt == null || !attackerEnt.IsValid || attackerEnt.DesignerName != "player") return;

            // Teammates (and the owner) can't pop their own chicken.
            if (attackerEnt.TeamNum == owner.TeamNum)
            {
                damageInfo.Damage = 0;
                return;
            }

            CreateHitParticles(damagedEntity);
        }

        private static void HandleExplosionHit(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            var nade = damageInfo.Attacker?.Value;
            if (nade == null || !nade.IsValid || nade.DesignerName != "hegrenade_projectile") return;
            if (string.IsNullOrEmpty(nade.Globalname) || !nade.Globalname.StartsWith(nadePrefix)) return;

            var parts = nade.Globalname[nadePrefix.Length..].Split('_');
            if (parts.Length < 2) return;
            if (!int.TryParse(parts[0], out int nadeTeam)) return;
            if (!uint.TryParse(parts[1], out uint ownerIndex)) return;

            if (damagedEntity.DesignerName != "player") return;
            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);
            if (victimPawn.Controller?.Value == null) return;

            var victim = victimPawn.Controller.Value.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid) return;

            if (victim.Index == ownerIndex || victimPawn.TeamNum == nadeTeam)
            {
                damageInfo.Damage *= SkillUtils.GetTeamDamageMultiplier(skillName);
                return;
            }

            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex);
            if (owner != null && owner.IsValid && SkillUtils.IsPredictedLethal(damageInfo, victimPawn))
                SkillUtils.RegisterKillCredit(victim.Index, owner.Index, KillfeedIcons.Explosion);
        }

        private static void CreateHitParticles(CBaseEntity chicken)
        {
            if (chicken.AbsOrigin == null) return;

            var particle = EntityManager.CreateTrackedParticleSystem(chicken.Index, chickenParticle, autoDestroySeconds: 3f);
            if (particle == null) return;

            particle.Teleport(new Vector(chicken.AbsOrigin.X, chicken.AbsOrigin.Y, chicken.AbsOrigin.Z + 10));
            particle.AcceptInput("Start");
        }

        public static void OnTick()
        {
            if (SkillPlayerInfo.IsEmpty) return;

            int tickCooldown = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "tickCooldown"));
            bool processMovement = Server.TickCount % tickCooldown == 0;
            bool hudFrame = SkillUtils.IsHudFrame();

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;
                if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo?.Skill != skillName) continue;

                if (hudFrame)
                    UpdateHUD(player, skillInfo);

                if (skillInfo.ChickenIndex == null || !processMovement) continue;

                var chicken = Utilities.GetEntityFromIndex<CChicken>((int)skillInfo.ChickenIndex.Value);
                if (chicken == null || !chicken.IsValid || chicken.AbsOrigin == null || chicken.Health <= 0)
                {
                    // Shot down by the enemy: the chicken is gone without exploding.
                    chickenOwners.TryRemove(skillInfo.ChickenIndex.Value, out _);
                    skillInfo.ChickenIndex = null;
                    skillInfo.LastOrigin = null;
                    skillInfo.Cooldown = DateTime.Now;
                    continue;
                }

                if ((DateTime.Now - skillInfo.SpawnTime).TotalSeconds >= SkillsInfo.GetValue<float>(skillName, "fuseTime"))
                {
                    ExplodeChicken(player, chicken, skillInfo);
                    continue;
                }

                Vector currentOrigin = new(chicken.AbsOrigin.X, chicken.AbsOrigin.Y, chicken.AbsOrigin.Z);
                var enemyPawn = GetClosestEnemy(currentOrigin, player.TeamNum);
                if (enemyPawn == null || enemyPawn.AbsOrigin == null) continue;

                if (chicken.Leader.Raw != enemyPawn.EntityHandle.Raw)
                    chicken.Leader.Raw = enemyPawn.EntityHandle.Raw;

                BoostChicken(chicken, currentOrigin, skillInfo);

                if (skillInfo.LastOrigin != null && SkillUtils.Distance(skillInfo.LastOrigin, enemyPawn.AbsOrigin) <= SkillsInfo.GetValue<float>(skillName, "triggerRadius"))
                    ExplodeChicken(player, chicken, skillInfo);
            }
        }

        private static void BoostChicken(CChicken chicken, Vector currentOrigin, PlayerSkillInfo skillInfo)
        {
            float boostFactor = SkillsInfo.GetValue<float>(skillName, "boostFactor");

            if (skillInfo.LastOrigin != null)
            {
                float dx = currentOrigin.X - skillInfo.LastOrigin.X;
                float dy = currentOrigin.Y - skillInfo.LastOrigin.Y;
                float dist2D = MathF.Sqrt(dx * dx + dy * dy);

                if (dist2D > 0.05f && dist2D < 20.0f)
                {
                    Vector newPos = new(currentOrigin.X + (dx * boostFactor), currentOrigin.Y + (dy * boostFactor), currentOrigin.Z);
                    chicken.Teleport(newPos, chicken.AbsRotation, chicken.AbsVelocity);
                    skillInfo.LastOrigin = newPos;
                    return;
                }
            }

            skillInfo.LastOrigin = currentOrigin;
        }

        private static CCSPlayerPawn? GetClosestEnemy(Vector chickenPos, byte ownerTeam)
        {
            CCSPlayerPawn? closestEnemy = null;
            float closestDistance = float.MaxValue;

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid || player.TeamNum == ownerTeam) continue;
                if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) continue;

                var pawn = player.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.Health <= 0) continue;

                float distance = SkillUtils.Distance(chickenPos, pawn.AbsOrigin);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestEnemy = pawn;
                }
            }
            return closestEnemy;
        }

        private static void ExplodeChicken(CCSPlayerController owner, CChicken chicken, PlayerSkillInfo skillInfo)
        {
            if (chicken.AbsOrigin == null) return;
            Vector pos = new(chicken.AbsOrigin.X, chicken.AbsOrigin.Y, chicken.AbsOrigin.Z + 10);
            byte team = owner.TeamNum;
            uint ownerIndex = owner.Index;

            RemoveChicken(skillInfo);
            skillInfo.Cooldown = DateTime.Now;

            nades[Server.TickCount] = (team, ownerIndex);
            SkillUtils.CreateHEGrenadeProjectile(pos, angle, new Vector(0, 0, -10), team);
        }

        private static void RemoveChicken(PlayerSkillInfo skillInfo)
        {
            if (skillInfo.ChickenIndex is not uint chickenIndex) return;

            chickenOwners.TryRemove(chickenIndex, out _);
            skillInfo.ChickenIndex = null;
            skillInfo.LastOrigin = null;
            EntityManager.DestroyEntity(chickenIndex, 0f);
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            if (entity.DesignerName != "hegrenade_projectile") return;

            var heProjectile = entity.As<CBaseCSGrenadeProjectile>();
            if (heProjectile == null || !heProjectile.IsValid) return;

            int spawnTick = Server.TickCount;

            Server.NextFrame(() =>
            {
                if (heProjectile == null || !heProjectile.IsValid || heProjectile.AbsRotation == null) return;
                if (!(NearlyEquals(angle.X, heProjectile.AbsRotation.X) && NearlyEquals(angle.Y, heProjectile.AbsRotation.Y) && NearlyEquals(angle.Z, heProjectile.AbsRotation.Z)))
                    return;

                heProjectile.TicksAtZeroVelocity = 100;
                heProjectile.Damage = SkillsInfo.GetValue<float>(skillName, "explosionDamage");
                heProjectile.DmgRadius = SkillsInfo.GetValue<float>(skillName, "explosionRadius");
                heProjectile.DetonateTime = 0;

                if (nades.TryRemove(spawnTick, out var source))
                    heProjectile.Globalname = $"{nadePrefix}{source.Team}_{source.Owner}_{heProjectile.Index}";
            });
        }

        private static float GetCooldown(PlayerSkillInfo skillInfo)
        {
            if (skillInfo.ChickenIndex != null) return 0;
            float time = (float)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
            return Math.Max(time, 0);
        }

        private static void SetInfoMessage(PlayerSkillInfo skillInfo, int type)
        {
            skillInfo.InfoMessageTime = DateTime.Now;
            skillInfo.InfoMessageType = type;
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null) return;

            if ((DateTime.Now - skillInfo.InfoMessageTime).TotalSeconds < 2)
            {
                playerInfo.PrintHTML = skillInfo.InfoMessageType switch
                {
                    1 => player.GetTranslation("explosivechicken_already_active"),
                    2 => player.GetTranslation("explosivechicken_blocked"),
                    _ => null
                };
                return;
            }

            if (skillInfo.ChickenIndex != null)
            {
                var chicken = Utilities.GetEntityFromIndex<CChicken>((int)skillInfo.ChickenIndex.Value);
                int health = chicken != null && chicken.IsValid ? Math.Max(chicken.Health, 0) : 0;
                playerInfo.PrintHTML = player.GetTranslation("explosivechicken_hud_active", $"<font color='#00FF00'>{health}</font>");
                return;
            }

            float cooldown = GetCooldown(skillInfo);
            playerInfo.PrintHTML = cooldown > 0
                ? player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")
                : null;
        }

        private static bool NearlyEquals(float a, float b, float epsilon = 0.001f) => Math.Abs(a - b) < epsilon;

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e0452b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Legendary, float cooldown = 20f, float fuseTime = 12f, float triggerRadius = 120f, int chickenHealth = 60, int tickCooldown = 16, float boostFactor = 2.5f, float explosionDamage = 150f, float explosionRadius = 300f, float dmgReductionForTeamates = 1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float FuseTime { get; set; } = fuseTime;
            public float TriggerRadius { get; set; } = triggerRadius;
            public int ChickenHealth { get; set; } = chickenHealth;
            public int TickCooldown { get; set; } = tickCooldown;
            public float BoostFactor { get; set; } = boostFactor;
            public float ExplosionDamage { get; set; } = explosionDamage;
            public float ExplosionRadius { get; set; } = explosionRadius;
            public float DmgReductionForTeamates { get; set; } = dmgReductionForTeamates;
        }
    }
}
