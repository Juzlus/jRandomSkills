using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class PrimaryBan : ISkill
    {
        private const Skills skillName = Skills.PrimaryBan;
        private static readonly ConcurrentDictionary<uint, byte> bannedPlayers = [];
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];
        private static readonly object setLock = new();
        private static readonly string[] disabledWeapons =
        [
            "weapon_ak47", "weapon_m4a4", "weapon_m4a1", "weapon_m4a1_silencer",
            "weapon_famas", "weapon_galilar", "weapon_aug", "weapon_sg553",
            "weapon_mp9", "weapon_mac10", "weapon_bizon", "weapon_mp7",
            "weapon_ump45", "weapon_p90", "weapon_mp5sd", "weapon_ssg08",
            "weapon_awp", "weapon_scar20", "weapon_g3sg1", "weapon_nova",
            "weapon_xm1014", "weapon_mag7", "weapon_sawedoff", "weapon_m249",
            "weapon_negev", "weapon_sg556"
        ];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lock (setLock)
            {
                bannedPlayers.TryRemove(playerIndex, out _);
                playersToTarget.TryRemove(playerIndex, out _);

                foreach (var kvp in playersToTarget)
                    if (kvp.Value == playerIndex)
                        playersToTarget.TryRemove(kvp.Key, out _);
            }
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                bannedPlayers.Clear();
                playersToTarget.Clear();

                foreach (var player in Utilities.GetPlayers())
                    SkillUtils.CloseMenu(player);
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid;
            var playerEvent = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;

            if (player == null || !player.IsValid || playerEvent == null || !playerEvent.IsValid) return;
            if (!bannedPlayers.ContainsKey(playerEvent.Index) || !disabledWeapons.Contains("weapon_" + weapon)) return;

            SetWeaponAttack(player, true);
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;
            foreach (var player in Utilities.GetPlayers())
            {
                if (!SkillUtils.HasMenu(player)) continue;
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);

                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                var enemies = Utilities.GetPlayers().Where(p =>p != null &&p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p =>p != null &&p.IsValid &&p.Team != player.Team &&p.PlayerPawn?.Value != null &&p.PlayerPawn.Value.IsValid &&p.PlayerPawn.Value.Health > 0 &&!p.IsHLTV &&p.Team != CsTeam.Spectator&& p.Team != CsTeam.None).ToArray();

                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Index.ToString())));
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (playerInfo.SkillUsed)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("areareaper_used_info")}");
                return;
            }

            string enemyId = commands[0];

            if (!uint.TryParse(enemyId, out uint enemyIndex)) {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }
            
            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);

            if (enemy == null)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            bannedPlayers.TryAdd(enemy.Index, 0);
            playersToTarget[player.Index] = enemy.Index;
            SetWeaponAttack(enemy, true);
            playerInfo.SkillUsed = true;

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            if (enemyEvent == null || !enemyEvent.IsValid) return;

            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("primaryban_player_info", enemy.PlayerName));
            enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("primaryban_enemy_info"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var enemies = Utilities.GetPlayers().Where(p =>p != null &&p.IsValid).Select(p => PlayerManager.GetPlayerEvent(p)).Where(p =>p != null &&p.IsValid &&p.Team != player.Team &&p.PlayerPawn?.Value != null &&p.PlayerPawn.Value.IsValid &&p.PlayerPawn.Value.Health > 0 &&!p.IsHLTV &&p.Team != CsTeam.Spectator&& p.Team != CsTeam.None).ToArray();
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = new(enemies.Select(e => (e.PlayerName, e.Index.ToString())));
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (playersToTarget.TryRemove(player.Index, out uint targetIndex))
            {
                bannedPlayers.TryRemove(targetIndex, out _);

                var target = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)targetIndex));
                if (target != null && target.IsValid && target.PawnIsAlive && !SkillUtils.IsFreezeTime())
                {
                    target.PrintToChat($" {ChatColors.Green}" + target.GetTranslation("primaryban_disable_info"));
                    SetWeaponAttack(target, false);
                }
            }

            SkillUtils.CloseMenu(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            bannedPlayers.TryRemove(player.Index, out _);
            SetWeaponAttack(player, false);
            SkillUtils.CloseMenu(player);
        }

        private static void SetWeaponAttack(CCSPlayerController? player, bool disableWeapon)
        {
            player = PlayerManager.GetPlayerEvent(player);
            if (player == null || !player.IsValid) return;

            var pawn = player?.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;
            foreach (var weapon in pawn.WeaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid && !string.IsNullOrEmpty(weapon.Value.DesignerName))
                {
                    string weaponName = weapon.Value.DesignerName;
                    if (disabledWeapons.Contains(weaponName)) 
                    {
                        weapon.Value.NextPrimaryAttackTick = disableWeapon ? int.MaxValue : Server.TickCount;
                        weapon.Value.NextSecondaryAttackTick = disableWeapon ? int.MaxValue : Server.TickCount;

                        Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                        Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
                    }
                }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ffc061", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}