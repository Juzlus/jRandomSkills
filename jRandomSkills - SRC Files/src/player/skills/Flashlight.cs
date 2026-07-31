// https://github.com/creazy231/cs2-css-flashlight

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;

namespace src.player.skills
{
    public class Flashlight : ISkill
    {
        private const Skills skillName = Skills.Flashlight;
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
            public uint LightEntityIndex { get; set; }
            public bool IsActive { get; set; }
        }

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            ClearAllEntities();
            SkillPlayerInfo.Clear();
        }

        public static void RoundEnd()
        {
            ClearAllEntities();
        }

        public static void OnTick()
        {
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid)
                    continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);

                if (playerInfo?.Skill != skillName)
                    continue;

                if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
                    continue;

                UpdateHUD(player, skillInfo);

                if (!skillInfo.IsActive || skillInfo.LightEntityIndex == 0)
                    continue;

                var lightEntity = Utilities.GetEntityFromIndex<CBarnLight>((int)skillInfo.LightEntityIndex);

                if (lightEntity == null || !lightEntity.IsValid)
                {
                    skillInfo.IsActive = false;
                    skillInfo.LightEntityIndex = 0;
                    continue;
                }

                UpdateLightPosition(player, lightEntity);

                if (Server.TickCount % 32 != 0) continue;
                CanBlind(player, lightEntity);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid)
                return;

            SkillPlayerInfo.TryAdd(player.Index, new PlayerSkillInfo
            {
                SteamID = player.Index,
                CanUse = true,
                Cooldown = DateTime.MinValue,
                LightEntityIndex = 0,
                IsActive = false
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null)
                return;

            RemovePlayerLight(player.Index);
            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim != null && victim.IsValid)
                RemovePlayerLight(victim.Index);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid)
                return;

            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.Health <= 0)
                return;

            if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
                return;

            if (!skillInfo.CanUse)
                return;

            skillInfo.CanUse = false;
            skillInfo.Cooldown = DateTime.Now;

            CBarnLight? lightEntity = null;
            if (skillInfo.LightEntityIndex != 0)
                lightEntity = Utilities.GetEntityFromIndex<CBarnLight>((int)skillInfo.LightEntityIndex);

            if (lightEntity == null || !lightEntity.IsValid)
                CreatePlayerFlashlight(player, skillInfo);
            else
            {
                skillInfo.IsActive = !skillInfo.IsActive;
                lightEntity.Enabled = skillInfo.IsActive;

                if (skillInfo.IsActive)
                {
                    lightEntity.AcceptInput("TurnOn");
                    UpdateLightPosition(player, lightEntity);
                }
                else
                {
                    lightEntity.AcceptInput("TurnOff");
                    lightEntity.Teleport(new Vector(0, 0, -1000));
                }
            }
        }

        private static void CreatePlayerFlashlight(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            var playerPawn = player.PlayerPawn?.Value;

            if (playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null)
                return;

            RemovePlayerLight(player.Index);

            if (EntityManager.OverBudget())
                return;

            var light = Utilities.CreateEntityByName<CBarnLight>("light_barn");

            if (light == null || !light.IsValid)
                return;

            EntityManager.RegisterEntity(light.Index, player.Index, "light_barn");

            int colorR = SkillsInfo.GetValue<int>(skillName, "colorR");
            int colorG = SkillsInfo.GetValue<int>(skillName, "colorG");
            int colorB = SkillsInfo.GetValue<int>(skillName, "colorB");
            float brightness = SkillsInfo.GetValue<float>(skillName, "brightness");
            float range = SkillsInfo.GetValue<float>(skillName, "range");

            light.Enabled = true;
            light.Color = Color.FromArgb(255, Math.Clamp(colorR, 0, 255), Math.Clamp(colorG, 0, 255), Math.Clamp(colorB, 0, 255));
            light.ColorTemperature = 6500;
            light.Brightness = brightness;
            light.Range = range;
            light.SoftX = 1f;
            light.SoftY = 1f;
            light.Skirt = 0.5f;
            light.SkirtNear = 1.0f;
            light.CastShadows = 0;
            light.DirectLight = 3;
            light.SizeParams.X = 45;
            light.SizeParams.Y = 45;
            light.SizeParams.Z = 0.03f;

            using (var keyValues = new CEntityKeyValues())
            {
                light.DispatchSpawn(keyValues);
            }

            light.AcceptInput("TurnOn");

            skillInfo.LightEntityIndex = light.Index;
            skillInfo.IsActive = true;

            UpdateLightPosition(player, light);
        }

        private static void UpdateLightPosition(CCSPlayerController player, CBarnLight light)
        {
            var pawn = player.PlayerPawn.Value;

            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.V_angle == null)
                return;

            Vector eyePos = new(
                pawn.AbsOrigin.X,
                pawn.AbsOrigin.Y,
                pawn.AbsOrigin.Z + pawn.ViewOffset.Z
            );

            Vector forward = SkillUtils.GetForwardVector(pawn.V_angle);

            const float distance = 18.0f;

            Vector lightPos = new(
                eyePos.X + forward.X * distance,
                eyePos.Y + forward.Y * distance,
                eyePos.Z + forward.Z * distance
            );

            light.Teleport(lightPos, pawn.V_angle, null);
        }

        private static void CanBlind(CCSPlayerController owner, CBarnLight light)
        {
            var ownerPawn = owner.PlayerPawn.Value;
            if (ownerPawn == null || !ownerPawn.IsValid || ownerPawn.AbsOrigin == null || ownerPawn.V_angle == null)
                return;

            if (light == null || !light.IsValid || light.AbsOrigin == null)
                return;

            float blindDuration = SkillsInfo.GetValue<float>(skillName, "blindDuration");
            float blindAlpha = SkillsInfo.GetValue<float>(skillName, "blindAlpha");

            float blindAngle = SkillsInfo.GetValue<float>(skillName, "blindAngle");
            float range = SkillsInfo.GetValue<float>(skillName, "range");
            float rangeSq = range * range;

            Vector eyePos = new(
                ownerPawn.AbsOrigin.X,
                ownerPawn.AbsOrigin.Y,
                ownerPawn.AbsOrigin.Z + ownerPawn.ViewOffset.Z
            );
            Vector ownerForward = SkillUtils.GetForwardVector(ownerPawn.V_angle);
            Vector lightPos = new(
                eyePos.X + ownerForward.X * 18.0f,
                eyePos.Y + ownerForward.Y * 18.0f,
                eyePos.Z + ownerForward.Z * 18.0f
            );

            float minDot = MathF.Cos(blindAngle * MathF.PI / 180.0f);

            foreach (var target in PlayerManager.GetTickPlayers())
            {
                if (target == null || !target.IsValid || target.Index == owner.Index || target.Team == owner.Team)
                    continue;

                var targetInfo = PlayerManager.GetPlayerByIndex(target.Index);
                if (targetInfo != null && targetInfo.Skill == Skills.AntyFlash)
                    continue;

                var targetPawn = target.PlayerPawn.Value;

                if (targetPawn == null || !targetPawn.IsValid || targetPawn.Health <= 0 || target.TeamNum == owner.TeamNum)
                    continue;

                bool targetBlinded = targetPawn.BlindUntilTime > Server.CurrentTime;
                bool ownerBlinded = ownerPawn.BlindUntilTime > Server.CurrentTime;
                var (playerSeesEnemy, enemySeesPlayer) = ArePlayersSpottedBothWays(owner, ownerPawn, target, targetPawn);

                bool needPlayerSees = !ownerBlinded;
                bool needEnemySees = !targetBlinded;

                if (ownerBlinded && targetBlinded)
                    continue;

                if (needPlayerSees && !playerSeesEnemy)
                    continue;

                if (needEnemySees && !enemySeesPlayer)
                    continue;

                Vector targetEyePos = new(
                    targetPawn.AbsOrigin.X,
                    targetPawn.AbsOrigin.Y,
                    targetPawn.AbsOrigin.Z + targetPawn.ViewOffset.Z
                );

                Vector toTarget = new(
                    targetEyePos.X - lightPos.X,
                    targetEyePos.Y - lightPos.Y,
                    targetEyePos.Z - lightPos.Z
                );

                float distanceSq = toTarget.X * toTarget.X + toTarget.Y * toTarget.Y + toTarget.Z * toTarget.Z;

                if (distanceSq <= 0.0001f || distanceSq > rangeSq)
                    continue;

                float distance = MathF.Sqrt(distanceSq);
                float invLength = 1.0f / distance;

                Vector directionToTarget = new(
                    toTarget.X * invLength,
                    toTarget.Y * invLength,
                    toTarget.Z * invLength
                );

                float sourceDot = ownerForward.X * directionToTarget.X +
                                  ownerForward.Y * directionToTarget.Y +
                                  ownerForward.Z * directionToTarget.Z;

                if (sourceDot < minDot)
                    continue;

                Vector targetForward = SkillUtils.GetForwardVector(targetPawn.V_angle);

                Vector fromTargetToLight = new(
                    lightPos.X - targetEyePos.X,
                    lightPos.Y - targetEyePos.Y,
                    lightPos.Z - targetEyePos.Z
                );

                float targetDistanceSq = fromTargetToLight.X * fromTargetToLight.X +
                                          fromTargetToLight.Y * fromTargetToLight.Y +
                                          fromTargetToLight.Z * fromTargetToLight.Z;

                if (targetDistanceSq <= 0.0001f)
                    continue;

                float targetInvLength = 1.0f / MathF.Sqrt(targetDistanceSq);

                fromTargetToLight.X *= targetInvLength;
                fromTargetToLight.Y *= targetInvLength;
                fromTargetToLight.Z *= targetInvLength;

                float targetDot = targetForward.X * fromTargetToLight.X +
                                  targetForward.Y * fromTargetToLight.Y +
                                  targetForward.Z * fromTargetToLight.Z;

                if (targetDot < minDot)
                    continue;

                BlindPlayers(targetPawn, blindDuration, blindAlpha);
            }
        }

        private static void BlindPlayers(CCSPlayerPawn pawn, float blindDuration, float blindAlpha)
        {
            var rng = jRandomSkills.Instance.Random;
            float now = Server.CurrentTime;

            bool alreadyBlinded = pawn.BlindUntilTime > now;

            if (!alreadyBlinded)
                pawn.BlindStartTime = now;

            pawn.BlindUntilTime = now + blindDuration;

            pawn.FlashDuration = blindDuration + (float)rng.NextDouble() * 0.05f;
            pawn.FlashMaxAlpha = blindAlpha + (float)rng.NextDouble() * 0.5f;

            Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flFlashDuration");
            Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flFlashMaxAlpha");
        }

        private static (bool, bool) ArePlayersSpottedBothWays(CCSPlayerController player, CCSPlayerPawn playerPawn, CCSPlayerController enemy, CCSPlayerPawn enemyPawn)
        {
            int playerSlot = player.Slot;
            int enemySlot = enemy.Slot;

            if (playerSlot < 0 || enemySlot < 0)
                return (false, false);

            int playerBit = playerSlot % 32;
            int enemyBit = enemySlot % 32;

            uint playerMask = 1u << playerBit;
            uint enemyMask = 1u << enemyBit;

            bool playerSeesEnemy = (enemyPawn.EntitySpottedState.SpottedByMask[0] & playerMask) != 0;
            bool enemySeesPlayer = (playerPawn.EntitySpottedState.SpottedByMask[0] & enemyMask) != 0;

            return (playerSeesEnemy, enemySeesPlayer);
        }

        private static void RemovePlayerLight(uint playerIndex)
        {
            if (SkillPlayerInfo.TryGetValue(playerIndex, out var info) && info.LightEntityIndex != 0)
            {
                EntityManager.DestroyEntity(info.LightEntityIndex);
                info.LightEntityIndex = 0;
                info.IsActive = false;
            }
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            float cooldown = 0;

            float time = (float)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
            cooldown = Math.Max(time, 0);

            if (cooldown == 0 && !skillInfo.CanUse)
                skillInfo.CanUse = true;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);

            if (playerInfo == null)
                return;

            if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        private static void ClearAllEntities()
        {
            foreach (var key in SkillPlayerInfo.Keys)
                RemovePlayerLight(key);
        }

        public class SkillConfig(
            Skills skill = skillName,
            bool active = true,
            string color = "#a3000b",
            CsTeam onlyTeam = CsTeam.None,
            bool disableOnFreezeTime = true,
            bool needsTeammates = false,
            string requiredPermission = "",
            float? hudDuration = null,
            float? descriptionHudDuration = null,
            int maxPerServer = 2,
            Rarity rarity = Rarity.Legendary,
            float cooldown = 2f,
            int colorR = 255,
            int colorG = 255,
            int colorB = 255,
            float brightness = 1.5f,
            float range = 1200.0f,
            float blindDuration = 5f,
            float blindAngle = 10.0f,
            float blindAlpha = 200
        ) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Cooldown { get; set; } = cooldown;
            public int ColorR { get; set; } = colorR;
            public int ColorG { get; set; } = colorG;
            public int ColorB { get; set; } = colorB;
            public float Brightness { get; set; } = brightness;
            public float Range { get; set; } = range;
            public float BlindDuration { get; set; } = blindDuration;
            public float BlindAlpha { get; set; } = blindAlpha;
            public float BlindAngle { get; set; } = blindAngle;
        }
    }
}