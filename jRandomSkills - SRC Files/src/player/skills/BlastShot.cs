using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    public class BlastShot : ISkill
    {
        private const Skills skillName = Skills.BlastShot;
        private static readonly QAngle angle = new(13, -5, 5);
        private static readonly ConcurrentDictionary<int, (byte Team, uint Owner)> nades = [];
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly string[] allowedWeapons = ["weapon_mp5sd"];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            SkillPlayerInfo.Clear();
            nades.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.Index, new PlayerSkillInfo
            {
                SteamID = player.Index,
                CanUse = true,
                Cooldown = DateTime.MinValue,
            });
            SkillUtils.TryGiveWeapon(player, CsItem.MP5SD);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void OnTick()
        {
            if (SkillPlayerInfo.IsEmpty) return;

            float cooldown = SkillsInfo.GetValue<float>(skillName, "cooldown");

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill != skillName) continue;

                var playerPawn = player.PlayerPawn?.Value;
                if (playerPawn == null || !playerPawn.IsValid) continue;

                if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) continue;

                var buttons = player.Buttons;
                bool isAttack2 = (buttons & PlayerButtons.Attack2) != 0;

                if (skillInfo.CanUse && isAttack2)
                {
                    var weapon = playerPawn.WeaponServices?.ActiveWeapon?.Value;
                    
                    if (weapon == null || !weapon.IsValid) continue;
                    if (!allowedWeapons.Contains(SkillUtils.GetDesignerName(weapon))) continue;

                    skillInfo.CanUse = false;
                    skillInfo.Cooldown = DateTime.Now;
                    SpawnExplosion(player);
                }

                UpdateHUD(player, skillInfo, cooldown);
            }
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo, float cooldownSkill)
        {
            float cooldown = 0;
            if (skillInfo != null)
            {
                float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(cooldownSkill) - DateTime.Now).TotalSeconds);
                cooldown = Math.Max(time, 0);

                if (cooldown == 0 && skillInfo?.CanUse == false)
                    skillInfo.CanUse = true;
            }

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        private static void SpawnExplosion(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            float force = SkillsInfo.GetValue<float>(skillName, "force");

            Vector pos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            Vector vel = SkillUtils.GetForwardVector(pawn.EyeAngles) * force;

            SkillUtils.CreateHEGrenadeProjectile(pos, angle, vel, player.TeamNum);
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

                if (nades.TryRemove(lastTick, out var source))
                    heProjectile.Globalname = $"blastshot_team_{source.Team}_{source.Owner}_{heProjectile.Index}";
            });
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null) return;

            var nade = damageInfo.Attacker.Value;
            if (nade == null || !nade.IsValid) return;

            if (nade.DesignerName != "hegrenade_projectile") return;
            if (string.IsNullOrEmpty(nade.Globalname) || !nade.Globalname.StartsWith("blastshot_team_")) return;

            var parts = nade.Globalname.Split('_');
            if (parts.Length < 4) return;
            if (!int.TryParse(parts[2], out int nadeTeam)) return;
            if (!uint.TryParse(parts[3], out uint ownerIndex)) return;

            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);

            if (victimPawn.DesignerName != "player") return;
            if (victimPawn.Controller?.Value == null) return;

            var victim = victimPawn.Controller.Value.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid || victim.Index == ownerIndex) return;

            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex);
            if (owner != null && !owner.IsValid) owner = null;

            if (victimPawn.TeamNum == nadeTeam)
            {
                if (!SkillsInfo.GetValue<bool>(skillName, "friendlyFire"))
                {
                    damageInfo.Damage = 0;
                    return;
                }

                damageInfo.Damage *= SkillUtils.GetTeamDamageMultiplier(skillName);
            }

            if (owner != null && SkillUtils.IsPredictedLethal(damageInfo, victimPawn))
                SkillUtils.RegisterKillCredit(victim.Index, owner.Index, KillfeedIcons.Explosion);
        }

        private static bool NearlyEquals(float a, float b, float epsilon = 0.001f) => Math.Abs(a - b) < epsilon;

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7740c9", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float explosionRadius = 400.0f, int explosionDamage = 60, float dmgReductionForTeamates = 0.5f, float cooldown = 10f, float force = 1000f, bool friendlyFire = true) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float ExplosionRadius { get; set; } = explosionRadius;
            public int ExplosionDamage { get; set; } = explosionDamage;
            public float DmgReductionForTeamates { get; set; } = dmgReductionForTeamates;
            public float Cooldown { get; set; } = cooldown;
            public float Force { get; set; } = force;
        public bool FriendlyFire { get; set; } = friendlyFire;
        }
    }
}
