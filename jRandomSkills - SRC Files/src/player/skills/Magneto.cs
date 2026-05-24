using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Magneto : ISkill
    {
        private const Skills skillName = Skills.Magneto;
        private readonly static ConcurrentDictionary<uint, byte> nades = [];
        private readonly static ConcurrentDictionary<uint, byte> players = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            nades.Clear();
            players.Clear();
        }

        public static void OnTick()
        {
            if (Server.TickCount % 5 != 0) return;
            float radius = SkillsInfo.GetValue<float>(skillName, "radius");

            foreach (var nadeIndex in nades.Keys)
            {
                var nade = Utilities.GetEntityFromIndex<CBaseCSGrenadeProjectile>((int)nadeIndex);
                if (nade == null || !nade.IsValid)
                {
                    nades.TryRemove(nadeIndex, out _);
                    continue;
                }

                foreach (var playerIndex in players.Keys)
                {
                    var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                    if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid)
                    {
                        players.TryRemove(playerIndex, out _);
                        continue;
                    }

                    var pawn = player.PlayerPawn.Value;
                    double distanceMoved = SkillUtils.GetDistance(nade.AbsOrigin ?? Vector.Zero, pawn.AbsOrigin ?? Vector.Zero);

                    if (distanceMoved < radius && nade.TeamNum != player.TeamNum)
                    {
                        nade.Teleport(null, null, -nade.AbsVelocity);
                        nades.TryRemove(nadeIndex, out _);
                    }
                }
            }
        }

        public static void OnEntitySpawned(CEntityInstance @event)
        {
            var name = @event.DesignerName;
            if (!name.EndsWith("_projectile")) return;

            var grenade = @event.As<CBaseCSGrenadeProjectile>();
            if (grenade == null || !grenade.IsValid) return;

            nades.TryAdd(grenade.Index, 0);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            players.TryAdd(player.Index, 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            players.TryRemove(player.Index, out _);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#f081ec", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float radius = 100) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float Radius { get; set; } = radius;
        }
    }
}