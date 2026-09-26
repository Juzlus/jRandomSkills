using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Scavenger : ISkill
    {
        private const Skills skillName = Skills.Scavenger;
        private static readonly CsItem[] grenades = [CsItem.HEGrenade, CsItem.Flashbang, CsItem.SmokeGrenade];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(attacker) || victim == null || !victim.IsValid || attacker == victim) return;
            if (attacker!.Team == victim.Team) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(attacker.Index);
            if (playerInfo?.Skill != skillName) return;

            uint attackerIndex = attacker.Index;
            Server.NextFrame(() =>
            {
                var player = Utilities.GetPlayerFromIndex((int)attackerIndex);
                if (!Instance.IsPlayerValid(player)) return;

                RefillWeapons(player!);

                if (Instance.Random.NextDouble() < SkillsInfo.GetValue<float>(skillName, "grenadeChance"))
                    SkillUtils.TryGiveWeapon(player!, grenades[Instance.Random.Next(grenades.Length)]);
            });
        }

        private static void RefillWeapons(CCSPlayerController player)
        {
            var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
            if (weapons == null) return;

            foreach (var handle in weapons)
            {
                var weapon = handle.Value;
                if (weapon == null || !weapon.IsValid) continue;

                var vdata = weapon.GetVData<CCSWeaponBaseVData>();
                if (vdata == null || vdata.MaxClip1 <= 0) continue;
                if (vdata.GearSlot is not (gear_slot_t.GEAR_SLOT_RIFLE or gear_slot_t.GEAR_SLOT_PISTOL)) continue;

                weapon.Clip1 = vdata.MaxClip1;
                Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");

                if (weapon.ReserveAmmo[0] < vdata.PrimaryReserveAmmoMax)
                {
                    weapon.ReserveAmmo[0] = vdata.PrimaryReserveAmmoMax;
                    Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_pReserveAmmo");
                }
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#9c8b52", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float grenadeChance = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float GrenadeChance { get; set; } = grenadeChance;
        }
    }
}
