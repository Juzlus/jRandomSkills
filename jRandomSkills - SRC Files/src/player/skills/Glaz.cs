using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Glaz : ISkill
    {
        private const Skills skillName = Skills.Glaz;
        private readonly static ConcurrentDictionary<int, byte> smokes = [];
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

        public static void SmokegrenadeDetonate(EventSmokegrenadeDetonate @event)
        {
            smokes.TryAdd(@event.Entityid, 0);
        }

        public static void SmokegrenadeExpired(EventSmokegrenadeExpired @event)
        {
            smokes.TryRemove(@event.Entityid, out _);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var (info, player) in infoList)
            {
                if (player == null) continue;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);

                var observedPlayer = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == player?.Pawn?.Value?.ObserverServices?.ObserverTarget?.Value?.Handle);
                var observerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == observedPlayer?.SteamID);

                if (playerInfo?.Skill != skillName && observerInfo?.Skill != skillName) continue;
                foreach (var entityIndex in smokes.Keys)
                {
                    var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)entityIndex);
                    if (entity == null || !entity.IsValid) continue;
                    info.TransmitEntities.Remove(entity.Index);
                }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();
            SkillUtils.TryGiveWeapon(player, CsItem.SmokeGrenade);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5d00ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}