using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class DeathBomb : ISkill
    {
        private const Skills skillName = Skills.DeathBomb;
        private static readonly string[] voiceLines = ["balkan.radiobotfallback01", "balkan.radiobotfallback02", "balkan.radiobotfallback04"];
        private static readonly QAngle angle = new(10, -5, 9);
        private static readonly ConcurrentDictionary<int, (byte Team, uint Owner)> nades = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            nades.Clear();
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!IsDeadPlayerValid(player)) return;

            CsTeam lastTeam = player!.Team;

            Server.NextWorldUpdate(() =>
            {

                if (player == null || !player.IsValid || player.Team != lastTeam) return;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.Health == pawn.MaxHealth) return;

                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill == skillName)
                    SpawnExplosion(player!);
            });
        }

        private static void SpawnExplosion(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            Vector pos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
            pos.Z += 10;

            SkillUtils.CreateHEGrenadeProjectile(pos, angle, new Vector(0, 0, -10), player.TeamNum);

            foreach (var _p in PlayerManager.GetTickPlayers().Where(p => p.IsValid && p.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist))
                SkillUtils.PrintToChat(_p, $"{ChatColors.DarkRed}\u202A{player.PlayerName}\u202C: {ChatColors.Lime}{_p.GetTranslation("deathbomb_explosion")}",
                    border: !PlayerManager.GetTickPlayers().Any(p => p.Team == player.Team && p != player) ? "tb" : "t");

            player.EmitSound(voiceLines[Random.Shared.Next(voiceLines.Length)], volume: SkillsInfo.GetValue<float>(skillName, "soundVolume"));

            nades.AddOrUpdate(Server.TickCount, (player.TeamNum, player.Index), (_, _) => (player.TeamNum, player.Index));
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            if (entity.DesignerName != "hegrenade_projectile") return;

            var heProjectile = entity.As<CBaseCSGrenadeProjectile>();
            if (heProjectile == null || !heProjectile.IsValid || heProjectile.AbsRotation == null) return;

            int lastTick = Server.TickCount;

            Server.NextFrame(() =>
            {
                if (heProjectile == null || !heProjectile.IsValid) return;
                if (!(NearlyEquals(angle.X, heProjectile.AbsRotation.X) && NearlyEquals(angle.Y, heProjectile.AbsRotation.Y) && NearlyEquals(angle.Z, heProjectile.AbsRotation.Z)))
                    return;

                heProjectile.TicksAtZeroVelocity = 100;
                heProjectile.Damage = SkillsInfo.GetValue<int>(skillName, "explosionDamage");
                heProjectile.DmgRadius = SkillsInfo.GetValue<float>(skillName, "explosionRadius");
                heProjectile.DetonateTime = 0;

                if (nades.TryRemove(lastTick, out var source))
                    heProjectile.Globalname = $"deathbomb_team_{source.Team}_{source.Owner}_{heProjectile.Index}";
            });
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null) return;

            var nade = param2.Attacker.Value;
            if (nade == null || !nade.IsValid) return;

            if (nade.DesignerName != "hegrenade_projectile") return;
            if (string.IsNullOrEmpty(nade.Globalname) || !nade.Globalname.StartsWith("deathbomb_team_")) return;

            var parts = nade.Globalname.Split('_');
            if (parts.Length < 4) return;
            if (!int.TryParse(parts[2], out int nadeTeam)) return;
            if (!uint.TryParse(parts[3], out uint ownerIndex)) return;

            CCSPlayerPawn victimPawn = new(param.Handle);

            if (victimPawn.DesignerName != "player") return;
            if (victimPawn.Controller?.Value == null) return;

            var victim = victimPawn.Controller.Value.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid || victim.Index == ownerIndex) return;

            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex);
            if (owner != null && !owner.IsValid) owner = null;

            if (victimPawn.TeamNum == nadeTeam)
                param2.Damage *= SkillUtils.GetTeamDamageMultiplier(skillName);

            if (owner != null && param2.Damage >= victimPawn.Health)
                SkillUtils.RegisterKillCredit(victim.Index, owner.Index, KillfeedIcons.Explosion);
        }

        private static bool NearlyEquals(float a, float b, float epsilon = 0.001f) => Math.Abs(a - b) < epsilon;

        private static bool IsDeadPlayerValid(CCSPlayerController? player)
        {
            return player != null && player.IsValid && player.PlayerPawn?.Value != null;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#F5CB42", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float explosionRadius = 500.0f, int explosionDamage = 999, float dmgReductionForTeamates = 0.5f, float soundVolume = 1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float SoundVolume { get; set; } = soundVolume;
            public float ExplosionRadius { get; set; } = explosionRadius;
            public int ExplosionDamage { get; set; } = explosionDamage;
            public float DmgReductionForTeamates { get; set; } = dmgReductionForTeamates;
        }
    }
}