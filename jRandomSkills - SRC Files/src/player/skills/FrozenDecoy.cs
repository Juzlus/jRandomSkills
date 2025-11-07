using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class FrozenDecoy : ISkill
    {
        private const Skills skillName = Skills.FrozenDecoy;
        private static readonly ConcurrentDictionary<Vector, byte> decoys = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            decoys.Clear();
        }

        public static void DecoyStarted(EventDecoyStarted @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;
            decoys.TryAdd(new Vector(@event.X, @event.Y, @event.Z), 0);
        }

        public static void DecoyDetonate(EventDecoyDetonate @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName) return;
            foreach (var decoy in decoys.Keys.Where(v => v.X == @event.X && v.Y == @event.Y && v.Z == @event.Z))
                decoys.TryRemove(decoy, out _);
        }

        public static void OnTick()
        {
            foreach (Vector decoyPos in decoys.Keys)
                foreach (var player in Utilities.GetPlayers().Where(p => p.Team == CsTeam.Terrorist || p.Team == CsTeam.CounterTerrorist))
                {
                    var decoyRadius = SkillsInfo.GetValue<float>(skillName, "triggerRadius");
                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;
                    double distance = SkillUtils.GetDistance(decoyPos, pawn.AbsOrigin);
                    if (distance <= decoyRadius)
                    {
                        double modifier = Math.Clamp(distance / decoyRadius, 0f, 1f);
                        pawn.VelocityModifier = (float)Math.Pow(modifier, SkillsInfo.GetValue<int>(skillName, "slownessMultiplier"));
                    }
                }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillUtils.TryGiveWeapon(player, CsItem.DecoyGrenade);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#00eaff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float triggerRadius = 180, int slownessMultiplier = 5) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float TriggerRadius { get; set; } = triggerRadius;
            public int SlownessMultiplier { get; set; } = slownessMultiplier;
        }
    }
}