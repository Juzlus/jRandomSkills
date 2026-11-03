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
        private static readonly List<CCSPlayerController> holderBuffer = [];
        private const Skills skillName = Skills.Ninja;
        private static readonly ConcurrentDictionary<nint, float> invisibilityChanged = [];
        private static readonly ConcurrentDictionary<uint, byte> invisiblePlayers = [];
        private const string bloodParticle = "particles/blood_impact/blood_impact_high.vpcf";
        private const string cameraViewModel = "models/sprays/spray_plane.vmdl";
        private static readonly object setLock = new();
        private const int BarLength = 10;
        private const char FullBlock = '▓';
        private const char EmptyBlock = '░';
        private static readonly string[] bars = BuildBars();

        private static string[] BuildBars()
        {
            var result = new string[BarLength + 1];
            for (int level = 0; level <= BarLength; level++)
                result[level] = new string(FullBlock, level) + new string(EmptyBlock, BarLength - level);
            return result;
        }

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(bloodParticle);
            Instance.AddToManifest(cameraViewModel);
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
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(PlayerManager.GetPlayerEvent(player) ?? player);

            if (playerInfo?.Skill != skillName) return;
            UpdateNinja(player);
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(PlayerManager.GetPlayerEvent(player) ?? player);

            if (playerInfo?.Skill != skillName) return;
            UpdateNinja(player);
        }

        public static void RevealOnDeath(uint playerIndex)
        {
            invisiblePlayers.TryRemove(playerIndex, out _);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            if (invisiblePlayers.IsEmpty) return;

            var bomb = PlayerManager.GetTickBomb();
            uint? bombOwnerIndex = bomb != null && bomb.IsValid ? bomb.OwnerEntity?.Index : null;

            var hidden = SkillUtils.ResolveHiddenPawns(invisiblePlayers.Keys, bombOwnerIndex);
            if (hidden.Count == 0) return;

            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid || player.Team == CsTeam.Spectator) continue;

                var targetHandle = player.Pawn.Value?.ObserverServices?.ObserverTarget.Value?.Handle ?? nint.Zero;

                foreach (var target in hidden)
                {
                    if (target.Index == player.Key()) continue;
                    if (player.Team == target.Team) continue;

                    // Only the actively spectated pawn stays transmitted; hiding it breaks the camera.
                    if (targetHandle != nint.Zero && target.Pawn.Handle == targetHandle) continue;

                    SkillUtils.HideTransmit(info, target.PawnIndex);

                    SkillUtils.HideCarriedEntities(info, target);

                    if (bomb == null || !target.HoldsBomb) continue;

                    SkillUtils.HideTransmit(info, bomb.Index);
                }
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 2 != 0) return;

            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            foreach (var player in holderBuffer)
            {
                UpdateNinja(PlayerManager.GetPlayerFromEvent(player));
                SkillUtils.FollowEmptyProp((PlayerManager.GetPlayerEvent(player) ?? player).Key(), player.PlayerPawn?.Value);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();

            if (EntityManager.GetPlayerEntities(player.Key(), "empty_prop").Count == 0)
                CreatePlayerPosProp(player);

            SkillUtils.ForceFullUpdateToEnemiesOf(player);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.SetPlayerInvisibility(player, 0);
            invisiblePlayers.TryRemove(player.Key(), out _);
            SkillUtils.ResetPrintHTML(player);
            EntityManager.DestroyPlayerEntities(player.Key());
        }

        private static void CreatePlayerPosProp(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            var emptyProp = EntityManager.CreateTrackedDynamicProp(player.Key(), trackAs: "empty_prop");
            if (emptyProp == null || !emptyProp.IsValid) return;

            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null) return;

            emptyProp.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(emptyProp.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            emptyProp.SetModel(cameraViewModel);
            emptyProp.Render = Color.FromArgb(1, 255, 255, 255);

            Vector newPos = new(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + 30);
            QAngle newAngle = new(playerPawn.AbsRotation.X, playerPawn.AbsRotation.Y, playerPawn.AbsRotation.Z);

            emptyProp.DispatchSpawn();
            emptyProp.Teleport(newPos, newAngle);

            NetStateFast.Changed(emptyProp, "CBaseEntity", "m_CBodyComponent");

        }

        private static void UpdateNinja(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return;

            if (player.PlayerPawn?.Value?.Health <= 0)
                return;

            var playerEvent = PlayerManager.GetPlayerEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var pawn = playerEvent.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            var flags = (PlayerFlags)pawn.Flags;
            var buttons = player.Buttons;

            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null) return;

            var activeWeapon = weaponServices.ActiveWeapon.Value;
            float percentInvisibility = 0;

            if (buttons.HasFlag(PlayerButtons.Duck))
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "duckPercentInvisibility");
            if (activeWeapon != null && (activeWeapon.DesignerName == "weapon_knife" || activeWeapon.DesignerName == "weapon_bayonet"))
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "knifePercentInvisibility");
            if (!buttons.HasFlag(PlayerButtons.Moveleft) && !buttons.HasFlag(PlayerButtons.Moveright) && !buttons.HasFlag(PlayerButtons.Forward) && !buttons.HasFlag(PlayerButtons.Back) && flags.HasFlag(PlayerFlags.FL_ONGROUND))
                percentInvisibility += SkillsInfo.GetValue<float>(skillName, "idlePercentInvisibility");

            var playerInfo = PlayerManager.GetPlayer(playerEvent);
            bool hudMissing = playerInfo != null && string.IsNullOrEmpty(playerInfo.PrintHTML);

            if (invisibilityChanged.TryGetValue(playerEvent.Handle, out float oldInvisibility))
                if (percentInvisibility == oldInvisibility && !hudMissing)
                    return;

            if (playerInfo != null)
                playerInfo.PrintHTML = BuildHud(playerEvent, percentInvisibility);

            invisibilityChanged.AddOrUpdate(playerEvent.Handle, percentInvisibility, (_, _) => percentInvisibility);

            if (percentInvisibility > .9)
                invisiblePlayers.TryAdd(playerEvent.Key(), 0);
            else
            {
                SkillUtils.SetPlayerInvisibility(playerEvent, percentInvisibility);
                invisiblePlayers.TryRemove(playerEvent.Key(), out _);
            }
        }

        private static string BuildHud(CCSPlayerController player, float percentInvisibility)
        {
            int level = percentInvisibility > .9 ? BarLength : Math.Clamp((int)Math.Round(percentInvisibility * BarLength), 0, BarLength);
            string color = level >= BarLength ? "#3ad15a" : level >= 6 ? "#f2c94c" : level >= 3 ? "#ff8a3d" : "#7a7a7a";
            return player.GetTranslation("ninja_hud", $"<font color='{color}'>{bars[level]}</font>");
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;
            if (!invisiblePlayers.ContainsKey(player.Key())) return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null) return;

            var particle = EntityManager.CreateTrackedParticleSystem(player.Key(), bloodParticle, autoDestroySeconds: 3f);
            if (particle == null) return;

            Vector pos = new(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + 50);
            particle.Teleport(pos);
            particle.AcceptInput("Start");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#dedede", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float idlePercentInvisibility = .3f, float duckPercentInvisibility = .3f, float knifePercentInvisibility = .3f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float IdlePercentInvisibility { get; set; } = idlePercentInvisibility;
            public float DuckPercentInvisibility { get; set; } = duckPercentInvisibility;
            public float KnifePercentInvisibility { get; set; } = knifePercentInvisibility;
        }
    }
}