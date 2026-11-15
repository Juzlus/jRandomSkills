using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class BreachZone : ISkill
    {
        private const Skills skillName = Skills.BreachZone;
        private readonly static ConcurrentDictionary<uint, int> playersWithSkill = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            DecoyRing.PreloadAssets();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            playersWithSkill.TryRemove(playerIndex, out _);
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

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            Vector pos = new(@event.X, @event.Y, @event.Z);
            DecoyTracker.Add(skillName, (uint)@event.Entityid, pos, player.Key());
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
            bool onlyForTeammates = SkillsInfo.GetValue<bool>(skillName, "onlyForTeammates");

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

                    bool decoyTeam = DecoyTracker.GetPositionTeam(skillName, decoyPos) == (CsTeam)pawn.TeamNum;
                    if (onlyForTeammates && !decoyTeam) continue;

                    double distance = SkillUtils.GetDistance(decoyPos, origin);
                    if (distance > decoyRadius) continue;

                    if (pawn.HealthShotBoostExpirationTime - 1 > Server.CurrentTime) continue;
                    pawn.HealthShotBoostExpirationTime = Server.CurrentTime + 3;
                    NetStateFast.Changed(pawn, "CCSPlayerPawn", "m_flHealthShotBoostExpirationTime");
                }
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null || damageInfo.Attacker == null || damageInfo.Attacker.Value == null)
                return;

            CCSPlayerPawn attackerPawn = new(damageInfo.Attacker.Value.Handle);
            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);

            if (attackerPawn.DesignerName != "player" || victimPawn.DesignerName != "player")
                return;

            var decoyPositions = DecoyTracker.Positions(skillName);
            if (decoyPositions.Length == 0) return;

            if (SkillUtils.IsFriendlyFireBlocked(skillName, damageInfo, victimPawn)) return;

            if (attackerPawn == null || attackerPawn.Controller?.Value == null || victimPawn == null || victimPawn.Controller?.Value == null)
                return;

            CCSPlayerController attacker = PlayerManager.GetPlayerEvent(attackerPawn.Controller.Value.As<CCSPlayerController>())!;

            float decoyRadius = SkillsInfo.GetValue<float>(skillName, "triggerRadius");
            float damageMultiplier = SkillsInfo.GetValue<float>(skillName, "damageMultiplier");
            float resistanceMultiplier = SkillsInfo.GetValue<float>(skillName, "resistanceMultiplier");
            bool onlyForTeammates = SkillsInfo.GetValue<bool>(skillName, "onlyForTeammates");

            var victimOrigin = victimPawn.AbsOrigin;
            var attackerOrigin = attackerPawn.AbsOrigin;
            if (victimOrigin == null || attackerOrigin == null) return;

            bool applyDamageMultiplier = false;
            bool applyResistanceMultiplier = false;

            bool isVictimTeam = false;
            bool isAttackerTeam = false;

            foreach (Vector decoyPos in decoyPositions)
            {
                CsTeam? decoyTeam = DecoyTracker.GetPositionTeam(skillName, decoyPos);
                isVictimTeam = decoyTeam == (CsTeam)victimPawn.TeamNum;
                isAttackerTeam = decoyTeam == (CsTeam)attackerPawn.TeamNum;

                double victimDistance = SkillUtils.GetDistance(decoyPos, victimOrigin);
                double attackerDistance = SkillUtils.GetDistance(decoyPos, attackerOrigin);

                if (victimDistance <= decoyRadius && (isVictimTeam || !onlyForTeammates))
                    applyResistanceMultiplier = true;

                if (attackerDistance <= decoyRadius && (isAttackerTeam || !onlyForTeammates))
                    applyDamageMultiplier = true;

                if (applyDamageMultiplier && applyResistanceMultiplier)
                    break;
            }

            if (applyDamageMultiplier)
                damageInfo.Damage *= damageMultiplier;

            if (applyResistanceMultiplier)
                damageInfo.Damage *= resistanceMultiplier;
        }

        public static void GrenadeThrown(EventGrenadeThrown @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Weapon;
            if (weapon != "decoy") return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft) && grenadesLeft > 1)
            {
                playersWithSkill[player.Key()] = grenadesLeft - 1;
                SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadesLeft - 1);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft) && grenadesLeft > 1 && SkillUtils.ThrewGrenadeRecently(player))
                SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadesLeft);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var weapon = @event.Item;
            if (string.IsNullOrEmpty(weapon) || weapon != "decoy") return;

            if (playersWithSkill.TryGetValue(player.Key(), out int grenadesLeft))
                playersWithSkill[player.Key()] = SkillUtils.ClampGrenadeAfterPickup(player, CsItem.DecoyGrenade, grenadesLeft);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillUtils.SetSkillHud(player, "breachzone_hud", [
                GetPercentage("damageMultiplier"),
                -GetPercentage("resistanceMultiplier")
            ]);

            int grenadeLimit = SkillsInfo.GetValue<int>(skillName, "grenadeLimit");
            SkillUtils.TryGiveWeapon(player, CsItem.DecoyGrenade);
            playersWithSkill[player.Key()] = grenadeLimit;

            SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, grenadeLimit);
        }

        private static int GetPercentage(string key)
        {
            float value = SkillsInfo.GetValue<float>(skillName, key);
            return (int)Math.Round((value - 1) * 100);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            playersWithSkill.TryRemove(player.Key(), out _);
            DecoyTracker.RemoveOwner(skillName, player.Key());

            var eventPlayer = PlayerManager.GetPlayerEvent(player);
            if (eventPlayer != null && eventPlayer.IsValid && eventPlayer.Key() != player.Key())
                DecoyTracker.RemoveOwner(skillName, eventPlayer.Key());

            SkillUtils.UpdateGrenadeCount(player, CsItem.DecoyGrenade, 1);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#00abff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float triggerRadius = 180, float damageMultiplier = 1.5f, float resistanceMultiplier = 0.75f, bool onlyForTeammates = false, int grenadeLimit = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float TriggerRadius { get; set; } = triggerRadius;
            public bool OnlyForTeammates { get; set; } = onlyForTeammates;
            public float DamageMultiplier { get; set; } = damageMultiplier;
            public float ResistanceMultiplier { get; set; } = resistanceMultiplier;
            public int GrenadeLimit { get; set; } = grenadeLimit;
        }
    }
}