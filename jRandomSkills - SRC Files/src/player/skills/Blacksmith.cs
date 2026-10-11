using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Blacksmith : ISkill
    {
        private const Skills skillName = Skills.Blacksmith;
        private static readonly List<CCSPlayerController> holderBuffer = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (!Instance.IsPlayerValid(player)) return;

            var pawn = player.PlayerPawn.Value!;
            if (pawn.ArmorValue <= 0 || pawn.ItemServices?.As<CCSPlayer_ItemServices>().HasHelmet != true)
                player.GiveNamedItem("item_assaultsuit");
        }

        public static void OnTick()
        {
            int interval = Math.Max(1, (int)(SkillsInfo.GetValue<float>(skillName, "regenInterval") * 64));
            if (Server.TickCount % interval != 0) return;

            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            if (holderBuffer.Count == 0) return;

            int amount = SkillsInfo.GetValue<int>(skillName, "armorPerRegen");
            int maxArmor = SkillsInfo.GetValue<int>(skillName, "maxArmor");

            foreach (var player in holderBuffer)
            {
                if (!Instance.IsPlayerValid(player)) continue;

                var pawn = player.PlayerPawn.Value!;
                if (pawn.ArmorValue >= maxArmor) continue;

                pawn.ArmorValue = Math.Min(pawn.ArmorValue + amount, maxArmor);
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8a8f99", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float regenInterval = 1f, int armorPerRegen = 5, int maxArmor = 100) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float RegenInterval { get; set; } = regenInterval;
            public int ArmorPerRegen { get; set; } = armorPerRegen;
            public int MaxArmor { get; set; } = maxArmor;
        }
    }
}
