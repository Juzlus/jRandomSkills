using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Pickpocket : ISkill
    {
        private const Skills skillName = Skills.Pickpocket;
        private static readonly ConcurrentDictionary<uint, int> victimCooldowns = [];
        private static readonly List<(CBasePlayerWeapon Weapon, SkillUtils.GrenadeKind Kind)> pocket = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;

            float from = SkillsInfo.GetValue<float>(skillName, "chanceFrom");
            float to = SkillsInfo.GetValue<float>(skillName, "chanceTo");
            float chance = (float)Random.Shared.NextDouble() * (to - from) + from;
            playerInfo.SkillChance = chance;

            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, chance)}",
                border: !PlayerManager.GetTickPlayers().Any(p => p.Team == player.Team && p != player) ? "tb" : "t");
        }

        public static void NewRound()
        {
            victimCooldowns.Clear();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            victimCooldowns.TryRemove(playerIndex, out _);
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            if (@event.DmgHealth <= 0 || !SkillUtils.FiresBullets(@event.Weapon)) return;

            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (attacker == null || !attacker.IsValid || victim == null || !victim.IsValid) return;
            if (attacker.Key() == victim.Key() || attacker.Team == victim.Team) return;
            var attackerInfo = PlayerManager.GetPlayer(attacker);
            if (attackerInfo?.Skill != skillName) return;

            int tick = Server.TickCount;
            if (victimCooldowns.TryGetValue(victim.Key(), out int readyTick) && tick < readyTick) return;
            if (Random.Shared.NextDouble() > (attackerInfo.SkillChance ?? 0f)) return;

            var pawn = victim.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.Health <= 0 || pawn.AbsOrigin == null || pawn.WeaponServices == null) return;

            pocket.Clear();
            foreach (var handle in pawn.WeaponServices.MyWeapons)
            {
                var weapon = handle.Value;
                if (weapon == null || !weapon.IsValid) continue;

                var kind = SkillUtils.GetGrenadeKind(weapon.DesignerName);
                if (kind != null) pocket.Add((weapon, kind.Value));
            }
            if (pocket.Count == 0) return;

            var (grenade, grenadeKind) = pocket[Random.Shared.Next(pocket.Count)];
            pocket.Clear();

            victimCooldowns[victim.Key()] = tick + (int)(Math.Max(0f, SkillsInfo.GetValue<float>(skillName, "cooldown")) * 64);
            TakeOne(pawn, grenade);

            Vector pos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + 40f);
            float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
            Vector velocity = new(MathF.Cos(angle) * 60f, MathF.Sin(angle) * 60f, 120f);
            byte team = pawn.TeamNum;

            Server.NextFrame(() =>
            {
                if (pawn == null || !pawn.IsValid) return;
                SkillUtils.SpawnGrenade(grenadeKind, pos, velocity, pawn, team);
            });

            var victimEvent = PlayerManager.GetPlayerFromEvent(victim);
            if (victimEvent != null && victimEvent.IsValid)
                victimEvent.PrintToChat($" {ChatColors.Red}{victimEvent.GetTranslation("pickpocket_enemy_info")}");
        }

        private static void TakeOne(CCSPlayerPawn pawn, CBasePlayerWeapon weapon)
        {
            var services = pawn.WeaponServices;
            var vdata = weapon.GetVData<CBasePlayerWeaponVData>();
            int ammoType = vdata == null ? -1 : vdata.PrimaryAmmoType;

            if (services != null && ammoType >= 0 && ammoType < services.Ammo.Length && services.Ammo[ammoType] > 1)
            {
                services.Ammo[ammoType]--;
                Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pWeaponServices");
                return;
            }

            weapon.AddEntityIOEvent("Kill", weapon, weapon, "", 0f);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#d18cff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon, float chanceFrom = .25f, float chanceTo = .45f, float cooldown = 1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
            public float Cooldown { get; set; } = cooldown;
        }
    }
}
