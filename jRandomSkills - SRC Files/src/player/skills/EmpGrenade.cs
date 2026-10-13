using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class EmpGrenade : ISkill
    {
        private const Skills skillName = Skills.EmpGrenade;

        private const string ownerTag = "EmpGrenade";

        private static readonly ConcurrentDictionary<uint, int> jammedUntilTick = [];
        private static readonly ConcurrentDictionary<uint, int> playersWithSkill = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            RestoreAll();
        }

        public static void RoundEnd()
        {
            RestoreAll();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            jammedUntilTick.TryRemove(playerIndex, out _);
            playersWithSkill.TryRemove(playerIndex, out _);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            if (jammedUntilTick.TryRemove(victim.Key(), out _))
                SetHud(victim.Key(), false);
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            if (@event.Weapon != "hegrenade") return;

            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            if (attacker == null || !attacker.IsValid) return;

            var attackerInfo = PlayerManager.GetPlayer(attacker);
            if (attackerInfo?.Skill != skillName) return;

            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid || victim.Team == attacker.Team) return;

            float duration = SkillsInfo.GetValue<float>(skillName, "jamSeconds");
            jammedUntilTick[victim.Key()] = Server.TickCount + (int)(duration * 64);
            SetHud(victim.Key(), true);

            var victimEvent = PlayerManager.GetPlayerFromEvent(victim);
            if (victimEvent != null && victimEvent.IsValid)
                victimEvent.PrintToChat($" {ChatColors.Red}{victimEvent.GetTranslation("empgrenade_enemy_info")}");
        }

        public static void OnTick()
        {
            if (jammedUntilTick.IsEmpty) return;

            int now = Server.TickCount;

            foreach (var kvp in jammedUntilTick)
            {
                if (now < kvp.Value) continue;
                if (!jammedUntilTick.TryRemove(kvp.Key, out _)) continue;

                SetHud(kvp.Key, false);
            }
        }

        private static void RestoreAll()
        {
            foreach (var playerIndex in jammedUntilTick.Keys)
                SetHud(playerIndex, false);

            jammedUntilTick.Clear();
        }

        private static void SetHud(uint playerIndex, bool jam)
        {
            var player = PlayerManager.GetController(playerIndex);
            if (player == null || !player.IsValid) return;

            ApplyHud(player, jam);
            ApplyHud(PlayerManager.GetPlayerFromEvent(player), jam);
        }

        private static void ApplyHud(CCSPlayerController? player, bool jam)
        {
            if (player == null || !player.IsValid) return;

            SkillUtils.SetCrosshairHidden(player, ownerTag, jam);
            SkillUtils.SetRadarDisabled(player, ownerTag, jam);
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            if (@event.Weapon != "hegrenade") return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Key()] = grenadesLeft - 1;
                player!.GiveNamedItem("weapon_hegrenade");
                SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, grenadesLeft - 1);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            playersWithSkill[player.Key()] = grenadeLimit;

            SkillUtils.TryGiveWeapon(player, CsItem.HEGrenade);
            SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, grenadeLimit);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Key(), out _);

            if (playersWithSkill.IsEmpty)
                RestoreAll();

            SkillUtils.UpdateGrenadeCount(player, CsItem.HEGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#3ad6e8", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon, float jamSeconds = 3f, int grenadeLimit = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float JamSeconds { get; set; } = jamSeconds;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}
