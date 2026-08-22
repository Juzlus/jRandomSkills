using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Replicator : ISkill
    {
        private const Skills skillName = Skills.Replicator;
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly ConcurrentDictionary<uint, byte> consumedReplicas = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            ClearAllReplicas();
            lock (setLock)
            {
                SkillPlayerInfo.Clear();
                consumedReplicas.Clear();
            }
        }

        private static void ClearAllReplicas()
        {
            var entities = Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic_override");
            foreach (var entity in entities)
                if (entity != null && entity.IsValid && entity.Entity != null && !string.IsNullOrEmpty(entity.Entity.Name) && (entity.Entity.Name?.StartsWith("Replica_") ?? false))
                    EntityManager.DestroyEntity(entity.Index);
        }

        public static void OnTick()
        {
            if (!SkillUtils.IsHudFrame()) return;
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill == skillName)
                    if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
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
            if (player == null || !player.IsValid) return;
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
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
            {
                if (!player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
                if (skillInfo.CanUse)
                {
                    skillInfo.CanUse = false;
                    skillInfo.Cooldown = DateTime.Now;
                    CreateReplica(player);
                }
            }
        }

        private static void CreateReplica(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null)
                return;

            var replica = EntityManager.CreateTrackedPropOverride(player.Index);
            if (replica == null || !replica.IsValid)
                return;

            consumedReplicas.TryRemove(replica.Index, out _);

            replica.Flags = playerPawn.Flags;
            replica.Flags |= (uint)Flags_t.FL_DUCKING;
            replica.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
            replica.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(replica.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            replica.SetModel(playerPawn!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
            replica.Entity!.Name = replica.Globalname = $"Replica_{Server.TickCount}_{(player.Team == CsTeam.CounterTerrorist ? "CT" : "TT")}";

            replica.UseAnimGraph = true;
            replica.AnimGraphUpdateEnabled = true;
            bool isDucking = ((PlayerFlags)playerPawn.Flags).HasFlag(PlayerFlags.FL_DUCKING);

            float distance = 40;
            Vector pos = playerPawn.AbsOrigin + SkillUtils.GetForwardVector(playerPawn.AbsRotation) * distance;

            replica.Teleport(pos, playerPawn.AbsRotation, null);
            replica.DispatchSpawn();
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damagedEntity == null || damagedEntity.Entity == null || damageInfo == null || damageInfo.Attacker == null || damageInfo.Attacker.Value == null)
                return;

            if (string.IsNullOrEmpty(damagedEntity.Entity.Name)) return;
            if (!damagedEntity.Entity.Name.StartsWith("Replica_")) return;

            var replica = damagedEntity.As<CPhysicsPropMultiplayer>();
            if (replica == null || !replica.IsValid) return;

            if (!consumedReplicas.TryAdd(replica.Index, 0)) return;

            replica.EmitSound("GlassBottle.BulletImpact", volume: SkillsInfo.GetValue<float>(skillName, "soundVolume"));

            var owner = GetReplicaOwner(replica.Index);
            EntityManager.DestroyEntity(replica.Index);

            CCSPlayerPawn attackerPawn = new(damageInfo.Attacker.Value.Handle);
            if (attackerPawn.DesignerName != "player")
                return;

            var attackerTeam = attackerPawn.TeamNum;
            var replicaTeam = replica.Globalname.EndsWith("CT") ? 3 : 2;

            SkillUtils.TakeHealth(attackerPawn, attackerTeam != replicaTeam ? SkillsInfo.GetValue<int>(skillName, "EnemyTeamDamage") : SkillsInfo.GetValue<int>(skillName, "YourTeamDamage"), owner, KillfeedIcons.Player);
        }

        private static CCSPlayerController? GetReplicaOwner(uint replicaIndex)
        {
            var ownerIndex = EntityManager.GetEntityOwner(replicaIndex);
            if (ownerIndex == null) return null;

            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex.Value);
            return owner != null && owner.IsValid ? owner : null;
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#a3000b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Common, float cooldown = 15f, int yourTeamDamage = 10, int enemyTeamDamage = 20, float soundVolume = 1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float SoundVolume { get; set; } = soundVolume;
            public float Cooldown { get; set; } = cooldown;
            public int YourTeamDamage { get; set; } = yourTeamDamage;
            public int EnemyTeamDamage { get; set; } = enemyTeamDamage;
        }
    }
}
