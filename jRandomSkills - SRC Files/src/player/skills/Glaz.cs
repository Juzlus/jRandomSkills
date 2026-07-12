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
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];
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
                var playerInfo = PlayerManager.GetPlayerByIndex((PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index));

                var observedPlayer = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == player?.Pawn?.Value?.ObserverServices?.ObserverTarget?.Value?.Handle);
                var observerInfo = observedPlayer != null ? PlayerManager.GetPlayerByIndex(observedPlayer.Index) : null;

                if (playerInfo?.Skill != skillName && observerInfo?.Skill != skillName) continue;
                foreach (var entityIndex in smokes.Keys)
                {
                    var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)entityIndex);
                    if (entity == null || !entity.IsValid) continue;

                    if (info.TransmitEntities.Contains(entity.Index))
                        info.TransmitEntities.Remove(entity.Index);
                }
            }
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Weapon;
            if (weapon != "smokegrenade") return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Index] = grenadesLeft - 1;
                player!.GiveNamedItem($"weapon_{weapon}");
                SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadesLeft - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadesLeft);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Item;
            if (string.IsNullOrEmpty(weapon) || weapon != "smokegrenade") return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadesLeft);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            Event.EnableTransmit();

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            playersWithSkill.TryAdd(player.Index, grenadeLimit);

            SkillUtils.TryGiveWeapon(player, CsItem.SmokeGrenade);
            SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadeLimit);

            SkillUtils.ForceFullUpdateToAll();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Index, out _);
            SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5d00ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, int grenadeLimit = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}