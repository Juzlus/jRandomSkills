﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.player;
using jRandomSkills.src.utils;
using static jRandomSkills.jRandomSkills;

namespace jRandomSkills
{
    public class Watchmaker : ISkill
    {
        private const Skills skillName = Skills.Watchmaker;
        private static bool bombPlanted = false;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            bombPlanted = false;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.ResetPrintHTML(player);
        }

        public static void BombPlanted(EventBombPlanted _)
        {
            bombPlanted = true;
        }

        public static void OnEntitySpawned(CEntityInstance entity)
        {
            if (bombPlanted) return;
            var name = entity.DesignerName;
            if (!name.EndsWith("_projectile"))
                return;

            var grenade = entity.As<CBaseCSGrenadeProjectile>();
            if (grenade.OwnerEntity.Value == null || !grenade.OwnerEntity.Value.IsValid) return;

            var pawn = grenade.OwnerEntity.Value.As<CCSPlayerPawn>();
            if (pawn == null || !pawn.IsValid || pawn.Controller == null || !pawn.Controller.IsValid || pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;
            var player = pawn.Controller.Value.As<CCSPlayerController>();

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill != skillName || Instance.GameRules == null) return;

            var roundTime = SkillsInfo.GetValue<int>(skillName, "changeRoundTime");
            Instance.GameRules.RoundTime += player.Team == CsTeam.Terrorist ? roundTime : -roundTime;
            if (player.Team == CsTeam.Terrorist)
                Localization.PrintTranslationToChatAll($" {ChatColors.Orange}{{0}}", ["watchmaker_tt"], [roundTime]);
            else
                Localization.PrintTranslationToChatAll($" {ChatColors.LightBlue}{{0}}", ["watchmaker_ct"], [roundTime]);
        }

        public static void OnTick()
        {
            if (bombPlanted) return;
            if (SkillUtils.IsFreezeTime()) return;
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    UpdateHUD(player);
            }
        }

        private static void UpdateHUD(CCSPlayerController player)
        {
            var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == skillName);
            if (skillData == null || Instance?.GameRules == null || Instance?.GameRules?.RoundTime == null || Instance.GameRules?.RoundStartTime == null) return;

            int seconds = 1 + (int)(Instance.GameRules.RoundTime - (Server.CurrentTime - Instance.GameRules.RoundStartTime));
            
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;
            playerInfo.PrintHTML = SkillUtils.SecondsToTimer(seconds);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff462e", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, int changeRoundTime = 10) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates)
        {
            public int ChangeRoundTime { get; set; } = changeRoundTime;
        }
    }
}