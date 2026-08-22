using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Berserker : ISkill
    {
        private const Skills skillName = Skills.Berserker;
        public static readonly ConcurrentDictionary<uint, int> jumpedPlayers = [];
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void NewRound()
        {
            jumpedPlayers.Clear();
        }

        private static float CalculateNewVelocity(CCSPlayerPawn pawn, float maxValue)
        {
            if (pawn!.Health <= 0) return 1f;

            float healthPercentage = pawn.MaxHealth > 0
                ? Math.Clamp((float)pawn.Health / pawn.MaxHealth, 0f, 1f)
                : 0f;

            float newValue = 1f + (maxValue - 1f) * (1f - healthPercentage);
            return Math.Max(1, Math.Min(maxValue, newValue));
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null || damageInfo.Attacker == null || damageInfo.Attacker.Value == null)
                return;

            CCSPlayerPawn attackerPawn = new(damageInfo.Attacker.Value.Handle);
            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);

            if (attackerPawn.DesignerName != "player" || victimPawn.DesignerName != "player")
                return;

            if (SkillUtils.IsFriendlyFireBlocked(skillName, damageInfo, victimPawn)) return;

            if (attackerPawn == null || attackerPawn.Controller?.Value == null || victimPawn == null || victimPawn.Controller?.Value == null)
                return;

            CCSPlayerController attacker = PlayerManager.GetPlayerEvent(attackerPawn.Controller.Value.As<CCSPlayerController>())!;

            var playerInfo = PlayerManager.GetPlayerByIndex(attacker!.Index);
            if (playerInfo == null) return;

            if (playerInfo.Skill == skillName)
            {
                float damageMultiplier = CalculateNewVelocity(attackerPawn, SkillsInfo.GetValue<float>(skillName, "maxDamageVelocity"));
                damageInfo.Damage *= damageMultiplier;
            }
        }

        public static void PlayerJump(EventPlayerJump @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;
            if (!jumpedPlayers.TryGetValue(player.Index, out _)) return;
            jumpedPlayers.AddOrUpdate(player.Index, Server.TickCount + 20, (k, v) => Server.TickCount + 20);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerPawn == null || playerInfo == null) return;
            jumpedPlayers.TryAdd(player.Index, 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null) return;
            playerPawn.VelocityModifier = 1;
            jumpedPlayers.TryRemove(player.Index, out _);
        }

        public static void OnTick()
        {
            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            if (holderBuffer.Count == 0) return;

            float maxSpeedVelocity = SkillsInfo.GetValue<float>(skillName, "maxSpeedVelocity");

            foreach (var player in holderBuffer)
            {
                if (!Instance.IsPlayerValid(player)) continue;

                var playerPawn = player.PlayerPawn?.Value;
                if (playerPawn == null || playerPawn.VelocityModifier == 0) continue;

                var buttons = player.Buttons;
                float newSpeedVelocity = CalculateNewVelocity(playerPawn, maxSpeedVelocity);

                if (buttons.HasFlag(PlayerButtons.Moveleft) || buttons.HasFlag(PlayerButtons.Moveright) || buttons.HasFlag(PlayerButtons.Forward) || buttons.HasFlag(PlayerButtons.Back))
                    playerPawn.VelocityModifier = newSpeedVelocity;

                if (jumpedPlayers.TryGetValue(player.Index, out var time) && time > Server.TickCount)
                    continue;

                if (playerPawn.MoveType == MoveType_t.MOVETYPE_NOCLIP) continue;

                if (!((PlayerFlags)player.Flags).HasFlag(PlayerFlags.FL_ONGROUND))
                    playerPawn.AbsVelocity.Z = Math.Min(playerPawn.AbsVelocity.Z, 10);
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#cc2929", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float maxSpeedVelocity = 2f, float maxDamageVelocity = 2f, bool friendlyFire = true) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float MaxSpeedVelocity { get; set; } = maxSpeedVelocity;
            public float MaxDamageVelocity { get; set; } = maxDamageVelocity;
        public bool FriendlyFire { get; set; } = friendlyFire;
        }
    }
}
