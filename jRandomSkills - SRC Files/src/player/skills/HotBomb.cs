using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;

namespace src.player.skills
{
    public class HotBomb : ISkill
    {
        private const Skills skillName = Skills.HotBomb;
        private readonly static ConcurrentDictionary<uint, byte> players = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void OnTick()
        {
            float cooldown = SkillsInfo.GetValue<float>(skillName, "cooldown");
            if (Server.TickCount % (int)(cooldown * 64) != 0) return;

            if (players.IsEmpty || jRandomSkills.Instance.GameRules?.FreezePeriod == true) return;
            int damage = SkillsInfo.GetValue<int>(skillName, "damage");

            foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && p.Team == CsTeam.Terrorist && p.PawnIsAlive))
            {
                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) continue;

                bool hasC4 = pawn.WeaponServices.MyWeapons.Any(w => w.Value?.DesignerName == "weapon_c4");

                if (!hasC4) continue;
                SkillUtils.TakeHealth(pawn, damage);
                player.ExecuteClientCommand("play sounds/player/burn_damage1");
            }
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            var weapon = @event.Item;
            if (string.IsNullOrEmpty(weapon) || weapon != "c4") return;

            player.PrintToCenterAlert(player.GetTranslation("hotbomb_hint"));
        }

        private static void ChangeC4Color()
        {
            Server.NextFrame(() =>
            {
                var bombEntities = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").ToList();
                if (bombEntities.Count == 0) return;

                var bomb = bombEntities.FirstOrDefault();
                if (bomb == null) return;

                bomb.Render = Color.Red;
                Utilities.SetStateChanged(bomb, "CBaseModelEntity", "m_clrRender");
            });
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            players.TryRemove(player.Index, out _);
        }

        public static void NewRound()
        {
            players.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (players.IsEmpty)
            {
                ChangeC4Color();
                foreach (var enemy in Utilities.GetPlayers().Where(p => p.IsValid && p.Team == CsTeam.Terrorist && p.PawnIsAlive))
                    enemy.PrintToCenterAlert(enemy.GetTranslation("hotbomb_hint"));
            }

            players.TryAdd(player.Index, 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            players.TryRemove(player.Index, out _);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#baf081", CsTeam onlyTeam = CsTeam.CounterTerrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = 1, Rarity rarity = Rarity.Common, float cooldown = 1, int damage = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public int Damage { get; set; } = damage;
        }
    }
}