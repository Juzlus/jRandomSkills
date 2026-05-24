using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills
{
    public class Smoker : ISkill
    {
        private const Skills skillName = Skills.Smoker;
        private readonly static ConcurrentDictionary<uint, List<Timer>> playerSmokes = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var timers in playerSmokes.Values)
                    timers.ForEach(t => t?.Kill());
                playerSmokes.Clear();
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.SmokeGrenade);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (playerSmokes.TryRemove(player.Index, out var timers))
                timers.ForEach(t => t?.Kill());
        }

        public static void SmokegrenadeDetonate(EventSmokegrenadeDetonate @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            uint playerIndex = player.Index;

            Vector pos = new(@event.X, @event.Y, @event.Z);
            float refillInterval = 15.5f;

            Timer? smokeTimer = null;
            smokeTimer = Instance.AddTimer(refillInterval, () =>
            {
                var player = Utilities.GetPlayerFromIndex((int)playerIndex);

                if (player == null || !player.IsValid)
                {
                    smokeTimer?.Kill();
                    return;
                }

                SkillUtils.CreateSmokeGrenadeProjectile(pos, QAngle.Zero, Vector.Zero, player.TeamNum);
            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

            playerSmokes.AddOrUpdate(player.Index, [smokeTimer], (_, list) => { list.Add(smokeTimer); return list; });
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#b5ab8f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = 1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}