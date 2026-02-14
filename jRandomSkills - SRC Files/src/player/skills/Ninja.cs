using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Ninja : ISkill
    {
        private const Skills skillName = Skills.Ninja;
        private static readonly ConcurrentDictionary<nint, float> invisibilityChanged = [];
        private static readonly ConcurrentDictionary<CCSPlayerController, byte> invisiblePlayers = [];
        private const string bloodParticle = "particles/blood_impact/blood_impact_high.vpcf";
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(bloodParticle);
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                invisibilityChanged.Clear();
                invisiblePlayers.Clear();
            }
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);

            if (playerInfo?.Skill != skillName) return;
            UpdateNinja(player);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = @event.Userid;
            if (!Instance.IsPlayerValid(player)) return;
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);

            if (playerInfo?.Skill != skillName) return;
            UpdateNinja(player);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;

                var targetHandle = player.Pawn.Value?.ObserverServices?.ObserverTarget.Value?.Handle ?? nint.Zero;
                bool isObservingNinja = false;

                if (targetHandle != nint.Zero)
                {
                    var target = Utilities.GetPlayers().FirstOrDefault(p => p?.Pawn?.Value?.Handle == targetHandle);
                    var targetInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == target?.SteamID);
                    if (targetInfo?.Skill == skillName) isObservingNinja = true;
                }

                foreach (var _player in invisiblePlayers.Keys)
                    if (player.SteamID != _player.SteamID && !isObservingNinja)
                    {
                        var playerPawn = _player.PlayerPawn.Value;
                        if (playerPawn == null || !playerPawn.IsValid) continue;

                        var entity = Utilities.GetEntityFromIndex<CBaseEntity>((int)playerPawn.Index);
                        if (entity == null || !entity.IsValid) continue;
                        info.TransmitEntities.Remove(entity.Index);

                        var bombIndex = GetBombIndex(_player);
                        if (bombIndex == null) continue;
                        var bombEntity = Utilities.GetEntityFromIndex<CBaseEntity>((int)bombIndex);
                        if (bombEntity == null || !bombEntity.IsValid) continue;
                        info.TransmitEntities.Remove(bombEntity.Index);
                    }
            }
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    UpdateNinja(player);
                if (!player.PawnIsAlive)
                    invisiblePlayers.TryRemove(player, out _);
            }
        }

        public static void EnableSkill(CCSPlayerController _)
        {
            Event.EnableTransmit();
        }
        
        public static void DisableSkill(CCSPlayerController player)
        {
            SetPlayerVisibility(player, 0);
            invisiblePlayers.TryRemove(player, out _);
        }

        private static void UpdateNinja(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            
            var flags = (PlayerFlags)pawn.Flags;
            var buttons = player.Buttons;

            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null) return;

            var activeWeapon = weaponServices.ActiveWeapon.Value;
            float percentInvisibility = 0;

            if (buttons.HasFlag(PlayerButtons.Duck))
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "duckPercentInvisibility");
            if (activeWeapon != null && activeWeapon.DesignerName == "weapon_knife")
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "knifePercentInvisibility");
            if (!buttons.HasFlag(PlayerButtons.Moveleft) && !buttons.HasFlag(PlayerButtons.Moveright) && !buttons.HasFlag(PlayerButtons.Forward) && !buttons.HasFlag(PlayerButtons.Back) && flags.HasFlag(PlayerFlags.FL_ONGROUND))
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "idlePercentInvisibility");

            if (invisibilityChanged.TryGetValue(player.Handle, out float oldInvisibility))
                if (percentInvisibility == oldInvisibility)
                    return;

            invisibilityChanged.TryAdd(player.Handle, percentInvisibility);

            if (percentInvisibility > .9)
                invisiblePlayers.TryAdd(player, 0);
            else
            {
                SetPlayerVisibility(player, percentInvisibility);
                invisiblePlayers.TryRemove(player, out _);
            }
        }

        private static void SetPlayerVisibility(CCSPlayerController player, float percentInvisibility)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn != null)
            {
                var color = Color.FromArgb(Math.Max(255 - (int)(255 * percentInvisibility), 0), 255, 255, 255);
                playerPawn.Render = color;
                Utilities.SetStateChanged(playerPawn, "CBaseModelEntity", "m_clrRender");
            }
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

        private static uint? GetBombIndex(CCSPlayerController player)
        {
            var bombEntities = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4").ToList();
            if (bombEntities.Count == 0) return null;

            var bomb = bombEntities.FirstOrDefault();
            if (bomb == null) return null;

            if (bomb.OwnerEntity.Index != player.Index) return null;
            return bomb.Index;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#dedede", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float idlePercentInvisibility = .3f, float duckPercentInvisibility = .3f, float knifePercentInvisibility = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float IdlePercentInvisibility { get; set; } = idlePercentInvisibility;
            public float DuckPercentInvisibility { get; set; } = duckPercentInvisibility;
            public float KnifePercentInvisibility { get; set; } = knifePercentInvisibility;
        }
    }
}