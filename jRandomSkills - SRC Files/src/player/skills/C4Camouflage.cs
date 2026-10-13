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
    public class C4Camouflage : ISkill
    {
        private static readonly List<CCSPlayerController> holderBuffer = [];
        private const Skills skillName = Skills.C4Camouflage;
        private static readonly ConcurrentDictionary<uint, byte> invisiblePlayers = [];
        private const string bloodParticle = "particles/blood_impact/blood_impact_high.vpcf";
        private const string cameraViewModel = "models/sprays/spray_plane.vmdl";

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(bloodParticle);
            Instance.AddToManifest(cameraViewModel);
        }

        public static void NewRound()
        {
            invisiblePlayers.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            Event.EnableTransmit();
            if (EntityManager.GetPlayerEntities(player.Key(), "empty_prop").Count == 0)
                CreatePlayerPosProp(player);
        }

        public static void WeaponEquip(EventItemEquip @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            var weapon = @event.Item;

            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            if (weapon == "c4" && player.PlayerPawn?.Value?.Health > 0)
            {
                invisiblePlayers.TryAdd(player.Key(), 0);
                SkillUtils.SetPlayerInvisibility(player, .5f);
            }
            else
            {
                invisiblePlayers.TryRemove(player.Key(), out _);
                SkillUtils.SetPlayerInvisibility(player, 0);
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 2 != 0) return;

            if (!invisiblePlayers.IsEmpty)
            {
                var bomb = PlayerManager.GetTickBomb();
                // CheckTransmit hides the model but the radar blip comes from spotted state, so clear it every tick.
                foreach (var key in invisiblePlayers.Keys)
                {
                    var player = PlayerManager.GetController(key);
                    if (player != null)
                        ClearSpottedState(player, bomb);
                }
            }

            PlayerManager.FillSkillHolders(skillName, holderBuffer);
            foreach (var player in holderBuffer)
                SkillUtils.FollowEmptyProp(player.Key(), player.PlayerPawn?.Value);
        }

        public static void RevealOnDeath(uint playerIndex)
        {
            invisiblePlayers.TryRemove(playerIndex, out _);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            if (invisiblePlayers.IsEmpty) return;

            var bomb = PlayerManager.GetTickBomb();

            var hidden = SkillUtils.ResolveHiddenPawns(invisiblePlayers.Keys, null);
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

                    if (info.TransmitEntities.Contains(target.PawnIndex))
                        info.TransmitEntities.Remove(target.PawnIndex);

                    SkillUtils.HideCarriedEntities(info, target);

                    if (bomb == null) continue;

                    if (info.TransmitEntities.Contains(bomb.Index))
                        info.TransmitEntities.Remove(bomb.Index);
                }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            Event.EnableTransmit();

            if (player == null || !player.IsValid) return;
            var playerPawn = player.PlayerPawn.Value;

            if (playerPawn == null || !playerPawn.IsValid) return;

            if (EntityManager.GetPlayerEntities(player.Key(), "empty_prop").Count == 0)
                CreatePlayerPosProp(player);

            if (playerPawn.WeaponServices == null || playerPawn.WeaponServices.ActiveWeapon == null || !playerPawn.WeaponServices.ActiveWeapon.IsValid) return;
            if (playerPawn.WeaponServices.ActiveWeapon.Value == null || !playerPawn.WeaponServices.ActiveWeapon.Value.IsValid) return;

            var activeWeapon = playerPawn.WeaponServices.ActiveWeapon.Value;
            if (activeWeapon.DesignerName != "weapon_c4") return;

            invisiblePlayers.TryAdd(player.Key(), 0);
            SkillUtils.SetPlayerInvisibility(player, .5f);

            SkillUtils.ForceFullUpdateToAll();
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            invisiblePlayers.TryRemove(player.Key(), out _);
            SkillUtils.SetPlayerInvisibility(player, 0);
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

            Utilities.SetStateChanged(emptyProp, "CBaseEntity", "m_CBodyComponent");

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

        // Wipe spotted state (pawn + carried bomb) so a disguised carrier produces no radar blip.
        private static void ClearSpottedState(CCSPlayerController player, CC4? bomb)
        {
            var pawn = player.PlayerPawn?.Value;
            if (pawn != null && pawn.IsValid)
            {
                pawn.EntitySpottedState.Spotted = false;
                pawn.EntitySpottedState.SpottedByMask[0] = 0;
                pawn.EntitySpottedState.SpottedByMask[1] = 0;
            }

            if (bomb != null && bomb.IsValid && bomb.OwnerEntity?.Index is uint ownerEntity && (ownerEntity == player.Index || ownerEntity == player.PlayerPawn.Index))
            {
                bomb.EntitySpottedState.Spotted = false;
                bomb.EntitySpottedState.SpottedByMask[0] = 0;
                bomb.EntitySpottedState.SpottedByMask[1] = 0;
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#00911f", CsTeam onlyTeam = CsTeam.Terrorist, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Uncommon) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
        }
    }
}