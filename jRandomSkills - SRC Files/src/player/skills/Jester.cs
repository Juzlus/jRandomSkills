using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Drawing;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Jester : ISkill
    {
        private const Skills skillName = Skills.Jester;
        private static bool jesterMode = false;
        private static bool jesterStarted = false;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            jesterStarted = false;
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            if (!jesterMode) return;
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            int hitgroup = @event.Hitgroup;

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim)) return;
            var attackerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == attacker?.SteamID);
            var victimInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == victim?.SteamID);

            if (attackerInfo?.Skill == skillName || victimInfo?.Skill == skillName)
                RestoreHealth(victim!, @event.DmgHealth);
        }

        private static void RestoreHealth(CCSPlayerController victim, float damage)
        {
            var playerPawn = victim.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;
            var newHealth = playerPawn.Health + damage;

            if (newHealth > 100)
                newHealth = 100;

            playerPawn.Health = (int)newHealth;
        }

        public static void EnableSkill(CCSPlayerController _)
        {
            if (!jesterStarted)
            {
                var minTime = SkillsInfo.GetValue<float>(skillName, "minTime");
                var maxTime = SkillsInfo.GetValue<float>(skillName, "maxTime");
                float wait = (float)Instance.Random.NextDouble() * (maxTime - minTime) + minTime;
                Instance.AddTimer(wait, ChangeMode);
                jesterStarted = true;
            }
        }

        private static void ChangeMode()
        {
            if (Instance.SkillPlayer.Any(p => p.Skill == skillName))
            {
                jesterMode = !jesterMode;
                foreach (var player in Utilities.GetPlayers().Where(p => !p.IsBot && p.PawnIsAlive))
                {
                    if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) continue;
                    var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
                    if (playerInfo?.Skill == skillName)
                    {
                        SetPlayerColor(player.PlayerPawn.Value);
                        player.ExecuteClientCommand("play sounds/weapons/taser/taser_charge_ready");
                    }
                }
                var minTime = SkillsInfo.GetValue<float>(skillName, "minTime");
                var maxTime = SkillsInfo.GetValue<float>(skillName, "maxTime");
                float wait = (float)Instance.Random.NextDouble() * (maxTime - minTime) + minTime;
                Instance.AddTimer(wait, ChangeMode);
            }
        }

        private static void SetPlayerColor(CCSPlayerPawn pawn)
        {
            var color = jesterMode ? Color.FromArgb(255, 128, 0, 128) : Color.FromArgb(255, 255, 255, 255);
            pawn.Render = color;
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
        }

        public static void OnTick()
        {
            if (SkillUtils.IsFreezeTime()) return;
            foreach (var player in Utilities.GetPlayers().Where(p => !p.IsBot && p.PawnIsAlive))
            {
                if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) continue;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
                if (playerInfo?.Skill == skillName)
                    UpdateHUD(player);
            }
        }

        private static void UpdateHUD(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;
            playerInfo.PrintHTML = $"{player.GetTranslation("jester_mode")}: <font color='{(jesterMode ? "#00ff00" : "#ff0000")}'>{player.GetTranslation(jesterMode ? "jester_on" : "jester_off")}</font>";
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8f108f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, float minTime = 10f, float maxTime = 25f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates)
        {
            public float MinTime { get; set; } = minTime;
            public float MaxTime { get; set; } = maxTime;
        }
    }
}