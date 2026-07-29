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
        private const float DefaultHeadshotMultiplier = 4f;
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

        public static void OnTakeDamage(DynamicHook h)
        {
            var victimEntity = h.GetParam<CEntityInstance>(0);
            var info = h.GetParam<CTakeDamageInfo>(1);
            if (victimEntity == null || !victimEntity.IsValid || info == null) return;

            var victimPawn = victimEntity.As<CCSPlayerPawn>();
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.DesignerName != "player") return;

            var victimController = victimPawn.Controller.Value;
            if (victimController == null || !victimController.IsValid) return;

            var victim = victimController.As<CCSPlayerController>();
            if (victim == null || !victim.IsValid || !victim.PawnIsAlive) return;

            var victimInfo = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(victim)?.Index ?? victim.Index);
            if (victimInfo == null || victimInfo.Skill != skillName) return;

            // Friendly-fire-off teammate hit deals 0 damage; don't zombify over a hit that never lands.
            if (SkillUtils.IsFriendlyFireBlocked(info, victimPawn)) return;

            float effectiveDamage = info.Damage;
            if (SkillUtils.GetHitGroup(info) == HitGroup_t.HITGROUP_HEAD)
                effectiveDamage *= GetHeadshotMultiplier(info);

            if (effectiveDamage < victimPawn.Health) return; // survivable, let it through

            if (TryBecomeZombie(victim, victimPawn))
                info.Damage = 0;
        }

        public static bool TryBecomeZombie(CCSPlayerController? victim, CCSPlayerPawn? victimPawn)
        {
            if (victim == null || !victim.IsValid || !victim.PawnIsAlive) return false;
            if (victimPawn == null || !victimPawn.IsValid) return false;

            var victimInfo = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(victim)?.Index ?? victim.Index);
            if (victimInfo == null || victimInfo.Skill != skillName) return false;

            lock (setLock)
            {
                if (zombies.ContainsKey(victim.Index)) return false;
                if (victim.TeamChanged) return false;

                zombies[victim.Index] = Server.TickCount;

                int zombieHealth = SkillsInfo.GetValue<int>(skillName, "zombieHealth");
                DropAllBotWeapons(victim);
                SetPlayerColor(victimPawn, false);
                SkillUtils.SetHealth(victimPawn, zombieHealth, zombieHealth);
                victim.ExecuteClientCommand("slot3");

                ApplyTurnTint(victim);
                return true;
            }
        }

        private static void ApplyTurnTint(CCSPlayerController victim)
        {
            int alpha = SkillsInfo.GetValue<int>(skillName, "A");
            if (alpha <= 0) return;

            SkillUtils.ApplyScreenColor(victim,
                r: SkillsInfo.GetValue<int>(skillName, "R"),
                g: SkillsInfo.GetValue<int>(skillName, "G"),
                b: SkillsInfo.GetValue<int>(skillName, "B"),
                a: alpha,
                duration: 200,
                holdTime: 600);
        }

        private static float GetHeadshotMultiplier(CTakeDamageInfo info)
        {
            var ability = info.Ability?.Value;
            if (ability == null || !ability.IsValid) return DefaultHeadshotMultiplier;

            var weapon = ability.As<CCSWeaponBase>();
            if (weapon == null || !weapon.IsValid) return DefaultHeadshotMultiplier;

            var vdata = weapon.GetVData<CCSWeaponBaseVData>();
            if (vdata == null || vdata.HeadshotMultiplier <= 0) return DefaultHeadshotMultiplier;

            return vdata.HeadshotMultiplier;
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

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff5C0A", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int zombieHealth = 500, int r = 255, int g = 0, int b = 0, int a = 60) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int ZombieHealth { get; set; } = zombieHealth;
            public int R { get; set; } = r;
            public int G { get; set; } = g;
            public int B { get; set; } = b;
            public int A { get; set; } = a;
        }
    }
}