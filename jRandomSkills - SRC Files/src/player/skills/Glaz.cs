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
        private readonly static ConcurrentDictionary<uint, uint> smokes = [];
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];
        private static readonly object setLock = new();
        private static readonly List<uint> smokeIndexes = [];
        private static readonly Dictionary<nint, uint> pawnOwners = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            playersWithSkill.TryRemove(playerIndex, out _);
        }

        public static void NewRound()
        {
            lock (setLock)
                smokes.Clear();
        }

        public static void SmokegrenadeDetonate(EventSmokegrenadeDetonate @event)
        {
            var smoke = Utilities.GetEntityFromIndex<CBaseEntity>(@event.Entityid);
            if (smoke == null || !smoke.IsValid) return;
            smokes[smoke.Index] = smoke.EntityHandle.Raw;
        }

        public static void SmokegrenadeExpired(EventSmokegrenadeExpired @event)
        {
            smokes.TryRemove((uint)@event.Entityid, out _);
        }

        private static bool TrackExistingSmokes()
        {
            bool found = false;
            foreach (var smoke in Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("smokegrenade_projectile"))
            {
                if (smoke == null || !smoke.IsValid || !smoke.DidSmokeEffect) continue;
                smokes[smoke.Index] = smoke.EntityHandle.Raw;
                found = true;
            }
            return found;
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            if (smokes.IsEmpty || playersWithSkill.IsEmpty) return;

            smokeIndexes.Clear();
            foreach (var (entityIndex, handleRaw) in smokes)
            {
                var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)entityIndex);
                if (entity == null || !entity.IsValid || entity.EntityHandle.Raw != handleRaw)
                {
                    smokes.TryRemove(entityIndex, out _);
                    continue;
                }
                smokeIndexes.Add(entityIndex);
            }

            if (smokeIndexes.Count == 0) return;

            bool pawnOwnersBuilt = false;

            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;
                var playerInfo = PlayerManager.GetPlayer(PlayerManager.GetPlayerEvent(player) ?? player);

                if (playerInfo?.Skill != skillName)
                {
                    var observerTarget = player.Pawn?.Value?.ObserverServices?.ObserverTarget?.Value?.Handle ?? nint.Zero;
                    if (observerTarget == nint.Zero) continue;

                    if (!pawnOwnersBuilt)
                    {
                        pawnOwnersBuilt = true;
                        BuildPawnOwners();
                    }

                    if (!pawnOwners.TryGetValue(observerTarget, out uint observedIndex)) continue;
                    if (PlayerManager.GetPlayerByKey(observedIndex)?.Skill != skillName) continue;
                }

                foreach (var index in smokeIndexes)
                    SkillUtils.HideTransmit(info, index);
            }
        }

        private static void BuildPawnOwners()
        {
            pawnOwners.Clear();
            var owners = pawnOwners;

            foreach (var p in PlayerManager.GetTickPlayers())
            {
                if (p == null || !p.IsValid) continue;

                nint handle = p.Pawn?.Value?.Handle ?? nint.Zero;
                if (handle == nint.Zero) continue;

                owners.TryAdd(handle, p.Key());
            }
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Weapon;
            if (weapon != "smokegrenade") return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Key()] = grenadesLeft - 1;
                SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadesLeft - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft) && grenadesLeft > 1 && SkillUtils.ThrewGrenadeRecently(player))
                SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadesLeft);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Item;
            if (string.IsNullOrEmpty(weapon) || weapon != "smokegrenade") return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft))
                playersWithSkill[player.Key()] = SkillUtils.ClampGrenadeAfterPickup(player, CsItem.SmokeGrenade, grenadesLeft);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            Event.EnableTransmit();

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            SkillUtils.TryGiveWeapon(player, CsItem.SmokeGrenade);
            playersWithSkill[player.Key()] = grenadeLimit;

            SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, grenadeLimit);

            if (TrackExistingSmokes())
                SkillUtils.ForceFullUpdate(player);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Key(), out _);
            SkillUtils.UpdateGrenadeCount(player, CsItem.SmokeGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5d00ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int grenadeLimit = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}