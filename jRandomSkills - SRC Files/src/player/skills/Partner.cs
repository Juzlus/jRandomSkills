using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Net;

namespace src.player.skills
{
    public class Partner : ISkill
    {
        private const Skills skillName = Skills.Partner;
        private const int Levels = 5;
        private const char FullBlock = '▓';
        private const char EmptyBlock = '░';

        private sealed class Bond
        {
            public uint PartnerKey;
            public int NextHealTick;
        }

        private static readonly ConcurrentDictionary<uint, Bond> bonds = [];
        private static readonly string[] bars = BuildBars();

        private static string[] BuildBars()
        {
            var result = new string[Levels + 1];
            for (int level = 0; level <= Levels; level++)
                result[level] = new string(FullBlock, level) + new string(EmptyBlock, Levels - level);
            return result;
        }

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            bonds.Clear();

            foreach (var player in PlayerManager.GetTickPlayers())
                SkillUtils.CloseMenu(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            bonds.TryRemove(playerIndex, out _);

            foreach (var kvp in bonds)
                if (kvp.Value.PartnerKey == playerIndex)
                    bonds.TryRemove(kvp.Key, out _);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var teammates = SkillUtils.GetSelectableTeammates(player);
            if (teammates.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = new(teammates.Select(t => (t.PlayerName, t.Key().ToString())));
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("partner_no_teammates")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            bonds.TryRemove(player.Key(), out _);
            SkillUtils.ResetPrintHTML(player);
            SkillUtils.CloseMenu(player);
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (playerInfo.SkillUsed)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("areareaper_used_info")}");
                return;
            }

            if (commands.Length == 0 || !uint.TryParse(commands[0], out uint partnerKey))
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("partner_no_teammates")}");
                return;
            }

            var partner = PlayerManager.GetController(partnerKey);
            if (partner == null || !partner.IsValid || partner.Team != player.Team || partner.Key() == player.Key())
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("partner_no_teammates")}");
                return;
            }

            bonds[player.Key()] = new Bond { PartnerKey = partner.Key(), NextHealTick = Server.TickCount + SkillsInfo.GetValue<int>(skillName, "tickCooldown") };
            playerInfo.SkillUsed = true;
            SkillUtils.CloseMenu(player);

            playerEvent.PrintToChat($" {ChatColors.Green}{playerEvent.GetTranslation("partner_player_info", partner.PlayerName)}");

            var partnerEvent = PlayerManager.GetPlayerFromEvent(partner);
            if (partnerEvent != null && partnerEvent.IsValid)
                partnerEvent.PrintToChat($" {ChatColors.Green}{partnerEvent.GetTranslation("partner_teammate_info", player.PlayerName)}");
        }

        public static void OnTick()
        {
            int tick = Server.TickCount;

            if (tick % 32 == 0)
                RefreshMenus();

            if (bonds.IsEmpty || !SkillUtils.IsHudFrame()) return;

            float minDistance = Math.Max(0f, SkillsInfo.GetValue<float>(skillName, "minDistance"));
            float maxDistance = Math.Max(minDistance + 1f, SkillsInfo.GetValue<float>(skillName, "maxDistance"));
            int minHeal = Math.Max(0, SkillsInfo.GetValue<int>(skillName, "minHeal"));
            int maxHeal = Math.Max(minHeal, SkillsInfo.GetValue<int>(skillName, "maxHeal"));
            int cooldown = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "tickCooldown"));

            foreach (var (ownerKey, bond) in bonds)
            {
                var owner = PlayerManager.GetController(ownerKey);
                var ownerPawn = owner?.PlayerPawn?.Value;
                if (owner == null || ownerPawn == null || !ownerPawn.IsValid || ownerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                var ownerInfo = PlayerManager.GetPlayer(owner);
                if (ownerInfo == null || ownerInfo.Skill != skillName) continue;

                var partner = PlayerManager.GetController(bond.PartnerKey);
                var partnerPawn = partner?.PlayerPawn?.Value;
                bool partnerAlive = partner != null && partner.Team == owner.Team
                    && partnerPawn != null && partnerPawn.IsValid && partnerPawn.LifeState == (byte)LifeState_t.LIFE_ALIVE;

                int level = 0;
                if (partnerAlive && ownerPawn.AbsOrigin != null && partnerPawn!.AbsOrigin != null)
                    level = LevelFor(ownerPawn.AbsOrigin.Distance(partnerPawn.AbsOrigin), minDistance, maxDistance);

                string name = partner == null ? "?" : WebUtility.HtmlEncode(partner.PlayerName);
                string color = level >= 4 ? "#3ad15a" : level >= 2 ? "#f2c94c" : level >= 1 ? "#ff8a3d" : "#7a7a7a";
                ownerInfo.PrintHTML = partnerAlive
                    ? $"{name}: <font color='{color}'>{bars[level]}</font>"
                    : $"<font color='#7a7a7a'>{(owner.GetTranslation("partner_dead_info"))}</font>";

                if (level == 0)
                {
                    bond.NextHealTick = tick + cooldown;
                    continue;
                }

                if (tick < bond.NextHealTick) continue;
                bond.NextHealTick = tick + cooldown;

                int heal = minHeal + (level - 1) * (maxHeal - minHeal) / (Levels - 1);
                if (heal <= 0) continue;

                SkillUtils.AddHealth(ownerPawn, heal);
                SkillUtils.AddHealth(partnerPawn, heal);
            }
        }

        private static int LevelFor(float distance, float minDistance, float maxDistance)
        {
            if (distance > maxDistance) return 0;
            if (distance <= minDistance) return Levels;

            float fraction = (distance - minDistance) / (maxDistance - minDistance);
            return Math.Clamp(Levels - (int)(fraction * Levels), 1, Levels);
        }

        private static void RefreshMenus()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                var playerInfo = PlayerManager.GetPlayer(player);
                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                if (!SkillUtils.HasMenu(player)) continue;

                var teammates = SkillUtils.GetSelectableTeammates(player);
                ConcurrentBag<(string, string)> menuItems = new(teammates.Select(t => (t.PlayerName, t.Key().ToString())));
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff7eb6", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = true, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float minDistance = 75f, float maxDistance = 450f, int minHeal = 1, int maxHeal = 5, int tickCooldown = 64) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float MinDistance { get; set; } = minDistance;
            public float MaxDistance { get; set; } = maxDistance;
            public int MinHeal { get; set; } = minHeal;
            public int MaxHeal { get; set; } = maxHeal;
            public int TickCooldown { get; set; } = tickCooldown;
        }
    }
}
