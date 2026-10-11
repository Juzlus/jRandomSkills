using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    // Your decoys are lightning rods: when one lands, every enemy near it gets tased.
    public class ThunderGod : ISkill
    {
        private const Skills skillName = Skills.ThunderGod;
        private static readonly ConcurrentDictionary<uint, int> decoysLeft = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            DecoyRing.PreloadAssets();
        }

        public static void NewRound()
        {
            decoysLeft.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            int limit = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "decoyLimit"));
            decoysLeft[player.Index] = limit;
            SkillUtils.TryGiveWeapon(player, CsItem.DecoyGrenade);
            SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, limit);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            decoysLeft.TryRemove(player.Index, out _);
            SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, 1);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            decoysLeft.TryRemove(playerIndex, out _);
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid || @event.Weapon != "decoy") return;
            if (PlayerManager.GetPlayerByIndex(player.Index)?.HasSkill(skillName) != true) return;

            if (decoysLeft.TryGetValue(player.Index, out int left) && left > 1)
            {
                decoysLeft[player.Index] = left - 1;
                player.GiveNamedItem("weapon_decoy");
                SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, left - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            if (decoysLeft.TryGetValue(player.Index, out int left) && left > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, left);
        }

        public static void DecoyStarted(EventDecoyStarted @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player)) return;
            if (PlayerManager.GetPlayerByIndex(player!.Index)?.HasSkill(skillName) != true) return;

            float radius = SkillsInfo.GetValue<float>(skillName, "radius");
            int damage = SkillsInfo.GetValue<int>(skillName, "damage");
            Vector strike = new(@event.X, @event.Y, @event.Z);

            DecoyRing.Show(skillName, (uint)@event.Entityid, strike, radius);
            player.PlayerPawn.Value?.EmitSound("Weapon_Taser.Single", volume: SkillsInfo.GetValue<float>(skillName, "soundVolume"));

            int hit = 0;
            foreach (var enemy in PlayerManager.GetTickPlayers())
            {
                if (!Instance.IsPlayerValid(enemy) || enemy.Index == player.Index || enemy.Team == player.Team || !enemy.PawnIsAlive) continue;
                var enemyPawn = enemy.PlayerPawn.Value;
                if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.AbsOrigin == null) continue;
                if (SkillUtils.GetDistance(strike, enemyPawn.AbsOrigin) > radius) continue;

                enemyPawn.EmitSound("Weapon_Taser.Single", volume: SkillsInfo.GetValue<float>(skillName, "soundVolume"));
                SkillUtils.TakeHealth(enemyPawn, damage, player, KillfeedIcons.Taser);
                hit++;
            }

            if (hit > 0)
                player.PrintToChat($" {ChatColors.Yellow}{player.GetTranslation("thundergod_hit_info", hit)}");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffe600", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, float radius = 250f, int damage = 100, int decoyLimit = 2, float soundVolume = 1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Radius { get; set; } = radius;
            public int Damage { get; set; } = damage;
            public int DecoyLimit { get; set; } = decoyLimit;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
