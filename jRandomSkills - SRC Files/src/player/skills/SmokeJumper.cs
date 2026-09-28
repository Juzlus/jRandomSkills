using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class SmokeJumper : ISkill
    {
        private const Skills skillName = Skills.SmokeJumper;
        private static readonly ConcurrentDictionary<uint, int> smokesLeft = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            smokesLeft.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            smokesLeft[player.Index] = SkillsInfo.GetValue<int>(skillName, "smokeLimit");
            SkillUtils.TryGiveWeapon(player, CsItem.SmokeGrenade);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            smokesLeft.TryRemove(player.Index, out _);
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid || @event.Weapon != "smokegrenade") return;
            if (PlayerManager.GetPlayerByIndex(player.Index)?.HasSkill(skillName) != true) return;

            // Hand the next smoke over straight away so the player can keep jumping.
            if (smokesLeft.TryGetValue(player.Index, out int left) && left > 1)
            {
                smokesLeft[player.Index] = left - 1;
                player.GiveNamedItem($"weapon_{@event.Weapon}");
            }
        }

        public static void SmokegrenadeDetonate(EventSmokegrenadeDetonate @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;
            if (PlayerManager.GetPlayerByIndex(player!.Index)?.HasSkill(skillName) != true) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            pawn.Teleport(new Vector(@event.X, @event.Y, @event.Z + 8f), null, new Vector(0, 0, 0));
            SkillUtils.EmitSoundToPlayer(player, "Player.Respawn", SkillsInfo.GetValue<float>(skillName, "soundVolume"));
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#9aa7b8", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon, int smokeLimit = 2, float soundVolume = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int SmokeLimit { get; set; } = smokeLimit;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
