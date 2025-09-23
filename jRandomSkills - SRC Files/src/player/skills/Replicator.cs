﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.player;
using jRandomSkills.src.utils;
using static jRandomSkills.jRandomSkills;
using System.Collections.Concurrent;

namespace jRandomSkills
{
    public class Replicator : ISkill
    {
        private const Skills skillName = Skills.Replicator;
        private static readonly ConcurrentDictionary<ulong, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
                SkillPlayerInfo.Clear();
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
                        UpdateHUD(player, skillInfo);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.SteamID, new PlayerSkillInfo
            {
                SteamID = player.SteamID,
                CanUse = true,
                Cooldown = DateTime.MinValue,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryRemove(player.SteamID, out _);
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

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
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

            if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
            {
                if (!player.IsValid || !player.PawnIsAlive) return;
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
            var replica = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (replica == null || playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null)
                return;

            float distance = 40;
            Vector pos = playerPawn.AbsOrigin + SkillUtils.GetForwardVector(playerPawn.AbsRotation) * distance;

            if (((PlayerFlags)playerPawn.Flags).HasFlag(PlayerFlags.FL_DUCKING))
                pos.Z -= 19;
            
            replica.Flags = playerPawn.Flags;
            replica.Flags |= (uint)Flags_t.FL_DUCKING;
            replica.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(replica.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            replica.SetModel(playerPawn!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
            replica.Entity!.Name = replica.Globalname = $"Replica_{Server.TickCount}_{(player.Team == CsTeam.CounterTerrorist ? "CT" : "TT")}";
            replica.Teleport(pos, playerPawn.AbsRotation, null);
            replica.DispatchSpawn();
            replica.AcceptInput("EnableCollision");
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null || param2.Attacker == null || param2.Attacker.Value == null)
                return;

            CCSPlayerPawn attackerPawn = new(param2.Attacker.Value.Handle);
            if (string.IsNullOrEmpty(param.Entity.Name)) return;
            if (!param.Entity.Name.StartsWith("Replica_")) return;

            var replica = param.As<CPhysicsPropMultiplayer>();
            if (replica == null || !replica.IsValid) return;
            replica.EmitSound("GlassBottle.BulletImpact", volume: 1f);
            
            var attackerTeam = attackerPawn.TeamNum;
            var replicaTeam = replica.Globalname.EndsWith("CT") ? 3 : 2;
            SkillUtils.TakeHealth(attackerPawn, attackerTeam != replicaTeam ? 15 : 5);
            replica.AcceptInput("Kill");
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#a3000b", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, float cooldown = 15f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates)
        {
            public float Cooldown { get; set; } = cooldown;
        }
    }
}