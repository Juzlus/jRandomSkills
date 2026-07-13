using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class GodMode : ISkill
    {
        private const Skills skillName = Skills.GodMode;
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly object setLock = new();

        public static bool HaveHodMode(uint playerIndex) => SkillPlayerInfo.TryGetValue(playerIndex, out var skillInfo) && skillInfo.CanUse == false;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock) 
                SkillPlayerInfo.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.Index, new PlayerSkillInfo
            {
                SteamID = player.Index,
                CanUse = true,
                HaveGodMode = false,
                Cooldown = DateTime.MinValue,
            });

            if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
                skillInfo.HaveGodMode = true;
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo?.Skill == skillName)
                    if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
                        UpdateHUD(player, skillInfo);
            }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn != null && playerPawn.IsValid)
                playerPawn.TakesDamage = true;
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

            uint playerIndex = player.Index;

            if (SkillPlayerInfo.TryGetValue(playerIndex, out var skillInfo))
            {
                if (!player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
                if (skillInfo.CanUse)
                {
                    skillInfo.CanUse = false;
                    skillInfo.Cooldown = DateTime.Now;
                    skillInfo.HaveGodMode = true;

                    PlayerManager.GetPlayerFromEvent(player)?.PrintToChat($" {ChatColors.Green} {player.GetTranslation("godmode_on")}");
                    player.PlayerPawn.Value.TakesDamage = false;

                    Instance.AddTimer(SkillsInfo.GetValue<float>(skillName, "duration"), () => {
                        if (SkillPlayerInfo.TryGetValue(playerIndex, out var skillInfo))
                            skillInfo.HaveGodMode = false;

                        var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                        if (player != null && player.IsValid)
                        {
                            if (player.PlayerPawn == null || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;

                            player.PlayerPawn.Value.TakesDamage = true;
                            PlayerManager.GetPlayerFromEvent(player)?.PrintToChat($" {ChatColors.Red} {player.GetTranslation("godmode_off")}");

                            if (player.PlayerPawn.Value.Health <= 0)
                                player.CommitSuicide(false, true);
                        }
                    }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                }
            }
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public bool HaveGodMode {  get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e0d83a", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float cooldown = 30f, float duration = 2f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public float Duration { get; set; } = duration;
        }
    }
}