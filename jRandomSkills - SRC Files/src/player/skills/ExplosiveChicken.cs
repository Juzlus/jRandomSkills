//using CounterStrikeSharp.API;
//using CounterStrikeSharp.API.Core;
//using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
//using CounterStrikeSharp.API.Modules.Utils;
//using jRandomSkills.src.utils;
//using src.utils;
//using System.Collections.Concurrent;
//using System.Drawing;

//namespace src.player.skills
//{
//    public class ExplosiveChicken : ISkill
//    {
//        private const Skills skillName = Skills.ExplosiveChicken;

//        private class PlayerInfo
//        {
//            public uint OwnerIndex { get; set; }
//            public uint? ChickenIndex { get; set; }
//            public Vector? ChickenLastOrigin { get; set; }
//            public int ChickenHealth { get; set; }
//            public bool PlayerActive { get; set; }
//            public bool CanUse { get; set; }
//            public int Cooldown { get; set; }
//            public int InfoMessageTime { get; set; }
//            public int InfoMessageType { get; set; }
//        }

//        private static readonly ConcurrentDictionary<uint, PlayerInfo> chickens = [];
//        private static readonly ConcurrentDictionary<uint, uint> chickenOwner = [];
//        private const string chickenParticle = "particles/critters/chicken/chicken_goop.vpcf";
//        private static readonly QAngle angle = new(20, -15, 43);
//        private static readonly ConcurrentDictionary<int, (byte Team, uint Owner)> nades = [];

//        public static void LoadSkill()
//        {
//            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
//            jRandomSkills.Instance.AddToManifest(chickenParticle);
//        }

//        public static void NewRound()
//        {
//            foreach (var key in chickens.Keys)
//            {
//                if (chickens.TryRemove(key, out _))
//                    EntityManager.DestroyPlayerEntities(key);
//            }
            
//            chickens.Clear();
//            chickenOwner.Clear();
//        }

//        public static void EnableSkill(CCSPlayerController player)
//        {
//            if (player == null || !player.IsValid) return;
//            int health = SkillsInfo.GetValue<int>(skillName, "health");

//            chickens.TryAdd(player.Index, new PlayerInfo
//            {
//                OwnerIndex = player.Index,
//                ChickenIndex = null,
//                ChickenLastOrigin = null,
//                ChickenHealth = 0,
//                PlayerActive = true,
//                CanUse = true,
//                Cooldown = 0,
//                InfoMessageTime = 0,
//                InfoMessageType = 0,
//            });
//            chickens[player.Index].PlayerActive = true;
//        }

//        public static void DisableSkill(CCSPlayerController player)
//        {
//            if (player == null || !player.IsValid) return;
//            chickens[player.Index].PlayerActive = false;
//        }

//        public static void UseSkill(CCSPlayerController player)
//        {
//            var playerPawn = player.PlayerPawn.Value;
//            if (playerPawn?.CBodyComponent == null) return;

//            if (chickens.TryGetValue(player.Index, out var skillInfo))
//            {
//                if (!player.IsValid || playerPawn.Health <= 0) return;
                
//                if (skillInfo.CanUse && skillInfo.PlayerActive)
//                {
//                    if (skillInfo.ChickenIndex != null)
//                    {
//                        skillInfo.InfoMessageTime = Server.TickCount;
//                        skillInfo.InfoMessageType = 1;
//                        return;
//                    }

//                    skillInfo.CanUse = false;
//                    skillInfo.Cooldown = Server.TickCount;
//                    SpawnChicken(player, skillInfo);
//                }
//            }
//        }

//        private static void SpawnChicken(CCSPlayerController player, PlayerInfo skillInfo)
//        {
//            if (player == null || !player.IsValid) return;

//            var pawn = player.PlayerPawn.Value;
//            if (pawn == null || !pawn.IsValid || pawn.AbsRotation == null || pawn.AbsOrigin == null || pawn.Health <= 0) return;

//            float distance = 40;
//            Vector pawnPos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + 10);
//            Vector chickenPos = pawn.AbsOrigin + SkillUtils.GetForwardVector(pawn.AbsRotation) * distance;
//            chickenPos.Z += 10;

//            var trace = RayTrace.TraceShape(player, pawnPos, chickenPos, 0, null);
//            if (trace != null && trace.Value.DidHit)
//            {
//                skillInfo.CanUse = true;
//                skillInfo.Cooldown = 0;
//                skillInfo.InfoMessageTime = Server.TickCount;
//                skillInfo.InfoMessageType = 2;
//                return;
//            }

//            CChicken? chicken = EntityManager.CreateTrackedChicken(player.Index);
//            if (chicken == null || !chicken.IsValid) return;

//            int health = SkillsInfo.GetValue<int>(skillName, "chickenHealth");

//            chicken.Render = Color.Red;
//            chicken.MaxHealth = health;
//            chicken.Health = health;
//            chickenOwner[chicken.Index] = player.Index;

//            skillInfo.ChickenIndex = chicken.Index;
//            skillInfo.ChickenLastOrigin = chickenPos;
//            skillInfo.ChickenHealth = health;

//            chicken.Teleport(chickenPos);
//        }

//        public static void OnTakeDamage(DynamicHook h)
//        {
//            CEntityInstance param = h.GetParam<CEntityInstance>(0);
//            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

//            if (param == null || param.Entity == null || param2 == null) return;
//            HandleChickenHit(param, param2);
//            HandleExplosionHit(param, param2);
//        }

//        public static void HandleChickenHit(CEntityInstance param, CTakeDamageInfo param2)
//        {
//            if (!chickenOwner.TryGetValue(param.Index, out var ownerIndex)) return;

//            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex);
//            if (owner == null || !owner.IsValid) return;

//            var attackerEnt = param2.Attacker?.Value;
//            if (attackerEnt == null || !attackerEnt.IsValid) return;

//            var attackerPawn = new CCSPlayerPawn(attackerEnt.Handle);
//            if (!attackerPawn.IsValid || attackerPawn.DesignerName != "player") return;

//            var ownerPawn = owner.PlayerPawn.Value;
//            if (ownerPawn == null || !ownerPawn.IsValid) return;

//            if (attackerPawn.Index != ownerPawn.Index && attackerPawn.TeamNum == owner.TeamNum)
//            {
//                param2.Damage = 0;
//                return;
//            }

//            var playerInfo = chickens[owner.Index];
//            if (playerInfo == null) return;

//            playerInfo.ChickenHealth = Math.Max(0, playerInfo.ChickenHealth - (int)param2.Damage);

//            if (playerInfo.ChickenHealth > 0)
//                CreateHitParticles(param.Index);
//            else
//            {
//                playerInfo.ChickenIndex = null;
//                playerInfo.ChickenHealth = SkillsInfo.GetValue<int>(skillName, "chickenHealth");
//                playerInfo.ChickenLastOrigin = null;
//                chickenOwner.TryRemove(param.Index, out _);
//            }
//        }

//        public static void HandleExplosionHit(CEntityInstance param, CTakeDamageInfo param2)
//        {
//            var nade = param2.Attacker.Value;
//            if (nade == null || !nade.IsValid) return;

//            if (nade.DesignerName != "hegrenade_projectile") return;
//            if (string.IsNullOrEmpty(nade.Globalname) || !nade.Globalname.StartsWith("explosive_chicken_team_")) return;

//            var parts = nade.Globalname.Split('_');
//            if (parts.Length < 4) return;
//            if (!int.TryParse(parts[2], out int nadeTeam)) return;
//            if (!uint.TryParse(parts[3], out uint ownerIndex)) return;

//            CCSPlayerPawn victimPawn = new(param.Handle);

//            if (victimPawn.DesignerName != "player") return;
//            if (victimPawn.Controller?.Value == null) return;

//            var victim = victimPawn.Controller.Value.As<CCSPlayerController>();
//            if (victim == null || !victim.IsValid || victim.Index == ownerIndex) return;

//            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex);
//            if (owner != null && !owner.IsValid) owner = null;

//            if (victimPawn.TeamNum == nadeTeam)
//                param2.Damage *= SkillUtils.GetTeamDamageMultiplier(skillName);

//            if (owner != null && param2.Damage >= victimPawn.Health)
//                SkillUtils.RegisterKillCredit(victim.Index, owner.Index, KillfeedIcons.Explosion);
//        }


//        private static void CreateHitParticles(uint chickenIndex)
//        {
//            var chicken = Utilities.GetEntityFromIndex<CChicken>((int)chickenIndex);
//            if (chicken == null || !chicken.IsValid || chicken.AbsOrigin == null) return;

//            var particle = EntityManager.CreateTrackedParticleSystem(chicken.Index, chickenParticle, autoDestroySeconds: 3f);
//            if (particle == null) return;

//            Vector pos = new(chicken.AbsOrigin.X, chicken.AbsOrigin.Y, chicken.AbsOrigin.Z + 10);
//            particle.Teleport(pos);
//            particle.AcceptInput("Start");
//        }

//        public static void OnTick()
//        {
//            int tickCooldown = SkillsInfo.GetValue<int>(skillName, "tickCooldown");
//            float boostFactor = SkillsInfo.GetValue<float>(skillName, "boostFactor");
//            float triggerRadius = SkillsInfo.GetValue<float>(skillName, "triggerRadius");
//            float cooldownDuration = SkillsInfo.GetValue<float>(skillName, "cooldown");

//            bool processMovement = Server.TickCount % tickCooldown == 0;

//            var players = PlayerManager.GetTickPlayers().ToArray();

//            foreach (var player in players)
//            {
//                if (player == null || !player.IsValid) continue;

//                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
//                if (playerInfo?.Skill != skillName) continue;

//                var pawn = player.PlayerPawn.Value;
//                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.Health <= 0) continue;

//                if (!chickens.TryGetValue(player.Index, out var skillInfo) || skillInfo == null) continue;

//                if (!skillInfo.CanUse && skillInfo.ChickenIndex == null)
//                {
//                    float readyAtTick = skillInfo.Cooldown + (cooldownDuration * 64);
//                    if (Server.TickCount >= readyAtTick)
//                    {
//                        skillInfo.CanUse = true;
//                        skillInfo.Cooldown = 0;
//                    }
//                }

//                UpdateHUD(skillInfo);

//                if (skillInfo.ChickenIndex == null || !processMovement) continue;

//                var chicken = Utilities.GetEntityFromIndex<CChicken>((int)skillInfo.ChickenIndex.Value);
//                if (chicken == null || !chicken.IsValid || chicken.AbsOrigin == null)
//                {
//                    skillInfo.ChickenIndex = null;
//                    skillInfo.ChickenHealth = 0;
//                    skillInfo.ChickenLastOrigin = null;
//                    continue;
//                }

//                Vector currentOrigin = new(chicken.AbsOrigin.X, chicken.AbsOrigin.Y, chicken.AbsOrigin.Z);

//                CCSPlayerController? enemy = GetClosetEnemy(currentOrigin, player.TeamNum);
//                if (enemy == null || !enemy.IsValid) continue;

//                var enemyPawn = enemy.PlayerPawn?.Value;
//                if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.AbsOrigin == null) continue;

//                if (chicken.Leader.Raw != enemy.Pawn.Raw)
//                    chicken.Leader.Raw = enemy.Pawn.Raw;

//                player.PrintToChat($"Closest enemy: {(enemy != null ? enemy.PlayerName : "None")}");

//                if (skillInfo.ChickenLastOrigin != null)
//                {
//                    float dx = currentOrigin.X - skillInfo.ChickenLastOrigin.X;
//                    float dy = currentOrigin.Y - skillInfo.ChickenLastOrigin.Y;
//                    float dist2D = MathF.Sqrt(dx * dx + dy * dy);

//                    if (dist2D > 0.05f && dist2D < 20.0f)
//                    {
//                        Vector newPos = new(
//                            currentOrigin.X + (dx * boostFactor),
//                            currentOrigin.Y + (dy * boostFactor),
//                            currentOrigin.Z
//                        );

//                        player.PrintToChat($"Chicken boost");
//                        chicken.Teleport(newPos, chicken.AbsRotation, chicken.AbsVelocity);
//                        skillInfo.ChickenLastOrigin = newPos;
//                    }
//                    else
//                        skillInfo.ChickenLastOrigin = currentOrigin;
//                }
//                else
//                    skillInfo.ChickenLastOrigin = currentOrigin;

//                float pdx = enemyPawn.AbsOrigin.X - skillInfo.ChickenLastOrigin.X;
//                float pdy = enemyPawn.AbsOrigin.Y - skillInfo.ChickenLastOrigin.Y;
//                float pdz = enemyPawn.AbsOrigin.Z - skillInfo.ChickenLastOrigin.Z;
//                float distToPlayer = MathF.Sqrt(pdx * pdx + pdy * pdy + pdz * pdz);

//                if (distToPlayer <= triggerRadius)
//                    ExplodeChicken(player, chicken, skillInfo);
//            }
//        }

//        private static CCSPlayerController? GetClosetEnemy(Vector chickenPos, byte ownerTeam)
//        {
//            var players = PlayerManager.GetTickPlayers().ToArray();
//            CCSPlayerController? closestEnemy = null;

//            float closestDistance = float.MaxValue;
//            foreach (var player in players)
//            {
//                if (player == null || !player.IsValid || player.TeamNum == ownerTeam) continue;

//                var pawn = player.PlayerPawn?.Value;
//                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.Health <= 0) continue;

//                float distance = SkillUtils.Distance(chickenPos, pawn.AbsOrigin);
//                if (distance < closestDistance)
//                {
//                    closestDistance = distance;
//                    closestEnemy = player;
//                }
//            }
//            return closestEnemy;
//        }

//        private static void ExplodeChicken(CCSPlayerController owner, CChicken chicken, PlayerInfo skillInfo)
//        {
//            if (owner == null || !owner.IsValid || chicken == null || !chicken.IsValid || chicken.AbsOrigin == null) return;
//            Vector pos = new(chicken.AbsOrigin.X, chicken.AbsOrigin.Y, chicken.AbsOrigin.Z + 10);

//            EntityManager.DestroyEntity(chicken.Index, 0f);

//            skillInfo.ChickenIndex = null;
//            skillInfo.ChickenHealth = 0;
//            skillInfo.ChickenLastOrigin = null;

//            chickenOwner.TryRemove(chicken.Index, out _);

//            jRandomSkills.Instance.AddTickTimer(4, () =>
//            {
//                SkillUtils.CreateHEGrenadeProjectile(pos, angle, new Vector(0, 0, -10), owner.TeamNum);
//                nades.AddOrUpdate(Server.TickCount, (owner.TeamNum, owner.Index), (_, _) => (owner.TeamNum, owner.Index));
//            });
//        }

//        public static void OnEntitySpawned(CEntityInstance entity)
//        {
//            if (entity.DesignerName != "hegrenade_projectile") return;

//            var heProjectile = entity.As<CBaseCSGrenadeProjectile>();
//            if (heProjectile == null || !heProjectile.IsValid || heProjectile.AbsRotation == null) return;

//            int lastTick = Server.TickCount;

//            Server.NextFrame(() =>
//            {
//                if (heProjectile == null || !heProjectile.IsValid) return;
//                if (!(NearlyEquals(angle.X, heProjectile.AbsRotation.X) && NearlyEquals(angle.Y, heProjectile.AbsRotation.Y) && NearlyEquals(angle.Z, heProjectile.AbsRotation.Z)))
//                    return;

//                heProjectile.TicksAtZeroVelocity = 100;
//                heProjectile.Damage = SkillsInfo.GetValue<int>(skillName, "explosionDamage");
//                heProjectile.DmgRadius = SkillsInfo.GetValue<float>(skillName, "explosionRadius");
//                heProjectile.DetonateTime = 0;

//                if (nades.TryRemove(lastTick, out var source))
//                    heProjectile.Globalname = $"explosive_chicken_team_{source.Team}_{source.Owner}_{heProjectile.Index}";
//            });
//        }

//        private static void UpdateHUD(PlayerInfo skillInfo)
//        {
//            if (skillInfo == null) return;

//            var player = Utilities.GetPlayerFromIndex((int)skillInfo.OwnerIndex);
//            if (player == null || !player.IsValid) return;

//            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
//            if (playerInfo == null) return;

//            float cooldownDuration = SkillsInfo.GetValue<float>(skillName, "cooldown");

//            float cooldown = 0;
//            if (!skillInfo.CanUse)
//            {
//                float readyAtTick = skillInfo.Cooldown + (cooldownDuration * 64);
//                float time1 = (readyAtTick - Server.TickCount) / 64;
//                cooldown = (int)Math.Ceiling(Math.Max(time1, 0));
//            }

//            bool chickenActive = skillInfo.ChickenIndex != null;

//            if (skillInfo.InfoMessageTime + (64 * 2) > Server.TickCount)
//            {
//                string? message = skillInfo.InfoMessageType switch
//                {
//                    1 => player.GetTranslation("chicken_already_active"),
//                    2 => player.GetTranslation("chicken_blocked"),
//                    _ => null
//                };
//                playerInfo.PrintHTML = message;
//            }
//            else if (chickenActive)
//                playerInfo.PrintHTML = $"{player.GetTranslation("chicken_hud_active", $"<font color='#00FF00'>{skillInfo.ChickenHealth}</font>")}";
//            else if (cooldown > 0)
//                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
//            else
//                playerInfo.PrintHTML = null;
//        }

//        private static bool NearlyEquals(float a, float b, float epsilon = 0.001f) => Math.Abs(a - b) < epsilon;

//        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#b5ab8f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Legendary, float cooldown = 30, float triggerRadius = 150.0f, int chickenHealth = 100, int tickCooldown = 16, float boostFactor = 2.5f, float explosionDamage = 100, float explosionRadius = 150.0f, float dmgReductionForTeamates = 0.5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
//        {
//            public float Cooldown { get; set; } = cooldown;
//            public float TriggerRadius { get; set; } = triggerRadius;
//            public int ChickenHealth { get; set; } = chickenHealth;
//            public int TickCooldown { get; set; } = tickCooldown;
//            public float BoostFactor { get; set; } = boostFactor;
//            public float ExplosionDamage { get; set; } = explosionDamage;
//            public float ExplosionRadius { get; set; } = explosionRadius;
//            public float DmgReductionForTeamates { get; set; } = dmgReductionForTeamates;
//        }
//    }
//}