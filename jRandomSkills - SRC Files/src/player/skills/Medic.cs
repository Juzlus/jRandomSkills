using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.player;
using jRandomSkills.src.utils;
using static jRandomSkills.jRandomSkills;

namespace jRandomSkills
{
    public class Medic : ISkill
    {
        private const Skills skillName = Skills.Medic;
        private static readonly int healthToAdd = Config.GetValue<int>(skillName, "healthToAdd");
        private static readonly int healthShotLimit = Config.GetValue<int>(skillName, "healthShotLimit");
        private static readonly float timerCooldown = Config.GetValue<float>(skillName, "cooldown");
        private static readonly Dictionary<ulong, PlayerSkillInfo> SkillPlayerInfo = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, Config.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
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
            SkillPlayerInfo[player.SteamID] = new PlayerSkillInfo
            {
                SteamID = player.SteamID,
                CanUse = true,
                Cooldown = DateTime.MinValue,
                Count = healthShotLimit,
            };
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.Remove(player.SteamID);
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            float cooldown = 0;
            if (skillInfo != null)
            {
                float time = (int)(skillInfo.Cooldown.AddSeconds(timerCooldown) - DateTime.Now).TotalSeconds;
                cooldown = Math.Max(time, 0);

                if (cooldown == 0 && skillInfo?.CanUse == false)
                    skillInfo.CanUse = true;
            }

            var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == skillName);
            if (skillData == null) return;

            string infoLine = $"<font class='fontSize-l' class='fontWeight-Bold' color='#FFFFFF'>{Localization.GetTranslation("your_skill")}:</font> <br>";
            string skillLine = $"<font class='fontSize-l' class='fontWeight-Bold' color='{skillData.Color}'>{skillData.Name}</font> <br>";
            string remainingLine = cooldown != 0
                ? $"<font class='fontSize-m' color='#FFFFFF'>{Localization.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}</font> <br>"
                : $"<font class='fontSize-m' color='#{(skillInfo == null || skillInfo.Count == 0 ? "FF0000" : "00FF00")}'>{(skillInfo == null ? 0 : skillInfo.Count)}/{healthShotLimit}</font> <br>";

            var hudContent = infoLine + skillLine + remainingLine;
            player.PrintToCenterHtml(hudContent);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
            {
                if (!player.IsValid || !player.PawnIsAlive) return;
                if (skillInfo.CanUse && skillInfo.Count != 0)
                {
                    skillInfo.CanUse = false;
                    skillInfo.Cooldown = DateTime.Now;
                    skillInfo.Count -= 1;
                    SkillUtils.AddHealth(playerPawn, healthToAdd);
                    player.EmitSound("Healthshot.Success");
                }
            }
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public int Count { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#10c212", CsTeam onlyTeam = CsTeam.None, bool needsTeammates = false, int healthToAdd = 50, int healthShotLimit = 3, float cooldown = 1f) : Config.DefaultSkillInfo(skill, active, color, onlyTeam, needsTeammates)
        {
            public int HealthToAdd { get; set; } = healthToAdd;
            public int HealthShotLimit { get; set; } = healthShotLimit;
            public float Cooldown { get; set; } = cooldown;
        }
    }
}