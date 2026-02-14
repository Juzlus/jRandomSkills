using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Smoker : ISkill
    {
        private const Skills skillName = Skills.Smoker;
        private readonly static ConcurrentDictionary<uint, int> smokes = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
                smokes.Clear();


        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.SmokeGrenade);
        }

        public static void SmokegrenadeDetonate(EventSmokegrenadeDetonate @event)
        {
            Server.PrintToChatAll("BOOM");
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            var smoke = Utilities.GetEntityFromIndex<CSmokeGrenadeProjectile>(@event.Entityid);
            if (smoke == null || !smoke.IsValid) return;

            smokes.TryAdd(smoke.Index, Server.TickCount);

            // smoke.SmokeEffectTickBegin = Server.TickCount - ((19 - 15) * 64);
            smoke.NextThinkTick = 0;

            // Utilities.SetStateChanged(smoke, "CSmokeGrenadeProjectile", "m_nSmokeEffectTickBegin");
            Utilities.SetStateChanged(smoke, "CBaseEntity", "m_nNextThinkTick");
        }

        public static void SmokegrenadeExpired(EventSmokegrenadeExpired @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;

            Vector pos = new(@event.X, @event.Y, @event.Z);
            SkillUtils.CreateSmokeGrenadeProjectile(pos, new QAngle(0, 0, 0), new Vector(0, 0, 0), player.TeamNum);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#b5ab8f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}