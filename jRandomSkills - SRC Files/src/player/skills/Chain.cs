using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Chain : ISkill
    {
        private const Skills skillName = Skills.Chain;
        private const string tracerParticle = "particles/weapons/cs_weapon_fx/weapon_tracers_taser.vpcf";
        private const float tracerLifetime = .3f;
        private static readonly ConcurrentDictionary<(uint attacker, uint victim), (int total, int cap)> pending = [];
        private static bool applying;

        public static void LoadSkill()
        {
            Localization.RegisterDescriptionArgs(skillName, () => [(object)(int)MathF.Round(SkillsInfo.GetValue<float>(skillName, "chainPercent") * 100f), (object)SkillsInfo.GetValue<float>(skillName, "chainRange")]);
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(tracerParticle);
        }

        public static void NewRound()
        {
            pending.Clear();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            foreach (var key in pending.Keys)
                if (key.attacker == playerIndex || key.victim == playerIndex)
                    pending.TryRemove(key, out _);
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            if (applying) return;

            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(attacker) || victim == null || !victim.IsValid || attacker == victim) return;

            var playerInfo = PlayerManager.GetPlayer(attacker);
            if (playerInfo?.Skill != skillName) return;

            if (!SkillUtils.FiresBullets(@event.Weapon)) return;

            int dmgHealth = @event.DmgHealth;
            if (dmgHealth <= 0) return;

            uint attackerKey = attacker!.Key();
            uint victimKey = victim.Key();

            int cap = SkillUtils.CapToVictimHealth(victim, int.MaxValue);
            bool first = true;
            pending.AddOrUpdate((attackerKey, victimKey), (dmgHealth, cap), (_, old) =>
            {
                first = false;
                return (old.total + dmgHealth, old.cap);
            });
            if (!first) return;

            Server.NextFrame(() =>
            {
                if (!pending.TryRemove((attackerKey, victimKey), out var entry)) return;
                Apply(attackerKey, victimKey, Math.Min(entry.total, entry.cap));
            });
        }

        private static void Apply(uint attackerKey, uint victimKey, int totalDamage)
        {
            var attacker = PlayerManager.GetController(attackerKey);
            var victim = PlayerManager.GetController(victimKey);
            if (attacker == null || !attacker.IsValid || victim == null || !victim.IsValid) return;

            var victimPawn = victim.PlayerPawn.Value;
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.AbsOrigin == null) return;

            var attackerInfo = PlayerManager.GetPlayer(attacker);
            if (attackerInfo?.Skill != skillName) return;

            int damage = Math.Max(1, (int)MathF.Round(totalDamage * SkillsInfo.GetValue<float>(skillName, "chainPercent")));
            float range = SkillsInfo.GetValue<float>(skillName, "chainRange");
            float rangeSq = range * range;
            bool ffa = SkillUtils.CvarValue("mp_teammates_are_enemies", false);
            bool teamDamage = SkillsInfo.GetValue<bool>(skillName, "friendlyFire") && SkillUtils.CvarValue("mp_friendlyfire", false);

            float vx = victimPawn.AbsOrigin.X, vy = victimPawn.AbsOrigin.Y, vz = victimPawn.AbsOrigin.Z;
            Vector start = new(vx, vy, vz + 50);

            applying = true;
            try
            {
                foreach (var target in PlayerManager.GetTickPlayers())
                {
                    if (target == null || !target.IsValid) continue;
                    uint targetKey = target.Key();
                    if (targetKey == attackerKey || targetKey == victimKey) continue;

                    var targetPawn = target.PlayerPawn.Value;
                    if (targetPawn == null || !targetPawn.IsValid || targetPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || targetPawn.AbsOrigin == null) continue;

                    bool sameTeam = target.Team == attacker.Team;
                    if (sameTeam && !ffa && !teamDamage) continue;

                    float dx = targetPawn.AbsOrigin.X - vx, dy = targetPawn.AbsOrigin.Y - vy, dz = targetPawn.AbsOrigin.Z - vz;
                    if (dx * dx + dy * dy + dz * dz > rangeSq) continue;

                    Vector end = new(targetPawn.AbsOrigin.X, targetPawn.AbsOrigin.Y, targetPawn.AbsOrigin.Z + 50);
                    EntityManager.CreateTracer(attackerKey, tracerParticle, start, end, tracerLifetime);
                    SkillUtils.TakeHealth(targetPawn, damage, attacker, KillfeedIcons.Fist);
                }
            }
            finally
            {
                applying = false;
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7e57c2", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, float chainPercent = .25f, float chainRange = 400f, bool friendlyFire = false) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float ChainPercent { get; set; } = chainPercent;
            public float ChainRange { get; set; } = chainRange;
            public bool FriendlyFire { get; set; } = friendlyFire;
        }
    }
}
