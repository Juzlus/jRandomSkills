using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class GravityDecoy : ISkill
    {
        private const Skills skillName = Skills.GravityDecoy;

        private static readonly ConcurrentDictionary<uint, int> playersWithSkill = [];
        private static readonly ConcurrentDictionary<uint, float> affected = [];
        private static readonly ConcurrentDictionary<uint, float> restoreOnRespawn = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            DecoyRing.PreloadAssets();
        }

        public static void NewRound()
        {
            RestoreAll();
            DecoyTracker.Clear(skillName);
        }

        public static void RoundEnd()
        {
            RestoreAll();
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

            if (DecoyTracker.IsEmpty(skillName)) RestoreAll();
        }

        public static void OnTick()
        {
            var decoyPositions = DecoyTracker.Positions(skillName);

            if (decoyPositions.Length == 0 && affected.IsEmpty && restoreOnRespawn.IsEmpty) return;

            if (decoyPositions.Length == 0 && !affected.IsEmpty) RestoreAll();

            float radius = SkillsInfo.GetValue<float>(skillName, "triggerRadius");
            float gravity = SkillsInfo.GetValue<float>(skillName, "gravityScale");

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;
                if (player.Team is not (CsTeam.CounterTerrorist or CsTeam.Terrorist)) continue;

                var eventPlayer = PlayerManager.GetPlayerEvent(player);
                if (eventPlayer == null || !eventPlayer.IsValid) continue;

                var pawn = eventPlayer.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) continue;
                if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                if (restoreOnRespawn.TryRemove(eventPlayer.Index, out float respawnGravity))
                    pawn.ActualGravityScale = respawnGravity;

                bool inside = false;
                foreach (var decoyPos in decoyPositions)
                    if (SkillUtils.GetDistance(decoyPos, pawn.AbsOrigin) <= radius) { inside = true; break; }

                if (inside)
                {
                    if (!affected.TryAdd(eventPlayer.Index, pawn.ActualGravityScale)) continue;
                    pawn.ActualGravityScale = gravity;
                }
                else if (affected.TryRemove(eventPlayer.Index, out float ownGravity))
                    pawn.ActualGravityScale = ownGravity;
            }
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            if (!affected.TryRemove(victim.Index, out float ownGravity)) return;

            Restore(victim.Index, ownGravity);
            restoreOnRespawn[victim.Index] = ownGravity;
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            affected.TryRemove(playerIndex, out _);
            restoreOnRespawn.TryRemove(playerIndex, out _);
            playersWithSkill.TryRemove(playerIndex, out _);
        }

        private static void RestoreAll()
        {
            foreach (var entry in affected)
                Restore(entry.Key, entry.Value);

            foreach (var entry in restoreOnRespawn)
                Restore(entry.Key, entry.Value);

            affected.Clear();
            restoreOnRespawn.Clear();
        }

        private static void Restore(uint playerIndex, float gravity)
        {
            var player = Utilities.GetPlayerFromIndex((int)playerIndex);
            var pawn = player?.PlayerPawn?.Value;

            if (pawn != null && pawn.IsValid)
                pawn.ActualGravityScale = gravity;
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            if (@event.Weapon != "decoy") return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Index, out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Index] = grenadesLeft - 1;
                player!.GiveNamedItem("weapon_decoy");
                SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadesLeft - 1);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            playersWithSkill[player.Index] = grenadeLimit;

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

            if (DecoyTracker.IsEmpty(skillName)) RestoreAll();

            SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#a06cd5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float triggerRadius = 180f, float gravityScale = .5f, int grenadeLimit = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float TriggerRadius { get; set; } = triggerRadius;
            public float GravityScale { get; set; } = gravityScale;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}
