using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills
{
    public class Illusionist : ISkill
    {
        private const Skills skillName = Skills.Illusionist;
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly ConcurrentDictionary<int, Timer> ActiveTimers = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            ClearAllReplicas();
            lock (setLock)
                SkillPlayerInfo.Clear();
        }

        private static void ClearAllReplicas()
        {
            foreach (var timer in ActiveTimers.Values) timer?.Kill();
            ActiveTimers.Clear();

            var entities = Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic_override");
            foreach (var entity in entities)
                if (entity != null && entity.IsValid && entity.Entity != null && !string.IsNullOrEmpty(entity.Entity.Name) && (entity.Entity.Name?.StartsWith("Illusionist_") ?? false))
                    EntityManager.DestroyEntity(entity.Index);
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid ) continue;

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

            if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo) && skillInfo.CanUse)
            {
                skillInfo.CanUse = false;
                skillInfo.Cooldown = DateTime.Now;
                CreateReplica(player);
            }
        }

        private static void CreateReplica(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;
            if (playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null) return;

            var replica = EntityManager.CreateTrackedPropOverride(player.Index);
            if (replica == null || !replica.IsValid) return;

            replica.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
            replica.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(replica.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
           
            replica.SetModel(playerPawn!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
            replica.Entity!.Name = replica.Globalname = $"Illusionist_{Server.TickCount}_{(player.Team == CsTeam.CounterTerrorist ? "CT" : "TT")}";

            replica.UseAnimGraph = false;
            replica.DispatchSpawn();

            float distance = 40;
            Vector startPos = playerPawn.AbsOrigin + SkillUtils.GetForwardVector(playerPawn.AbsRotation) * distance;
            QAngle angle = new(0, playerPawn.EyeAngles.Y, 0);
            replica.Teleport(startPos, angle, new Vector(0, 0, -100));

            bool ducking = ((PlayerFlags)playerPawn.Flags).HasFlag(PlayerFlags.FL_DUCKING);

            string animName = ducking ? "crouch_new_knife_n" : "run_new_knife_n";
            replica.AcceptInput("SetAnimation", value: animName);
            replica.AcceptInput("SetPlaybackRate", value: "1.0");

            float speed = ducking ? 1.25f : 3.5f;
            Vector forwardVec = SkillUtils.GetForwardVector(angle);
            int replicaIndex = (int)replica.Index;

            Instance.AddTickTimer(10, () => {
                var moveTimer = Instance.AddTickTimer(1, () =>
                {
                    if (replica == null || !replica.IsValid)
                    {
                        if (ActiveTimers.TryRemove(replicaIndex, out var timer)) timer?.Kill();
                        return;
                    }

                    if (ducking && Server.TickCount % 50 == 0)
                    {
                        replica.AcceptInput("SetAnimation", value: animName);
                        replica.AcceptInput("SetPlaybackRate", value: "1.0");
                    }

                    Vector currentPos = replica.AbsOrigin!;
                    Vector nexPos = new(
                        currentPos.X + (forwardVec.X * speed),
                        currentPos.Y + (forwardVec.Y * speed),
                        currentPos.Z
                    );
                    replica.Teleport(nexPos, null, null);
                }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
                ActiveTimers.TryAdd(replicaIndex, moveTimer);
            }, TimerFlags.STOP_ON_MAPCHANGE);

            float duration = SkillsInfo.GetValue<float>(skillName, ducking ? "durationCrouch" : "durationRun");
            Instance.AddTimer(duration, () =>
            {
                if (replica != null && replica.IsValid)
                {
                    EntityManager.DestroyEntity(replica.Index);
                    if (ActiveTimers.TryRemove(replicaIndex, out var timer)) timer?.Kill();
                }
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null || param2.Attacker == null || param2.Attacker.Value == null)
                return;

            if (string.IsNullOrEmpty(param.Entity.Name)) return;
            if (!param.Entity.Name.StartsWith("Illusionist_")) return;

            var replica = param.As<CDynamicProp>();
            if (replica == null || !replica.IsValid) return;

            replica.EmitSound("GlassBottle.BulletImpact", volume: 1f);
            if (ActiveTimers.TryRemove((int)replica.Index, out var timer)) timer?.Kill();
            EntityManager.DestroyEntity(replica.Index);

            CCSPlayerPawn attackerPawn = new(param2.Attacker.Value.Handle);
            if (attackerPawn.DesignerName != "player") return;

            var attackerTeam = attackerPawn.TeamNum;
            var replicaTeam = replica.Globalname.EndsWith("CT") ? 3 : 2;
            SkillUtils.TakeHealth(attackerPawn, attackerTeam != replicaTeam ? SkillsInfo.GetValue<int>(skillName, "EnemyTeamDamage") : SkillsInfo.GetValue<int>(skillName, "YourTeamDamage"));
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#42f5ef", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 2, Rarity rarity = Rarity.Common, float cooldown = 30f, float durationRun = 5, float durationCrouch = 12, int yourTeamDamage = 10, int enemyTeamDamage = 20) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float DurationRun { get; set; } = durationRun;
            public float DurationCrouch { get; set; } = durationCrouch;
            public int YourTeamDamage { get; set; } = yourTeamDamage;
            public int EnemyTeamDamage { get; set; } = enemyTeamDamage;
        }
    }
}