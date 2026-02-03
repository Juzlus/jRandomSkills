using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class C4Camouflage : ISkill
    {
        private const Skills skillName = Skills.C4Camouflage;
        private static readonly ConcurrentDictionary<CCSPlayerController, byte> invisiblePlayers = [];
        private const string bloodParticle = "particles/blood_impact/blood_impact_high.vpcf";

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(bloodParticle);
        }

        public static void NewRound()
        {
            invisiblePlayers.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            if (playerInfo?.Skill != skillName) return;
            EnableSkill(player!);
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid!;
            var weapon = @event.Item;

            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
            if (playerInfo?.Skill != skillName) return;

            if (weapon == "c4")
                invisiblePlayers.TryAdd(player, 0);
            else
                invisiblePlayers.TryRemove(player, out _);
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
                if (!player.PawnIsAlive)
                    invisiblePlayers.TryRemove(player, out _);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;
                foreach (var _player in invisiblePlayers.Keys)
                    if (player.SteamID != _player.SteamID)
                    {
                        var playerPawn = _player.PlayerPawn.Value;
                        if (playerPawn == null || !playerPawn.IsValid) continue;

                        var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)playerPawn.Index);
                        if (entity == null || !entity.IsValid) continue;
                        info.TransmitEntities.Remove(entity.Index);

                        var bombIndex = GetBombIndex();
                        if (bombIndex == null) continue;

                        var bombEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)bombIndex);
                        if (bombEntity == null || !bombEntity.IsValid) continue;
                        info.TransmitEntities.Remove(bombEntity.Index);
                    }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();
            if (player == null || !player.IsValid) return;
            var playerPawn = player.PlayerPawn.Value;

            if (playerPawn == null || !playerPawn.IsValid) return;
            if (playerPawn.WeaponServices == null || playerPawn.WeaponServices.ActiveWeapon == null || !playerPawn.WeaponServices.ActiveWeapon.IsValid) return;
            if (playerPawn.WeaponServices.ActiveWeapon.Value == null || !playerPawn.WeaponServices.ActiveWeapon.Value.IsValid) return;

            var activeWeapon = playerPawn.WeaponServices.ActiveWeapon.Value;
            if (activeWeapon.DesignerName != "weapon_c4") return;

            invisiblePlayers.TryAdd(player, 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            invisiblePlayers.TryRemove(player, out _);
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;
            if (!invisiblePlayers.ContainsKey(player)) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return;

            CParticleSystem particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system")!;
            if (particle == null) return;

            particle.EffectName = bloodParticle;
            particle.StartActive = true;

            Vector pos = new(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + 50);
            particle.Teleport(pos);
            particle.DispatchSpawn();

            particle.AcceptInput("Start");
        }

        private static uint? GetBombIndex()
        {
            var bombEntities = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").ToList();
            if (bombEntities.Count == 0) return null;

            var bomb = bombEntities.FirstOrDefault();
            if (bomb == null) return null;

            return bomb.Index;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#00911f", CsTeam onlyTeam = CsTeam.Terrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
        }
    }
}