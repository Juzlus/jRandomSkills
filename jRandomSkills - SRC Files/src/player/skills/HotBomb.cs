using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
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
            int cooldown = (int)(SkillsInfo.GetValue<float>(skillName, "cooldown") * 64);
            if (Server.TickCount % cooldown != 0) return;

            if (players.IsEmpty || jRandomSkills.Instance.GameRules?.FreezePeriod == true) return;
            int damage = SkillsInfo.GetValue<int>(skillName, "damage");

            foreach (var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Team == CsTeam.Terrorist && p.PlayerPawn?.Value?.Health > 0))
            {
                var playerEvent = PlayerManager.GetPlayerFromEvent(player);
                if (playerEvent == null || !playerEvent.IsValid) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) continue;

                bool hasC4 = pawn.WeaponServices.MyWeapons.Any(w => w.Value?.DesignerName == "weapon_c4");
                if (!hasC4) continue;

                SkillUtils.TakeHealth(pawn, damage);
                playerEvent?.ExecuteClientCommand($"play player/player_damagebody_0{jRandomSkills.Instance.Random.Next(4, 8)}");
            }
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var weapon = @event.Item;
            if (string.IsNullOrEmpty(weapon) || weapon != "c4") return;

            // Gate on the bomb being hot (red), not on `players`: the fresh round's C4 is
            // handed out before NewRound clears `players`, but a new C4 spawns white, so colour is race-free.
            var bomb = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").FirstOrDefault();
            if (bomb == null || !bomb.IsValid) return;
            if (bomb.Render.R != 255 || bomb.Render.G != 0 || bomb.Render.B != 0) return;

            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            player.PrintToCenterAlert(player.GetTranslation("hotbomb_hint"));
        }

        private static void ChangeC4Color(Color color)
        {
            Server.NextFrame(() =>
            {
                var bombEntities = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").ToList();
                if (bombEntities.Count == 0) return;

                var bomb = bombEntities.FirstOrDefault();
                if (bomb == null) return;

                bomb.Render = color;
                Utilities.SetStateChanged(bomb, "CBaseModelEntity", "m_clrRender");
            });
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
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
                ChangeC4Color(Color.Red);
                foreach (var enemy in Utilities.GetPlayers().Where(p => p.IsValid && p.Team == CsTeam.Terrorist && p.PlayerPawn?.Value?.Health > 0))
                    enemy.PrintToCenterAlert(enemy.GetTranslation("hotbomb_hint"));
            }

            players.TryAdd(player.Index, 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            players.TryRemove(player.Index, out _);

            if (players.IsEmpty && !SkillUtils.IsFreezeTime())
            {
                ChangeC4Color(Color.White);
                foreach (var enemy in Utilities.GetPlayers().Where(p => p.IsValid && p.Team == CsTeam.Terrorist && p.PlayerPawn?.Value?.Health > 0))
                    enemy.PrintToCenterAlert(enemy.GetTranslation("hotbomb_disable_info"));
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#baf081", CsTeam onlyTeam = CsTeam.CounterTerrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = 1, Rarity rarity = Rarity.Common, float cooldown = 1, int damage = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public int Damage { get; set; } = damage;
        }
    }
}