using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class WildThrow : ISkill
    {
        private const Skills skillName = Skills.WildThrow;
        private readonly static ConcurrentDictionary<uint, byte> infectedPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var player in Utilities.GetPlayers())
                    DisableSkill(player);

                infectedPlayers.Clear();
                playersToTarget.Clear();
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var enemies = Utilities.GetPlayers().Where(p =>p != null &&p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p =>p != null &&p.IsValid &&p.Team != player.Team &&p.PlayerPawn?.Value != null &&p.PlayerPawn.Value.IsValid &&p.PlayerPawn.Value.Health > 0 &&!p.IsHLTV &&p.Team != CsTeam.Spectator&& p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Index.ToString())));
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (playersToTarget.TryRemove(player.Index, out uint targetIndex))
                infectedPlayers.TryRemove(targetIndex, out _);

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            infectedPlayers.TryRemove(player.Index, out _);
            SkillUtils.CloseMenu(player);
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;
            foreach (var player in Utilities.GetPlayers())
            {
                if (!SkillUtils.HasMenu(player)) continue;
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);

                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                var enemies = Utilities.GetPlayers().Where(p =>
                    p != null &&
                    p.IsValid)
                .Select(p => PlayerManager.GetPlayerEvent(p))
                .Where(p => 
                    p != null &&
                    p.IsValid &&
                    p.Team != player.Team &&
                    p.PlayerPawn?.Value != null &&
                    p.PlayerPawn.Value.IsValid &&
                    p.PlayerPawn.Value.Health > 0 &&
                    !p.IsHLTV &&
                    p.Team != CsTeam.Spectator
                    && p.Team != CsTeam.None
                ).ToArray();

                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Index.ToString())));
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            if (playerInfo.SkillUsed)
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("areareaper_used_info")}");
                return;
            }

            string enemyId = commands[0];
            if (!uint.TryParse(enemyId, out uint enemyIndex)) { player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index")); return; }
            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);

            if (enemy == null)
            {
                player.PrintToChat($" {ChatColors.Red}" + player.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            infectedPlayers.TryAdd(enemy.Index, 0);
            playersToTarget[player.Index] = enemy.Index;

            playerInfo.SkillUsed = true;
            player.PrintToChat($" {ChatColors.Green}" + player.GetTranslation("wildthrow_player_info", enemy.PlayerName));
            enemy.PrintToChat($" {ChatColors.Red}" + enemy.GetTranslation("wildthrow_enemy_info"));
        }

        public static void OnEntitySpawned(CEntityInstance @event)
        {
            var name = @event.DesignerName;
            if (!name.EndsWith("_projectile")) return;

            var grenade = @event.As<CBaseCSGrenadeProjectile>();
            if (grenade == null || !grenade.IsValid) return;

            if (grenade.OwnerEntity.Value == null || !grenade.OwnerEntity.Value.IsValid) return;
            var pawn = grenade.OwnerEntity.Value.As<CCSPlayerPawn>();

            if (pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return;
            var player = pawn.Controller.Value.As<CCSPlayerController>();

            if (!infectedPlayers.ContainsKey(player.Index)) return;

            Server.NextFrame(() => {
                if (grenade == null || !grenade.IsValid) return;

                float forceMultiplier = (float)(Instance.Random.NextDouble() * .6 + .7);
                float min = 150;
                float max = 450;

                float devX = GetRandom(min, max);
                float devY = GetRandom(min, max);
                float devZ = GetRandom(min, max);

                Vector randomDev = new(devX, devY, devZ);
                Vector newVelocity = new(
                    (grenade.Velocity.X + randomDev.X) * forceMultiplier,
                    (grenade.Velocity.Y + randomDev.Y) * forceMultiplier,
                    (grenade.Velocity.Z + randomDev.Z) * forceMultiplier
                );

                grenade.Teleport(null, null, newVelocity);
            });
        }

        private static float GetRandom(float min, float max)
        {
            float val = (float)(Instance.Random.NextDouble() * (max - min) + min);
            return Instance.Random.Next(0, 2) == 0 ? val : -val;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#384728", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
        }
    }
}