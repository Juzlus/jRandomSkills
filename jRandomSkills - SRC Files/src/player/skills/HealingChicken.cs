using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills
{
    public class HealingChicken : ISkill
    {
        private const Skills skillName = Skills.HealingChicken;
        private readonly static ConcurrentDictionary<ulong, List<Timer>> playerSmokes = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            // SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {

        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SpawnChicken(player);
        }

        public static void DisableSkill(CCSPlayerController player)
        {

        }

        private static void SpawnChicken(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            int amount = SkillsInfo.GetValue<int>(skillName, "amount");
            for (int i = 0; i < 1; i++)
            {
                CChicken? chicken = Utilities.CreateEntityByName<CChicken>("chicken");
                if (chicken == null || !chicken.IsValid) continue;

                chicken.Render = Color.Green;
                Vector offset = new (
                    (float)(100 * Math.Cos(2 * Math.PI * i / amount)),
                    (float)(100 * Math.Sin(2 * Math.PI * i / amount)),
                    0
                );

                chicken.DispatchSpawn();
                chicken.Teleport(pawn.AbsOrigin + offset);

                // Schema.SetSchemaValue(chicken.Handle, "CChicken", "m_leader", player.PlayerPawn.Raw);
                Vector spawn = SkillUtils.GetSpawnPointVector(player);
                Schema.SetSchemaValue(chicken.Handle, "CChicken", "m_vecPathGoal", spawn);

                float speed = 50;
                Instance.AddTickTimer(1, () =>
                {
                    if (chicken == null || !chicken.IsValid) return;

                    Server.PrintToChatAll($"{chicken.PathGoal}, {chicken.UpdateTimer.Duration}, {chicken.UpdateTimer.Timescale}");
                }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#b5ab8f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int amount = 3, int heal = 2, int tickCooldown = 16) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public int Amount { get; set; } = amount;
            public int Heal { get; set; } = heal;
            public int TickCooldown { get; set; } = tickCooldown;
        }
    }
}