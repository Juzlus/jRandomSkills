using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;

namespace src.player.skills
{
    public class Adrenaline : ISkill
    {
        private const Skills skillName = Skills.Adrenaline;

        private static readonly Dictionary<uint, float> boosted = [];
        private static readonly Dictionary<uint, float> drained = [];
        private static readonly Dictionary<ulong, int> cooldowns = [];
        private static readonly List<uint> expired = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                boosted.Clear();
                drained.Clear();
                cooldowns.Clear();
            }
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lock (setLock)
            {
                boosted.Remove(playerIndex);
                drained.Remove(playerIndex);
            }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            lock (setLock)
            {
                if (!boosted.Remove(player.Key())) return;
            }

            var pawn = player.PlayerPawn?.Value;
            if (pawn != null && pawn.IsValid && pawn.VelocityModifier > 1f)
                pawn.VelocityModifier = 1f;
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            lock (setLock)
            {
                drained.Remove(victim.Key());
                boosted.Remove(victim.Key());
            }
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            if (@event.DmgHealth <= 0 || !SkillUtils.FiresBullets(@event.Weapon)) return;

            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (attacker == null || !attacker.IsValid || victim == null || !victim.IsValid) return;
            if (attacker.Key() == victim.Key() || attacker.Team == victim.Team) return;
            if (PlayerManager.GetPlayer(attacker)?.Skill != skillName) return;

            var victimPawn = victim.PlayerPawn?.Value;
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.Health <= 0) return;

            float steal = Math.Max(0f, SkillsInfo.GetValue<float>(skillName, "stealPerHit"));
            float maxDrain = Math.Clamp(SkillsInfo.GetValue<float>(skillName, "maxSpeedLoss"), 0f, .95f);
            int tick = Server.TickCount;
            ulong pair = (ulong)attacker.Key() << 32 | victim.Key();

            lock (setLock)
            {
                if (cooldowns.TryGetValue(pair, out int readyTick) && tick < readyTick) return;

                drained.TryGetValue(victim.Key(), out float lost);
                float stolen = Math.Min(steal, maxDrain - lost);
                if (stolen <= 0f) return;

                cooldowns[pair] = tick + (int)(Math.Max(0f, SkillsInfo.GetValue<float>(skillName, "cooldown")) * 64);
                drained[victim.Key()] = lost + stolen;
                boosted.TryGetValue(attacker.Key(), out float gained);
                boosted[attacker.Key()] = gained + stolen;
            }
        }

        public static void OnTick()
        {
            lock (setLock)
            {
                if (boosted.Count == 0 && drained.Count == 0) return;

                Apply(boosted, true);
                Apply(drained, false);
            }
        }

        private static void Apply(Dictionary<uint, float> states, bool boost)
        {
            if (states.Count == 0) return;

            expired.Clear();
            foreach (var (key, amount) in states)
            {
                var player = PlayerManager.GetController(key);
                var pawn = player?.PlayerPawn?.Value;
                if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                {
                    expired.Add(key);
                    continue;
                }

                if (boost)
                {
                    float target = 1f + amount;
                    if (IsMoving(player!) && pawn!.VelocityModifier < target)
                        pawn.VelocityModifier = target;
                }
                else
                {
                    float target = 1f - amount;
                    if (pawn!.VelocityModifier > target)
                        pawn.VelocityModifier = target;
                }
            }

            foreach (var key in expired)
                states.Remove(key);
        }

        private static bool IsMoving(CCSPlayerController player)
        {
            var buttons = player.Buttons;
            return buttons.HasFlag(PlayerButtons.Forward) || buttons.HasFlag(PlayerButtons.Back)
                || buttons.HasFlag(PlayerButtons.Moveleft) || buttons.HasFlag(PlayerButtons.Moveright);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff5e3a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float stealPerHit = .2f, float maxSpeedLoss = .6f, float cooldown = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float StealPerHit { get; set; } = stealPerHit;
            public float MaxSpeedLoss { get; set; } = maxSpeedLoss;
            public float Cooldown { get; set; } = cooldown;
        }
    }
}
