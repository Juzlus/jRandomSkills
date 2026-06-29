using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;

namespace src.player.skills
{
    public class ReZombie : ISkill
    {
        private const Skills skillName = Skills.ReZombie;
        private static readonly ConcurrentDictionary<uint, int> zombies = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            zombies.TryRemove(player.Index, out _);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            zombies.TryRemove(player.Index, out _);
            SetPlayerColor(player.PlayerPawn.Value, true);
            ResetHealth(player);
        }

        public static void ResetHealth(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null) return;

            pawn.MaxHealth = 100;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            pawn.Health = Math.Min(pawn.Health, 100);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;
            if (player == null || !player.IsValid) return;

            if (!zombies.ContainsKey(player.Index) || weapon == "c4" || weapon.Contains("knife") || weapon.Contains("bayonet"))
                return;

            player.ExecuteClientCommand("slot3");
        }

        public static void NewRound()
        {
            foreach (var playerIndex in zombies.Keys)
            {
                var player = Utilities.GetPlayerFromIndex((int)playerIndex);
                if (player == null || !player.IsValid) continue;

                DisableSkill(player);
            }
            zombies.Clear();
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim == null || !victim.IsValid) return;

            var pawn = victim.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            bool isZombie = zombies.TryGetValue(victim.Index, out int tick);

            if (isZombie && tick + 4 < Server.TickCount)
                return;

            var playerInfo = PlayerManager.GetPlayerByIndex(victim.Index);
            if (playerInfo?.Skill != skillName) return;

            var zombieHealth = SkillsInfo.GetValue<int>(skillName, "zombieHealth");

            if (pawn.Health <= 0)
            {
                bool isBlock = victim.TeamChanged;

                zombies[victim.Index] = Server.TickCount;

                if (isBlock) return;

                DropAllBotWeapons(victim);
                SetPlayerColor(pawn, false);

                pawn.MaxHealth = zombieHealth;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");
                pawn.Health = zombieHealth;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

                victim.ExecuteClientCommand("slot3");
            }
            else if (isZombie && tick + 4 > Server.TickCount)
                SkillUtils.AddHealth(pawn, zombieHealth - pawn.Health, zombieHealth);
        }

        private static void DropAllBotWeapons(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || !player.IsBot) return;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return;

            var itemServices = pawn.ItemServices?.As<CCSPlayer_ItemServices>();
            if (itemServices == null || pawn.WeaponServices?.MyWeapons == null) return;

            foreach (var item in pawn.WeaponServices.MyWeapons)
            {
                if (item == null || !item.IsValid) continue;

                var weapon = item.Value;
                if (weapon == null || !weapon.IsValid) continue;

                var weaponName = weapon.DesignerName;
                if (string.IsNullOrEmpty(weaponName) || weaponName.Contains("knife") || weaponName.Contains("bayonet"))
                    continue;

                SkillUtils.SafeKillEntity<CBasePlayerWeapon>(weapon.Index);
            }
        }

        public static bool OnWeaponCanAcquire(DynamicHook hook, CCSPlayerController player, CEconItemView econItem, CCSWeaponBaseVData vdata)
        {
            string weaponName = vdata.Name;
            if (string.IsNullOrEmpty(weaponName)) return false;

            if (!zombies.ContainsKey(player.Index))
                return false;

            if (weaponName.Contains("knife") || weaponName.Contains("bayonet"))
                return false;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return false;

            hook.SetReturn(AcquireResult.InvalidItem);
            return true;
        }

        private static void SetPlayerColor(CCSPlayerPawn pawn, bool normal)
        {
            var color = normal ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 255, 0, 0);
            pawn.Render = color;
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff5C0A", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, int zombieHealth = 500) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public int ZombieHealth { get; set; } = zombieHealth;
        }
    }
}