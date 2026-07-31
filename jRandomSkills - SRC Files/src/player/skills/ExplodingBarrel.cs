using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class ExplodingBarrel : ISkill
    {
        private const Skills skillName = Skills.ExplodingBarrel;
        private static readonly QAngle angle = new(7, -3, 11);
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly ConcurrentDictionary<uint, byte> consumedBarrels = [];
        private static readonly ConcurrentDictionary<int, (byte Team, uint Owner)> nades = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            jRandomSkills.Instance.AddToManifest(SkillsInfo.GetValue<string>(skillName, "propModel"));
        }

        public static void NewRound()
        {
            ClearAllBarrels();
            lock (setLock)
            {
                SkillPlayerInfo.Clear();
                consumedBarrels.Clear();
                nades.Clear();
            }
        }

        private static void ClearAllBarrels()
        {
            var entities = Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic_override");
            foreach (var entity in entities)
                if (entity != null && entity.IsValid && entity.Entity != null && (entity.Entity.Name?.StartsWith("Barrel_") ?? false))
                    EntityManager.DestroyEntity(entity.Index);
        }

        public static void OnTick()
        {
            if (SkillPlayerInfo.IsEmpty || Server.TickCount % 8 != 0) return;

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;
                if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) continue;

                if (PlayerManager.GetPlayerByIndex(player.Index)?.Skill == skillName)
                    UpdateHUD(player, skillInfo);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.Index, new PlayerSkillInfo
            {
                SteamID = player.Index,
                CanUse = true,
                Cooldown = DateTime.MinValue,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            SkillPlayerInfo.TryRemove(player.Index, out _);
            EntityManager.DestroyPlayerEntities(player.Index);
            SkillUtils.ResetPrintHTML(player);
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            float cooldown = 0;
            if (skillInfo != null)
            {
                float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
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

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo) && skillInfo.CanUse)
            {
                skillInfo.CanUse = false;
                skillInfo.Cooldown = DateTime.Now;
                CreateBarrel(player);
            }
        }

        private static void CreateBarrel(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null)
                return;

            var barrel = EntityManager.CreateTrackedPropOverride(player.Index);
            if (barrel == null || !barrel.IsValid)
                return;

            consumedBarrels.TryRemove(barrel.Index, out _);

            float distance = 50;
            Vector pos = playerPawn.AbsOrigin + SkillUtils.GetForwardVector(playerPawn.AbsRotation) * distance;
            QAngle rotation = new(playerPawn.AbsRotation.X, playerPawn.AbsRotation.Y, playerPawn.AbsRotation.Z);

            barrel.Entity!.Name = barrel.Globalname = $"Barrel_{Server.TickCount}_{player.TeamNum}_{player.Index}";
            barrel.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
            barrel.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(barrel.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            barrel.DispatchSpawn();

            Server.NextFrame(() =>
            {
                if (barrel == null || !barrel.IsValid) return;
                barrel.SetModel(SkillsInfo.GetValue<string>(skillName, "propModel"));
                barrel.Teleport(pos, rotation, null);
            });
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
                    heProjectile.Globalname = $"barrel_team_{source.Team}_{source.Owner}_{heProjectile.Index}";
            });
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null) return;

            if (HandleBarrelHit(param)) return;
            HandleNadeDamage(param, param2);
        }

        private static bool HandleBarrelHit(CEntityInstance param)
        {
            if (string.IsNullOrEmpty(param.Entity!.Name)) return false;
            if (!param.Entity.Name.StartsWith("Barrel_")) return false;

            var barrel = param.As<CDynamicProp>();
            if (barrel == null || !barrel.IsValid) return true;
            if (!consumedBarrels.TryAdd(barrel.Index, 0)) return true;

            var nameParts = param.Entity.Name.Split('_');
            if (nameParts.Length < 4) return true;
            if (!int.TryParse(nameParts[2], out int teamNum)) return true;
            if (!uint.TryParse(nameParts[3], out uint ownerIndex)) return true;

            if (barrel.AbsOrigin == null) return true;

            float x = barrel.AbsOrigin.X;
            float y = barrel.AbsOrigin.Y;
            float z = barrel.AbsOrigin.Z + 32;

            EntityManager.DestroyEntity(barrel.Index, 0f);

            jRandomSkills.Instance.AddTickTimer(4, () =>
            {
                SkillUtils.CreateHEGrenadeProjectile(new Vector(x, y, z), angle, new Vector(0, 0, -10), teamNum);
                nades.AddOrUpdate(Server.TickCount, ((byte)teamNum, ownerIndex), (_, _) => ((byte)teamNum, ownerIndex));
            });

            return true;
        }

        private static void HandleNadeDamage(CEntityInstance param, CTakeDamageInfo param2)
        {
            var nade = param2.Attacker?.Value;
            if (nade == null || !nade.IsValid) return;
            if (nade.DesignerName != "hegrenade_projectile") return;
            if (string.IsNullOrEmpty(nade.Globalname) || !nade.Globalname.StartsWith("barrel_team_")) return;

            var parts = nade.Globalname.Split('_');
            if (parts.Length < 4) return;
            if (!int.TryParse(parts[2], out int nadeTeam)) return;
            if (!uint.TryParse(parts[3], out uint ownerIndex)) return;

            CCSPlayerPawn victimPawn = new(param.Handle);
            if (victimPawn.DesignerName != "player" || victimPawn.Controller?.Value == null) return;

            var victim = victimPawn.Controller.Value.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid || victim.Index == ownerIndex) return;

            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex);
            if (owner != null && !owner.IsValid) owner = null;

            if (victimPawn.TeamNum == nadeTeam)
            {
                float reduction = SkillsInfo.GetValue<float>(skillName, "dmgReductionForTeamates");
                param2.Damage *= 1f - Math.Clamp(reduction, 0f, 1f);

                if (IsFriendlyFireOff())
                {
                    int teamDamage = (int)param2.Damage;
                    param2.Damage = 0;

                    if (teamDamage > 0)
                        SkillUtils.TakeHealth(victimPawn, teamDamage, owner, KillfeedIcons.Explosion);

                    return;
                }
            }

            if (owner != null && param2.Damage >= victimPawn.Health)
                SkillUtils.RegisterKillCredit(victim.Index, owner.Index, KillfeedIcons.Explosion);
        }

        private static bool IsFriendlyFireOff()
        {
            bool ff = ConVar.Find("mp_friendlyfire")?.GetPrimitiveValue<bool>() ?? false;
            bool tae = ConVar.Find("mp_teammates_are_enemies")?.GetPrimitiveValue<bool>() ?? false;
            return !ff && !tae;
        }

        private static bool NearlyEquals(float a, float b, float epsilon = 0.001f) => Math.Abs(a - b) < epsilon;

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#c0392b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Common, float cooldown = 20f, float explosionRadius = 210f, int explosionDamage = 50, string propModel = "models/props/de_train/hr_t/barrel_a/barrel_a.vmdl", float dmgReductionForTeamates = 0.5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float ExplosionRadius { get; set; } = explosionRadius;
            public int ExplosionDamage { get; set; } = explosionDamage;
            public string PropModel { get; set; } = propModel;
            public float DmgReductionForTeamates { get; set; } = dmgReductionForTeamates;
        }
    }
}
