using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;
using System.Globalization;
using src.utils;

namespace src.player.skills
{
    public class Chicken : ISkill
    {
        private const Skills skillName = Skills.Chicken;
        private static HashSet<string> disabledWeapons => WeaponPool.Rifles;
        private const string ChickenModelPath = "models/chicken/chicken_silkie.vmdl";
        private const int MaterialGroupT = 5;
        private const int MaterialGroupCT = 9;
        private const float PlayerScale = .2f;
        private const float ModelScale = .85f;
        private const float ModelBack = 1.5f;
        private static readonly ConcurrentDictionary<uint, (uint Index, uint Raw)> chickens = [];

        private static int ChickenHealth() => Math.Max(1, SkillsInfo.GetValue<int>(skillName, "health"));
        private static float SpeedModifier() => SkillsInfo.GetValue<float>(skillName, "speedModifier");

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            jRandomSkills.Instance.AddToManifest(ChickenModelPath);
            Localization.RegisterDescriptionArgs(skillName, () => [(object)(int)MathF.Round((SkillsInfo.GetValue<float>(skillName, "speedModifier") - 1f) * 100f), (object)ChickenHealth()]);
        }

        public static void NewRound()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
                SetWeaponAttack(player, false);

            foreach (var playerKey in chickens.Keys)
                RemoveChicken(playerKey);

            chickens.Clear();
        }

        public static void WeaponPickup(EventItemPickup @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill != skillName) return;

            SetWeaponAttack(player, true);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn != null && playerPawn.IsValid)
            {
                Event.EnableTransmit();
                playerPawn.VelocityModifier = SpeedModifier();

                int health = ChickenHealth();
                playerPawn.MaxHealth = health;
                NetStateFast.Changed(playerPawn, "CBaseEntity", "m_iMaxHealth");

                playerPawn.Health = health;
                NetStateFast.Changed(playerPawn, "CBaseEntity", "m_iHealth");

                SkillUtils.ChangePlayerScale(player, PlayerScale);

                playerPawn.Render = Color.FromArgb(0, 255, 255, 255);
                playerPawn.ShadowStrength = 0f;
                SkillUtils.NetworkChanged(playerPawn, "CBaseModelEntity", "m_clrRender");

                SetWeaponAttack(player, true);
                CreateChicken(player);
            }
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillUtils.ResetPrintHTML(player);
            var playerPawn = player.PlayerPawn?.Value;

            if (playerPawn != null)
            {
                playerPawn.VelocityModifier = 1f;

                playerPawn.MaxHealth = 100;
                NetStateFast.Changed(playerPawn, "CBaseEntity", "m_iMaxHealth");

                if (playerPawn.Health > 0)
                {
                    playerPawn.Health = Math.Clamp(playerPawn.Health + Math.Max(0, 100 - ChickenHealth()), 1, 100);
                    NetStateFast.Changed(playerPawn, "CBaseEntity", "m_iHealth");
                }

                SkillUtils.ChangePlayerScale(player, 1);

                playerPawn.Render = Color.FromArgb(255, 255, 255, 255);
                playerPawn.ShadowStrength = 1.0f;
                SkillUtils.NetworkChanged(playerPawn, "CBaseModelEntity", "m_clrRender");

                SetWeaponAttack(player, false);
            }

            RemoveChicken(player.Key());
        }

        private static void SetWeaponAttack(CCSPlayerController player, bool disableWeapon)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player?.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null || pawn.WeaponServices.MyWeapons == null) return;

            foreach (var weapon in pawn.WeaponServices.MyWeapons)
                if (weapon != null && weapon.IsValid && weapon.Value != null && weapon.Value.IsValid)
                    if (disabledWeapons.Contains(weapon.Value.DesignerName))
                    {
                        weapon.Value.NextPrimaryAttackTick = disableWeapon ? int.MaxValue : Server.TickCount;
                        weapon.Value.NextSecondaryAttackTick = disableWeapon ? int.MaxValue : Server.TickCount;

                        NetStateFast.Changed(weapon.Value, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                        NetStateFast.Changed(weapon.Value, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
                    }
        }

        private static void CreateChicken(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null) return;

            var chicken = EntityManager.CreateTrackedDynamicProp(player.Key());
            if (chicken == null || !chicken.IsValid) return;

            float yaw = playerPawn.AbsRotation.Y * MathF.PI / 180f;
            float back = ModelBack;
            var position = new Vector(playerPawn.AbsOrigin.X - MathF.Cos(yaw) * back, playerPawn.AbsOrigin.Y - MathF.Sin(yaw) * back, playerPawn.AbsOrigin.Z);

            chicken.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(chicken.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            chicken.SetModel(ChickenModelPath);
            chicken.Teleport(position, playerPawn.AbsRotation, null);
            chicken.DispatchSpawn();

            float scale = ModelScale;
            string scaleText = scale.ToString(CultureInfo.InvariantCulture);
            chicken.CBodyComponent.SceneNode.GetSkeletonInstance().Scale = scale;
            chicken.AcceptInput("SetScale", chicken, chicken, scaleText);

            string materialGroup = (player.Team == CsTeam.Terrorist ? MaterialGroupT : MaterialGroupCT).ToString();
            chicken.AcceptInput("Skin", value: materialGroup);
            chicken.AcceptInput("SetParent", playerPawn, playerPawn, "!activator");

            uint chickenIndex = chicken.Index;
            uint chickenRaw = chicken.EntityHandle.Raw;
            Server.NextFrame(() =>
            {
                var entity = GetChicken(chickenIndex, chickenRaw);
                if (entity == null) return;
                entity.AcceptInput("SetScale", entity, entity, scaleText);
                entity.AcceptInput("Skin", value: materialGroup);
            });

            chickens[player.Key()] = (chickenIndex, chickenRaw);
        }

        private static CDynamicProp? GetChicken(uint index, uint raw)
        {
            var entity = Utilities.GetEntityFromIndex<CDynamicProp>((int)index);
            if (entity == null || !entity.IsValid || entity.EntityHandle.Raw != raw) return null;
            return entity;
        }

        private static void RemoveChicken(uint playerKey)
        {
            if (!chickens.TryRemove(playerKey, out var entry)) return;
            if (GetChicken(entry.Index, entry.Raw) != null)
                EntityManager.DestroyEntity(entry.Index);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            if (chickens.IsEmpty) return;

            foreach (var (info, player) in infoList)
            {
                if (player == null || !player.IsValid) continue;
                if (chickens.TryGetValue(player.Key(), out var entry))
                    SkillUtils.HideTransmit(info, entry.Index);
            }
        }

        public static void OnTick()
        {
            if (chickens.IsEmpty) return;

            float speed = SpeedModifier();
            foreach (var valuePair in chickens)
            {
                var player = PlayerManager.GetController(valuePair.Key);
                if (player == null || !player.IsValid) continue;

                if (GetChicken(valuePair.Value.Index, valuePair.Value.Raw) == null) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;

                if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                {
                    RemoveChicken(valuePair.Key);
                    continue;
                }

                pawn.VelocityModifier = speed;
                UpdateHUD(player);
            }
        }

        private static void UpdateHUD(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            var pawn = player.PlayerPawn.Value;
            if (pawn.WeaponServices == null || pawn.WeaponServices.ActiveWeapon == null || !pawn.WeaponServices.ActiveWeapon.IsValid || pawn.WeaponServices.ActiveWeapon.Value == null || !pawn.WeaponServices.ActiveWeapon.Value.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo == null) return;

            var weapon = pawn.WeaponServices.ActiveWeapon.Value;
            if (weapon == null || !disabledWeapons.Contains(weapon.DesignerName))
            {
                playerInfo.PrintHTML = null;
                return;
            }

            playerInfo.PrintHTML = $"<font color='#FF0000'>{player.GetTranslation("disabled_weapon")}</font>";
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#FF8B42", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int health = 50, float speedModifier = 1.1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int Health { get; set; } = health;
            public float SpeedModifier { get; set; } = speedModifier;
        }
    }
}
