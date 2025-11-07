using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class ReactiveArmor : ISkill
    {
        private const Skills skillName = Skills.ReactiveArmor;
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

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            int damage = @event.DmgHealth;

            if (!Instance.IsPlayerValid(attacker) || !Instance.IsPlayerValid(victim)) return;
            if (SkillPlayerInfo.TryGetValue(victim!.SteamID, out var skillInfo))
            {
                if (!victim.IsValid || !victim.PawnIsAlive || !skillInfo.CanUse) return;
                skillInfo.CanUse = false;
                skillInfo.Cooldown = DateTime.Now;
                RestoreHealth(victim, damage);
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

        private static void RestoreHealth(CCSPlayerController victim, float damage)
        {
            var playerPawn = victim.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;
            var newHealth = playerPawn.Health + damage;

            if (newHealth > 100)
                newHealth = 100;

            playerPawn.Health = (int)newHealth;
            Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#3cded3", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float cooldown = 15) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Cooldown { get; set; } = cooldown;
        }
    }
}