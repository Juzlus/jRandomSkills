using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
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

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            activeChickens.Clear();
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
                        state.Chicken.Remove();
        }

        private static void SpawnChicken(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            int amount = SkillsInfo.GetValue<int>(skillName, "amount");
            var list = new List<ChickenState>();

            for (int i = 0; i < amount; i++)
            {
                CChicken? chicken = EntityManager.CreateTrackedChicken(player.Index);
                if (chicken == null || !chicken.IsValid) continue;

                chicken.Render = Color.LightGreen;

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
            int tickCooldown = SkillsInfo.GetValue<int>(skillName, "tickCooldown");
            if (Server.TickCount % tickCooldown == 0) return;

            var players = PlayerManager.GetTickPlayers().ToArray();
            int healAmount = SkillsInfo.GetValue<int>(skillName, "heal");
            float healRadius = SkillsInfo.GetValue<float>(skillName, "healRadius");

            foreach (var player in players)
            {
                if (player == null || !player.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo?.Skill != skillName) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) continue;

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
                            float boostFactor = 2.5f;
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#b5ab8f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Legendary, int amount = 3, int heal = 2, int tickCooldown = 16, float healRadius = 150.0f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int Amount { get; set; } = amount;
            public int Heal { get; set; } = heal;
            public int TickCooldown { get; set; } = tickCooldown;
            public float HealRadius { get; set; } = healRadius;
        }
    }
}