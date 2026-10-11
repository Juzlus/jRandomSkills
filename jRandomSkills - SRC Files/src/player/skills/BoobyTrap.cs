using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;

namespace src.player.skills
{
    public class BoobyTrap : ISkill
    {
        private const Skills skillName = Skills.BoobyTrap;
        // Index of the terrorist whose trap is armed this round; the trap stays even if they die.
        private static uint? trapOwner;
        private static readonly object trapLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (trapLock)
                trapOwner = null;
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            lock (trapLock)
                trapOwner ??= player.Index;
        }

        public static void BombBegindefuse(EventBombBegindefuse @event)
        {
            var defuser = @event.Userid;
            if (defuser == null || !defuser.IsValid || defuser.Team != CsTeam.CounterTerrorist) return;

            uint ownerIndex;
            lock (trapLock)
            {
                if (trapOwner is not uint armed) return;
                ownerIndex = armed;
                trapOwner = null;
            }

            var owner = Utilities.GetPlayerFromIndex((int)ownerIndex);
            uint defuserIndex = defuser.Index;

            Server.NextFrame(() =>
            {
                var target = Utilities.GetPlayerFromIndex((int)defuserIndex);
                if (target == null || !target.IsValid) return;

                SkillUtils.ApplyScreenColor(target, 255, 255, 255, 255, 300, (int)(SkillsInfo.GetValue<float>(skillName, "blindTime") * 1000));
                target.EmitSound("BaseGrenade.Explode", volume: SkillsInfo.GetValue<float>(skillName, "soundVolume"));
                target.PrintToChat($" {ChatColors.Red}{target.GetTranslation("boobytrap_enemy_info")}");

                SkillUtils.TakeHealth(target.PlayerPawn.Value, SkillsInfo.GetValue<int>(skillName, "damage"), owner != null && owner.IsValid ? owner : null, KillfeedIcons.Explosion);
            });
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#b04a1c", CsTeam onlyTeam = CsTeam.Terrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Common, int damage = 40, float blindTime = 2f, float soundVolume = 1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int Damage { get; set; } = damage;
            public float BlindTime { get; set; } = blindTime;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
