using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class MagneticDecoy : ISkill
    {
        private const Skills skillName = Skills.MagneticDecoy;
        private static readonly ConcurrentDictionary<Vector, byte> decoys = [];
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];

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
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            decoys.TryAdd(new Vector(@event.X, @event.Y, @event.Z), 0);
        }

        public static void DecoyDetonate(EventDecoyDetonate @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            foreach (var decoy in decoys.Keys.Where(v => v.X == @event.X && v.Y == @event.Y && v.Z == @event.Z))
                decoys.TryRemove(decoy, out _);
        }

        public static void OnTick()
        {
            foreach (Vector decoyPos in decoys.Keys)
                foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist))
                {
                    var eventPlayer = PlayerManager.GetPlayerEvent(player);
                    if (eventPlayer == null || !eventPlayer.IsValid) continue;

                    var decoyRadius = SkillsInfo.GetValue<float>(skillName, "triggerRadius");

                    var pawn = eventPlayer.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) continue;

                    double distance = SkillUtils.GetDistance(decoyPos, pawn.AbsOrigin);
                    if (distance <= decoyRadius &&  distance > 10)
                    {
                        Vector direction = new(decoyPos.X - pawn.AbsOrigin.X, decoyPos.Y - pawn.AbsOrigin.Y, 0);
                        float length = direction.Length();

                        Vector normalized = direction / length;
                        float ratio = 1 - (float)(distance / decoyRadius);
                        float strenght = SkillsInfo.GetValue<float>(skillName, "strenght") * ratio;

                        pawn.AbsVelocity.X += normalized.X * strenght;
                        pawn.AbsVelocity.Y += normalized.Y * strenght;
                    }
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
            SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#81f0c4", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float triggerRadius = 180, float strenght = 30, int grenadeLimit = 3) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float TriggerRadius { get; set; } = triggerRadius;
            public float Strenght { get; set; } = strenght;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}