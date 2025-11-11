using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CS2TraceRay.Class;
using CS2TraceRay.Struct;
using src.utils;
using System.Collections.Concurrent;
using System.Numerics;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class RCMelon : ISkill
    {
        private const Skills skillName = Skills.RCMelon;
        private static readonly ConcurrentDictionary<CCSPlayerController, PlayerSkill> playersInfo = [];
        private const string melonProp = "models/cs_italy/italy_food_melon/italy_food_melon.vmdl";
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(melonProp);
        }

        public static void NewRound()
        {
            foreach (var playerSkill in playersInfo.Values)
                KillClone(playerSkill);
            lock (setLock)
                playersInfo.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = @event.Userid;
            if (player == null) return;
            if (playersInfo.TryGetValue(player, out var playerSkill))
                if (playerSkill.CloneProp != null)
                    BlockWeapon(player, true);
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid;
            if (player == null) return;
            if (playersInfo.TryGetValue(player, out var playerSkill))
                if (playerSkill.CloneProp != null)
                    BlockWeapon(player, true);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (playersInfo.TryGetValue(player, out var playerSkill))
                if (playerSkill.CloneProp == null && playerSkill.NextUse <= Server.TickCount)
                    CreateMelon(playerSkill);
                else if (playerSkill.CloneProp != null)
                    KillClone(playerSkill);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player.PlayerPawn.Value == null || player.PlayerPawn.Value.CameraServices == null) return;
            playersInfo.TryAdd(player, new PlayerSkill
            {
                Player = player,
                CloneProp = null,
                NextUse = 0,
                UseTime = 0,
                PlayerModel = player.PlayerPawn.Value!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (playersInfo.TryGetValue(player, out var playerSkill))
                KillClone(playerSkill);
            playersInfo.TryRemove(player, out _);
        }

        private static void KillClone(PlayerSkill playerSkill)
        {
            playerSkill.Player.EmitSound("SolidMetal.BulletImpact");
            if (playerSkill.CloneProp != null && playerSkill.CloneProp.IsValid && playerSkill.CloneProp.AbsOrigin != null && playerSkill.CloneProp.AbsRotation != null)
            {
                var playerPawn = playerSkill.Player.PlayerPawn.Value;
                if (playerPawn != null && playerPawn.IsValid)
                {
                    Vector pos = new(playerSkill.CloneProp.AbsOrigin.X, playerSkill.CloneProp.AbsOrigin.Y, playerSkill.CloneProp.AbsOrigin.Z);
                    QAngle angle = new(playerSkill.CloneProp.AbsRotation.X, playerSkill.CloneProp.AbsRotation.Y, playerSkill.CloneProp.AbsRotation.Z);
                    Server.NextFrame(() =>
                    {
                        playerPawn.Teleport(pos, angle);
                    });
                }
                playerSkill.CloneProp.AcceptInput("Kill");
                playerSkill.CloneProp = null;
            }

            BlockWeapon(playerSkill.Player, false);
            playerSkill.NextUse = Server.TickCount + SkillsInfo.GetValue<float>(skillName, "Cooldown") * 64;
            playerSkill.UseTime = 0;
        }

        public static void OnTick()
        {
            foreach (var playerSkill in playersInfo.Values)
                UpdateHUD(playerSkill);
        }

        private static CDynamicProp? CreateMelon(PlayerSkill playerSkill)
        {
            var player = playerSkill.Player;
            if (player == null) return null;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null) return null;

            Vector pos = playerPawn.AbsOrigin + SkillUtils.GetForwardVector(playerPawn.AbsRotation) * 50;
            Vector pos2 = playerPawn.AbsOrigin + SkillUtils.GetForwardVector(playerPawn.AbsRotation) * (50 + 25);
            if (!CheckPosition(player, pos2) || !((PlayerFlags)playerPawn.Flags).HasFlag(PlayerFlags.FL_ONGROUND))
            {
                playerSkill.InfoTime = Server.TickCount;
                return null;
            }

            var clone = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (clone == null || !clone.IsValid) return null;

            clone.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
            clone.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(clone.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            clone.Entity!.Name = clone.Globalname = $"IanaClone_{Server.TickCount}_{player.SteamID}";
            clone.DispatchSpawn();

            Server.NextFrame(() =>
            {
                clone.SetModel(playerSkill.PlayerModel);
                clone.Teleport(playerPawn.AbsOrigin, new QAngle(0, playerPawn.V_angle.Y, 0));

                playerPawn.Teleport(pos);
                BlockWeapon(playerSkill.Player, true);
                playerSkill.UseTime = Server.TickCount;
                playerSkill.NextUse = Server.TickCount + 64000;
                playerSkill.CloneProp = clone;

                Instance.AddTimer(SkillsInfo.GetValue<float>(skillName, "Duration"), () => 
                {
                    if (playerSkill.CloneProp != null)
                        KillClone(playerSkill);
                });

                playerPawn.SetModel(melonProp);
            });

            return clone;
        }

        private unsafe static bool CheckPosition(CCSPlayerController player, Vector endPos)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return false;

            Vector eyePos = new(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + 25);
            endPos.Z += 25;

            Ray ray = new(Vector3.Zero);
            CTraceFilter filter = new(playerPawn.Index, playerPawn.Index)
            {
                m_nObjectSetMask = 0xf,
                m_nCollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT,
                m_nInteractsWith = playerPawn.GetInteractsWith(),
                m_nInteractsExclude = 0,
                m_nBits = 11,
                m_bIterateEntities = true,
                m_bHitTriggers = false,
                m_nInteractsAs = 0x40000
            };

            filter.m_nHierarchyIds[0] = playerPawn.GetHierarchyId();
            filter.m_nHierarchyIds[1] = 0;
            CGameTrace trace = TraceRay.TraceHull(eyePos, endPos, filter, ray);

            return !trace.DidHit();
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null)
                return;

            if (string.IsNullOrEmpty(param.Entity.Name)) return;
            if (!param.Entity.Name.StartsWith("IanaClone_")) return;

            var nameParams = param.Entity.Name.Split('_')[2];
            _ = ulong.TryParse(nameParams, out ulong steamID);
            if (steamID == 0) return;

            var player = Utilities.GetPlayerFromSteamId(steamID);
            if (player == null) return;

            if (playersInfo.TryGetValue(player, out var playerSkill))
            {
                KillClone(playerSkill);
                var playerPawn = player.PlayerPawn.Value;
                if (playerPawn != null)
                    SkillUtils.TakeHealth(playerPawn, (int)param2.Damage);
            }
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = @event.Userid!;
            if (!Instance.IsPlayerValid(victim)) return;

            if (playersInfo.TryGetValue(victim, out var playerSkill) && playerSkill.CloneProp != null)
            {
                KillClone(playerSkill);
                var victimPawn = victim.PlayerPawn.Value;
                if (victimPawn ==  null) return;
                SkillUtils.AddHealth(victimPawn, @event.DmgHealth);
            }
        }

        private static void BlockWeapon(CCSPlayerController player, bool block)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;

            foreach (var weapon in pawn.WeaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
                {
                    weapon.Value.NextPrimaryAttackTick = block ? int.MaxValue : Server.TickCount;
                    weapon.Value.NextSecondaryAttackTick = block ? int.MaxValue : Server.TickCount;

                    Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                    Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
                }
        }

        private static void UpdateHUD(PlayerSkill playerSkill)
        {
            if (playerSkill == null || playerSkill.Player == null) return;

            float cooldown = 0;
            float time1 = (playerSkill.NextUse - Server.TickCount) / 64;
            cooldown = (int)Math.Ceiling(Math.Max(time1, 0));

            float duration = 0;
            float time2 = SkillsInfo.GetValue<float>(skillName, "Duration") - (Server.TickCount - playerSkill.UseTime) / 64;
            duration = (int)Math.Ceiling(Math.Max(time2, 0));

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == playerSkill?.Player?.SteamID);
            if (playerInfo == null) return;

            if (playerSkill.InfoTime + (64 * 2) > Server.TickCount)
                playerInfo.PrintHTML = $"{playerSkill?.Player.GetTranslation("shade_nospace")}";
            else if (cooldown == 0 && duration == 0)
                playerInfo.PrintHTML = null;
            else if (duration > 0)
                playerInfo.PrintHTML = $"{playerSkill?.Player.GetTranslation("hud_info", $"<font color='#00FF00'>{duration}</font>")}";
            else if (cooldown > 0)
                playerInfo.PrintHTML = $"{playerSkill?.Player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public class PlayerSkill
        {
            public required CCSPlayerController Player { get; set; }
            public CDynamicProp? CloneProp { get; set; }
            public float NextUse { get; set; }
            public float UseTime { get; set; }
            public int InfoTime { get; set; }
            public string PlayerModel { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d0d930", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float cooldown = 30, float duration = 10) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Cooldown { get; set; } = cooldown;
            public float Duration { get; set; } = duration;
        }
    }
}