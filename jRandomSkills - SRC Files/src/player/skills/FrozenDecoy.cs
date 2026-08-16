using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class FrozenDecoy : ISkill
    {
        private const Skills skillName = Skills.FrozenDecoy;
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            DecoyRing.PreloadAssets();
        }

        public static void NewRound()
        {
            DecoyTracker.Clear(skillName);
        }

        public static void RoundEnd()
        {
            DecoyTracker.Clear(skillName);
        }

        public static void DecoyStarted(EventDecoyStarted @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            Vector pos = new(@event.X, @event.Y, @event.Z);
            DecoyTracker.Add(skillName, (uint)@event.Entityid, pos, player.Index);
            DecoyRing.Show(skillName, (uint)@event.Entityid, pos, SkillsInfo.GetValue<float>(skillName, "triggerRadius"));
        }

        public static void DecoyDetonate(EventDecoyDetonate @event)
        {
            DecoyTracker.Remove(skillName, (uint)@event.Entityid);
        }

        public static void OnTick()
        {
            var decoyPositions = DecoyTracker.Positions(skillName);
            if (decoyPositions.Length == 0) return;

            float decoyRadius = SkillsInfo.GetValue<float>(skillName, "triggerRadius");
            int slownessMultiplier = SkillsInfo.GetValue<int>(skillName, "slownessMultiplier");

            List<CCSPlayerPawn> pawns = [];
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;
                if (player.Team is not (CsTeam.CounterTerrorist or CsTeam.Terrorist)) continue;

                var eventPlayer = PlayerManager.GetPlayerEvent(player);
                if (eventPlayer == null || !eventPlayer.IsValid) continue;

                var pawn = eventPlayer.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) continue;

                pawns.Add(pawn);
            }

            if (pawns.Count == 0) return;

            foreach (Vector decoyPos in decoyPositions)
                foreach (var pawn in pawns)
                {
                    var origin = pawn.AbsOrigin;
                    if (origin == null) continue;

                    double distance = SkillUtils.GetDistance(decoyPos, origin);
                    if (distance > decoyRadius) continue;

                    double modifier = Math.Clamp(distance / decoyRadius, 0f, 1f);
                    pawn.VelocityModifier = (float)Math.Pow(modifier, slownessMultiplier);
                }
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Weapon;
            if (weapon != "decoy") return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Index] = grenadesLeft - 1;
                player!.GiveNamedItem($"weapon_{weapon}");
                SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadesLeft - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadesLeft);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Item;
            if (string.IsNullOrEmpty(weapon) || weapon != "decoy") return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
                SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadesLeft);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            playersWithSkill.TryAdd(player.Index, grenadeLimit);

            SkillUtils.TryGiveWeapon(player, CsItem.DecoyGrenade);
            SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadeLimit);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Index, out _);
            DecoyTracker.RemoveOwner(skillName, player.Index);

            var eventPlayer = PlayerManager.GetPlayerEvent(player);
            if (eventPlayer != null && eventPlayer.IsValid && eventPlayer.Index != player.Index)
                DecoyTracker.RemoveOwner(skillName, eventPlayer.Index);

            SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#00eaff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float triggerRadius = 180, int slownessMultiplier = 5, int grenadeLimit = 3) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float TriggerRadius { get; set; } = triggerRadius;
            public int SlownessMultiplier { get; set; } = slownessMultiplier;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}