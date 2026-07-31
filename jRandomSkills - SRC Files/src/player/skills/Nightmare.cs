using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class Nightmare : ISkill
    {
        private const Skills skillName = Skills.Nightmare;
        private static readonly ConcurrentDictionary<uint, uint> playersToTarget = [];
        private static readonly ConcurrentDictionary<uint, uint> targetVolumes = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            jRandomSkills.Instance.AddToManifest(SkillsInfo.GetValue<string>(skillName, "postProcessing"));
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            lock (setLock)
            {
                RemoveVolume(playerIndex);
                playersToTarget.TryRemove(playerIndex, out _);

                foreach (var kvp in playersToTarget)
                    if (kvp.Value == playerIndex)
                        playersToTarget.TryRemove(kvp.Key, out _);
            }
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                foreach (var targetIndex in targetVolumes.Keys.ToArray())
                    RemoveVolume(targetIndex);

                foreach (var player in PlayerManager.GetTickPlayers())
                    SkillUtils.CloseMenu(player);

                targetVolumes.Clear();
                playersToTarget.Clear();
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 32 != 0) return;
            foreach (var player in PlayerManager.GetTickPlayers())
            {
                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo == null || playerInfo.Skill != skillName) continue;
                if (!SkillUtils.HasMenu(player)) continue;

                var enemies = SkillUtils.GetSelectableEnemies(player, true);
                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                SkillUtils.UpdateMenu(player, menuItems);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.SkillUsed = false;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            var enemies = SkillUtils.GetSelectableEnemies(player, true);
            if (enemies.Length > 0)
            {
                ConcurrentBag<(string, string)> menuItems = [.. enemies.Select(e => (e.PlayerName, e.Index.ToString()))];
                SkillUtils.CreateMenu(player, menuItems);
            }
            else
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index")}");
        }

        public static void TypeSkill(CCSPlayerController player, string[] commands)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill != skillName) return;

            var playerEvent = PlayerManager.GetPlayerFromEvent(player);
            if (playerEvent == null || !playerEvent.IsValid) return;

            if (playerInfo.SkillUsed)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}{playerEvent.GetTranslation("areareaper_used_info")}");
                return;
            }

            if (!uint.TryParse(commands[0], out uint enemyIndex))
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            var enemy = Utilities.GetPlayerFromIndex((int)enemyIndex);
            if (enemy == null || !enemy.IsValid)
            {
                playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                return;
            }

            lock (setLock)
            {
                if (!ApplyVolume(player.Index, enemy))
                {
                    playerEvent.PrintToChat($" {ChatColors.Red}" + playerEvent.GetTranslation("selectplayerskill_incorrect_enemy_index"));
                    return;
                }

                playersToTarget[player.Index] = enemy.Index;
            }

            playerInfo.SkillUsed = true;

            var enemyEvent = PlayerManager.GetPlayerFromEvent(enemy);
            playerEvent.PrintToChat($" {ChatColors.Green}" + playerEvent.GetTranslation("nightmare_player_info", enemy.PlayerName));

            if (enemyEvent != null && enemyEvent.IsValid)
                enemyEvent.PrintToChat($" {ChatColors.Red}" + enemyEvent.GetTranslation("nightmare_enemy_info"));
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            lock (setLock)
            {
                if (playersToTarget.TryRemove(player.Index, out uint targetIndex))
                {
                    RemoveVolume(targetIndex);

                    var target = PlayerManager.GetPlayerFromEvent(Utilities.GetPlayerFromIndex((int)targetIndex));
                    if (target != null && target.IsValid && target.PawnIsAlive && !SkillUtils.IsFreezeTime())
                        target.PrintToChat($" {ChatColors.Green}" + target.GetTranslation("nightmare_disable_info"));
                }

                SkillUtils.CloseMenu(player);
            }
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            lock (setLock)
                RemoveVolume(player.Index);
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            if (targetVolumes.IsEmpty) return;

            foreach (var (info, viewer) in infoList)
            {
                if (viewer == null || !viewer.IsValid) continue;

                foreach (var kvp in targetVolumes)
                {
                    if (kvp.Key == viewer.Index) continue;
                    if (info.TransmitEntities.Contains(kvp.Value))
                        info.TransmitEntities.Remove(kvp.Value);
                }
            }
        }

        private static bool ApplyVolume(uint ownerIndex, CCSPlayerController target)
        {
            RemoveVolume(target.Index);

            var targetPawn = target.PlayerPawn?.Value;
            if (targetPawn == null || !targetPawn.IsValid || targetPawn.AbsOrigin == null) return false;

            if (EntityManager.OverBudget()) return false;

            var volume = Utilities.CreateEntityByName<CPostProcessingVolume>("post_processing_volume");
            if (volume == null || !volume.IsValid || volume.Entity == null) return false;

            var keys = new CEntityKeyValues();
            try
            {
                keys.SetString("targetname", $"Nightmare_{target.Index}");
                keys.SetString("postprocessing", SkillsInfo.GetValue<string>(skillName, "postProcessing"));
                keys.SetBool("master", true);
                keys.SetBool("enableexposure", true);
                keys.SetFloat("fadetime", SkillsInfo.GetValue<float>(skillName, "fadeTime"));
                keys.SetFloat("minexposure", SkillsInfo.GetValue<float>(skillName, "minExposure"));
                keys.SetFloat("maxexposure", SkillsInfo.GetValue<float>(skillName, "maxExposure"));
                keys.SetFloat("exposurespeedup", 1f);
                keys.SetFloat("exposurespeeddown", 1f);
                keys.SetBool("startdisabled", false);
                keys.SetInt("spawnflags", 4097);
                keys.SetVector("origin", targetPawn.AbsOrigin);

                volume.DispatchSpawn(keys);
            }
            finally
            {
                keys.Dispose();
            }

            if (!volume.IsValid) return false;

            volume.AcceptInput("SetParent", targetPawn, null, "!activator");

            EntityManager.RegisterExisting(volume, ownerIndex, "post_processing_volume");
            targetVolumes[target.Index] = volume.Index;
            return true;
        }

        private static void RemoveVolume(uint targetIndex)
        {
            if (!targetVolumes.TryRemove(targetIndex, out uint volumeIndex)) return;
            EntityManager.DestroyEntity(volumeIndex);
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#5b2c6f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Rare, string postProcessing = "lighting/postprocessing/effects/death_cam_phase1_low_violence.vpost", float fadeTime = .25f, float minExposure = .5f, float maxExposure = 2f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public string PostProcessing { get; set; } = postProcessing;
            public float FadeTime { get; set; } = fadeTime;
            public float MinExposure { get; set; } = minExposure;
            public float MaxExposure { get; set; } = maxExposure;
        }
    }
}
