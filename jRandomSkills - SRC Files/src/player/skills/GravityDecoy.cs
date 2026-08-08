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

        private const float defaultGravity = 1f;

        private static readonly ConcurrentDictionary<Vector, byte> decoys = [];
        private static readonly ConcurrentDictionary<uint, int> playersWithSkill = [];
        private static readonly ConcurrentDictionary<uint, byte> affected = [];
        private static readonly ConcurrentDictionary<uint, byte> restoreOnRespawn = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            DecoyRing.PreloadAssets();
        }

        public static void NewRound()
        {
            RestoreAll();
            decoys.Clear();
            DecoyRing.ClearAll(skillName);
        }

        public static void RoundEnd()
        {
            RestoreAll();
            decoys.Clear();
            DecoyRing.ClearAll(skillName);
        }

        public static void DecoyStarted(EventDecoyStarted @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            Vector pos = new(@event.X, @event.Y, @event.Z);
            decoys.TryAdd(pos, 0);
            DecoyRing.Show(skillName, (uint)@event.Entityid, pos, SkillsInfo.GetValue<float>(skillName, "triggerRadius"));
        }

        public static void DecoyDetonate(EventDecoyDetonate @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            foreach (var decoy in decoys.Keys.Where(v => v.X == @event.X && v.Y == @event.Y && v.Z == @event.Z))
                decoys.TryRemove(decoy, out _);

            DecoyRing.Hide(skillName, (uint)@event.Entityid);

            if (decoys.IsEmpty) RestoreAll();
        }

        public static void OnTick()
        {
            if (decoys.IsEmpty && affected.IsEmpty && restoreOnRespawn.IsEmpty) return;

            if (decoys.IsEmpty && !affected.IsEmpty) RestoreAll();

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

                if (restoreOnRespawn.TryRemove(eventPlayer.Index, out _))
                    pawn.ActualGravityScale = defaultGravity;

                bool inside = !decoys.IsEmpty && decoys.Keys.Any(d => SkillUtils.GetDistance(d, pawn.AbsOrigin) <= radius);

                if (inside)
                {
                    if (!affected.TryAdd(eventPlayer.Index, 0)) continue;
                    pawn.ActualGravityScale = gravity;
                }
                else if (affected.TryRemove(eventPlayer.Index, out _))
                    pawn.ActualGravityScale = defaultGravity;
            }
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            if (!affected.TryRemove(victim.Index, out _)) return;

            Restore(victim.Index);
            restoreOnRespawn[victim.Index] = 0;
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            affected.TryRemove(playerIndex, out _);
            restoreOnRespawn.TryRemove(playerIndex, out _);
            playersWithSkill.TryRemove(playerIndex, out _);
        }

        private static void RestoreAll()
        {
            foreach (uint playerIndex in affected.Keys)
                Restore(playerIndex);

            foreach (uint playerIndex in restoreOnRespawn.Keys)
                Restore(playerIndex);

            affected.Clear();
            restoreOnRespawn.Clear();
        }

        private static void Restore(uint playerIndex)
        {
            var player = Utilities.GetPlayerFromIndex((int)playerIndex);
            var pawn = player?.PlayerPawn?.Value;

            if (pawn != null && pawn.IsValid)
                pawn.ActualGravityScale = defaultGravity;
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
