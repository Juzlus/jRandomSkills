using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Planter : ISkill
    {
        private const Skills skillName = Skills.Planter;
        private static readonly ConcurrentDictionary<ulong, float> plantingPlayers = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void BombBeginplant(EventBombBeginplant @event)
        {
            var user = @event.Userid;
            if (user == null || !user.IsValid || !user.PawnIsAlive) return;
            plantingPlayers.TryAdd(user.SteamID, Server.CurrentTime);
        }

        public static void BombAbortplant(EventBombAbortplant @event)
        {
            var user = @event.Userid;
            if (user == null || !user.IsValid || !user.PawnIsAlive) return;
            plantingPlayers.TryRemove(user.SteamID, out _);
            SkillUtils.ResetPrintHTML(user);
        }

        public static void BombPlanted(EventBombPlanted @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            plantingPlayers.TryRemove(player!.SteamID, out _);

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            if (playerInfo?.Skill != skillName) return;
            playerInfo.PrintHTML = null;

            var plantedBomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
            if (plantedBomb != null)
                Server.NextFrame(() => {
                    if (plantedBomb != null && plantedBomb.IsValid)
                        plantedBomb.C4Blow = Server.CurrentTime + SkillsInfo.GetValue<int>(skillName, "extraC4BlowTime");
                });

            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
                p.PrintToCenterAlert(p.GetTranslation("bombplanted", SkillsInfo.GetValue<int>(skillName, "extraC4BlowTime")));
        }

        public static void NewRound()
        {
            foreach (var player in Utilities.GetPlayers())
                DisableSkill(player);
            plantingPlayers.Clear();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            plantingPlayers.TryRemove(player.SteamID, out _);
            SkillUtils.ResetPrintHTML(player);

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            Schema.SetSchemaValue<bool>(pawn!.Handle, "CCSPlayerPawn", "m_bInBombZone", false);
        }

        public static void OnTick()
        {
            float currentTime = Server.CurrentTime;
            foreach (var player in Utilities.GetPlayers().Where(p => p.Team == CsTeam.Terrorist))
            {
                if (!Instance.IsPlayerValid(player)) continue;

                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill != skillName) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;

                if (pawn.WeaponServices == null) continue;
                var activeWeapon = pawn.WeaponServices.ActiveWeapon.Value;
                if (activeWeapon == null || !activeWeapon.IsValid || activeWeapon.DesignerName != "weapon_c4") continue;

                pawn.InBombZone = true;
                Schema.SetSchemaValue<bool>(pawn.Handle, "CCSPlayerPawn", "m_bInBombZone", true);

                if (plantingPlayers.TryGetValue(player.SteamID, out float plantTime))
                {
                    float remaining = plantTime + 3f - currentTime;
                    playerInfo.PrintHTML = $"{player.GetTranslation("planter_planting", $"<font color='#00FF00'>{Math.Max(0, remaining):0.0}s</font>")}";
                    player.PrintToCenter(" ");
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#7d7d7d", CsTeam onlyTeam = CsTeam.Terrorist, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = 1, Rarity rarity = Rarity.Common, int extraC4BlowTime = 60) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public int ExtraC4BlowTime { get; set; } = extraC4BlowTime;
        }
    }
}