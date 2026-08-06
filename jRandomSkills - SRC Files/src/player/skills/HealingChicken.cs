using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;

namespace src.player.skills
{
    public class HealingChicken : ISkill
    {
        private const Skills skillName = Skills.HealingChicken;

        private class ChickenState
        {
            public CChicken? Chicken { get; set; }
            public Vector? LastOrigin { get; set; }
            public int TickCounter { get; set; }
        }

        private static readonly ConcurrentDictionary<uint, List<ChickenState>> activeChickens = new();
        private static readonly ConcurrentDictionary<uint, (uint ownerIndex, int health)> chickenOwners = new();
        private const string chickenParticle = "particles/critters/chicken/chicken_goop.vpcf";

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            jRandomSkills.Instance.AddToManifest(chickenParticle);
        }

        public static void NewRound()
        {
            activeChickens.Clear();
            chickenOwners.Clear();
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param2 == null) return;

            if (!chickenOwners.TryGetValue(param.Index, out var ownerData)) return;
            uint ownerIndex = ownerData.ownerIndex;
            int health = ownerData.health;

            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex);
            if (owner == null || !owner.IsValid) return;

            var attackerEnt = param2.Attacker?.Value;
            if (attackerEnt == null || !attackerEnt.IsValid) return;

            var attackerPawn = new CCSPlayerPawn(attackerEnt.Handle);
            if (!attackerPawn.IsValid || attackerPawn.DesignerName != "player") return;

            var ownerPawn = owner.PlayerPawn.Value;
            if (ownerPawn == null || !ownerPawn.IsValid) return;

            if (attackerPawn.Index != ownerPawn.Index && attackerPawn.TeamNum == owner.TeamNum)
            {
                param2.Damage = 0;
                return;
            }

            int newHealth = Math.Max(0, health - (int)param2.Damage);
            chickenOwners[param.Index] = (ownerIndex, newHealth);

            if (newHealth > 0)
                CreateHitParticles(param.Index);
        }

        private static void CreateHitParticles(uint chickenIndex)
        {
            var chicken = Utilities.GetEntityFromIndex<CChicken>((int)chickenIndex);
            if (chicken == null || !chicken.IsValid || chicken.AbsOrigin == null) return;

            var particle = EntityManager.CreateTrackedParticleSystem(chicken.Index, chickenParticle, autoDestroySeconds: 3f);
            if (particle == null) return;

            Vector pos = new(chicken.AbsOrigin.X, chicken.AbsOrigin.Y, chicken.AbsOrigin.Z + 10);
            particle.Teleport(pos);
            particle.AcceptInput("Start");
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SpawnChicken(player);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (activeChickens.TryRemove(player.Index, out var chickens))
                foreach (var state in chickens)
                    if (state.Chicken != null && state.Chicken.IsValid)
                    {
                        chickenOwners.TryRemove(state.Chicken.Index, out _);
                        state.Chicken.Remove();
                    }
        }

        private static void SpawnChicken(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            int amount = SkillsInfo.GetValue<int>(skillName, "amount");
            int health = SkillsInfo.GetValue<int>(skillName, "chickenHealth");
            var list = new List<ChickenState>();

            for (int i = 0; i < amount; i++)
            {
                CChicken? chicken = EntityManager.CreateTrackedChicken(player.Index);
                if (chicken == null || !chicken.IsValid) continue;

                chicken.Render = Color.LightGreen;
                chicken.MaxHealth = health;
                chicken.Health = health;

                chickenOwners[chicken.Index] = (player.Index, health);

                Vector offset = new(
                    (float)(100 * Math.Cos(2 * Math.PI * i / amount)),
                    (float)(100 * Math.Sin(2 * Math.PI * i / amount)),
                    0
                );

                chicken.Teleport(new Vector(pawn.AbsOrigin.X + offset.X, pawn.AbsOrigin.Y + offset.Y, pawn.AbsOrigin.Z + offset.Z));
                chicken.Leader.Raw = pawn.Index;

                list.Add(new ChickenState { Chicken = chicken, LastOrigin = null, TickCounter = 0 });
            }

            activeChickens[player.Index] = list;
        }

        public static void OnTick()
        {
            if (activeChickens.IsEmpty) return;

            int tickCooldown = SkillsInfo.GetValue<int>(skillName, "tickCooldown");
            if (Server.TickCount % tickCooldown == 0) return;

            float boostFactor = SkillsInfo.GetValue<float>(skillName, "boostFactor");

            var players = PlayerManager.GetTickPlayers();
            int healAmount = SkillsInfo.GetValue<int>(skillName, "heal");
            float healRadius = SkillsInfo.GetValue<float>(skillName, "healRadius");

            foreach (var player in players)
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo?.Skill != skillName) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.Health <= 0) continue;

                if (!activeChickens.TryGetValue(player.Index, out var chickens)) continue;

                for (int i = chickens.Count - 1; i >= 0; i--)
                {
                    var state = chickens[i];
                    if (state.Chicken == null || !state.Chicken.IsValid || state.Chicken.AbsOrigin == null)
                    {
                        chickens.RemoveAt(i);
                        continue;
                    }

                    var chicken = state.Chicken;

                    if (chicken.Leader.Raw != player.Pawn.Raw)
                        chicken.Leader.Raw = player.Pawn.Raw;

                    Vector currentOrigin = new(chicken.AbsOrigin.X, chicken.AbsOrigin.Y, chicken.AbsOrigin.Z);

                    if (state.LastOrigin != null)
                    {
                        float dx = currentOrigin.X - state.LastOrigin.X;
                        float dy = currentOrigin.Y - state.LastOrigin.Y;
                        float dist2D = MathF.Sqrt(dx * dx + dy * dy);

                        if (dist2D > 0.05f && dist2D < 20.0f)
                        {
                            Vector newPos = new(
                                currentOrigin.X + (dx * boostFactor),
                                currentOrigin.Y + (dy * boostFactor),
                                currentOrigin.Z
                            );

                            chicken.Teleport(newPos, chicken.AbsRotation, chicken.AbsVelocity);
                            state.LastOrigin = newPos;
                        }
                        else
                            state.LastOrigin = currentOrigin;
                    }
                    else
                        state.LastOrigin = currentOrigin;

                    float pdx = pawn.AbsOrigin.X - state.LastOrigin.X;
                    float pdy = pawn.AbsOrigin.Y - state.LastOrigin.Y;
                    float pdz = pawn.AbsOrigin.Z - state.LastOrigin.Z;
                    float distToPlayer = MathF.Sqrt(pdx * pdx + pdy * pdy + pdz * pdz);

                    if (distToPlayer <= healRadius)
                    {
                        state.TickCounter++;
                        if (state.TickCounter >= tickCooldown)
                        {
                            SkillUtils.AddHealth(pawn, healAmount);
                            state.TickCounter = 0;
                        }
                    }
                    else
                        state.TickCounter = 0;
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#b5ab8f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Legendary, int amount = 3, int heal = 2, int tickCooldown = 16, float healRadius = 150.0f, int chickenHealth = 50, float boostFactor = 2.5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int Amount { get; set; } = amount;
            public int Heal { get; set; } = heal;
            public int TickCooldown { get; set; } = tickCooldown;
            public float HealRadius { get; set; } = healRadius;
            public int ChickenHealth { get; set; } = chickenHealth;
            public float BoostFactor { get; set; } = boostFactor;
        }
    }
}