using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class SwapPosition : ISkill
    {
        private const Skills skillName = Skills.SwapPosition;
        private static readonly ConcurrentDictionary<uint, ZamianaMiejsc_PlayerInfo> SkillPlayerInfo = [];
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
            if (SkillUtils.IsFreezeTime()) return;
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid) continue;
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill == skillName)
                {
                    if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
                    {
                        if (skillInfo.LastClick.AddSeconds(4) >= DateTime.Now)
                            UpdateHUD(player, skillInfo, true);
                        else
                            UpdateHUD(player, skillInfo, false);
                    }
                }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var cooldownBeforeUse = SkillsInfo.GetValue<float>(skillName, "cooldownBeforeUse");
            SkillPlayerInfo.TryAdd(player.Index, new ZamianaMiejsc_PlayerInfo
            {
                SteamID = player.Index,
                CanUse = false,
                Cooldown = cooldownBeforeUse <= 0 ? DateTime.MinValue : Event.GetFreezeTimeEnd().AddSeconds(cooldownBeforeUse - SkillsInfo.GetValue<float>(skillName, "cooldown")),
                LastClick = DateTime.MinValue,
                FindedEnemy = false,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;
            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        private static void UpdateHUD(CCSPlayerController player, ZamianaMiejsc_PlayerInfo skillInfo, bool showInfo)
        {
            if(player == null || !player.IsValid) return;

            float cooldown = 0;
            if (skillInfo != null)
            {
                float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
                cooldown = Math.Max(time, 0);

                if (cooldown == 0 && skillInfo?.CanUse == false)
                    skillInfo.CanUse = true;
            }

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null) return;

            if (cooldown == 0)
            {
                if (showInfo)
                    playerInfo.PrintHTML = skillInfo != null && !skillInfo.FindedEnemy 
                        ? $"<font color='#FF0000'>{player.GetTranslation("hud_info_no_enemy")}</font>" : null;
                else
                    SkillUtils.ResetPrintHTML(player);
                return;
            }

            playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
            {
                List<CCSPlayerController> enemy = Utilities.GetPlayers().FindAll(p => Instance.IsPlayerValid(p) && p.Team != player.Team && p.PawnIsAlive);
                if (enemy.Count == 0)
                {
                    skillInfo.FindedEnemy = false;
                    skillInfo.LastClick = DateTime.Now;
                    return;
                }

                CCSPlayerController randomEnemy = enemy[Instance.Random.Next(0, enemy.Count)];
                if (!player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE || !randomEnemy.IsValid || !randomEnemy.PawnIsAlive) return;
                if (skillInfo.CanUse)
                {
                    skillInfo.FindedEnemy = true;
                    skillInfo.CanUse = false;
                    skillInfo.Cooldown = DateTime.Now;
                    TeleportPlayers(player, randomEnemy);
                }
                else
                    skillInfo.LastClick = DateTime.Now;
            }
        }

        private static void TeleportPlayers(CCSPlayerController attacker, CCSPlayerController victim)
        {
            var attackerPawn = attacker.PlayerPawn.Value;
            var victimPawn = victim.PlayerPawn.Value;
            if (attackerPawn == null || !attackerPawn.IsValid || victimPawn == null || !victimPawn.IsValid) return;
            if (attackerPawn.AbsOrigin == null || attackerPawn.AbsRotation == null || victimPawn.AbsOrigin == null || victimPawn.AbsRotation == null) return;

            Vector attackerPosition = new(attackerPawn.AbsOrigin.X, attackerPawn.AbsOrigin.Y, attackerPawn.AbsOrigin.Z);
            QAngle attackerAngles = new(attackerPawn.V_angle.X, attackerPawn.V_angle.Y, 0);
            Vector attackerVelocity = new(attackerPawn.AbsVelocity.X, attackerPawn.AbsVelocity.Y, attackerPawn.AbsVelocity.Z);

            Vector victimPosition = new(victimPawn.AbsOrigin.X, victimPawn.AbsOrigin.Y, victimPawn.AbsOrigin.Z);
            QAngle victimAngles = new(victimPawn.V_angle.X, victimPawn.V_angle.Y, 0);
            Vector victimVelocity = new(victimPawn.AbsVelocity.X, victimPawn.AbsVelocity.Y, victimPawn.AbsVelocity.Z);

            victimPawn.Teleport(attackerPosition, null, attackerVelocity);
            attackerPawn.Teleport(victimPosition, null, victimVelocity);

            victimPawn.Look(attackerAngles);
            attackerPawn.Look(victimAngles);
        }

        public class ZamianaMiejsc_PlayerInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
            public DateTime LastClick { get; set; }
            public bool FindedEnemy { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#1466F5", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float cooldown = 30f, float cooldownBeforeUse = 10f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float CooldownBeforeUse { get; set; } = cooldownBeforeUse;
        }
    }
}