using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills
{
    public class Jester : ISkill
    {
        private const Skills skillName = Skills.Jester;
        private static readonly ConcurrentDictionary<ulong, JesterInfo> jesters = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            Server.NextWorldUpdate(() =>
            {
                foreach (var jester in jesters.Values)
                {
                    if (jester.Timer != null)
                    {
                        jester.Timer?.Kill();
                        jester.Timer = null;
                    }

                    var player = Utilities.GetPlayerFromSteamId(jester.SteamId);
                    if (player != null && player.IsValid)
                    {
                        SkillUtils.ResetPrintHTML(player);

                        var pawn = player.PlayerPawn.Value;
                        if (pawn == null || !pawn.IsValid || !player.PawnIsAlive) return;

                        var color = Color.FromArgb(255, 255, 255, 255);
                        pawn.Render = color;
                        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
                    }
                }
                jesters.Clear();
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            var jester = GetJesterInfo(player.SteamID);
            if (jester == null) return;

            Server.NextWorldUpdate(() =>
            {
                if (jester.Timer != null)
                {
                    jester.Timer?.Kill();
                    jester.Timer = null;
                }

                SkillUtils.ResetPrintHTML(player);
                SetPlayerColor(player, true);
            });
        }

        public static void BombBegindefuse(EventBombBegindefuse @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            ulong steamId = player.SteamID;
            var jester = GetJesterInfo(steamId);
            if (jester == null) return;

            if (jester.Timer != null)
            {
                jester.Timer?.Kill();
                jester.Timer = null;
            }

            jester.Timer = Instance.AddTimer(1, () => {
                ChangeMode(steamId, false);
            });
        }

        public static void BombBeginplant(EventBombBeginplant @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return;

            ulong steamId = player.SteamID;
            var jester = GetJesterInfo(steamId);
            if (jester == null) return;

            if (jester.Timer != null)
            {
                jester.Timer?.Kill();
                jester.Timer = null;
            }

            jester.Timer = Instance.AddTimer(1, () => {
                if (player != null && player.IsValid)
                    ChangeMode(steamId, false);
            });
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            int hitgroup = @event.Hitgroup;

            if (!Instance.IsPlayerValid(victim)) return;
            var jesterVictim = GetJesterInfo(victim!.SteamID);

            if (!Instance.IsPlayerValid(attacker))
            {
                if (jesterVictim != null && jesterVictim.Active)
                    SkillUtils.RestoreHealth(victim);
                return;
            }

            var jesterAttacker = GetJesterInfo(attacker!.SteamID);
            if ((jesterVictim != null && jesterVictim.Active) || (jesterAttacker != null && jesterAttacker.Active))
                SkillUtils.RestoreHealth(victim);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var minTime = SkillsInfo.GetValue<float>(skillName, "minTime");
            var maxTime = SkillsInfo.GetValue<float>(skillName, "maxTime");
            float wait = (float)Instance.Random.NextDouble() * (maxTime - minTime) + minTime;

            ulong steamId = player.SteamID;

            jesters.TryAdd(steamId, new JesterInfo {
                SteamId = steamId,
                Active = false,
                Timer = Instance.AddTimer(wait, () => ChangeMode(steamId))
            });
        }

        private static void ChangeMode(ulong steamId, bool? forceActive = null)
        {
            if (!jesters.TryGetValue(steamId, out var jester)) return;

            if (jester.Timer != null)
            {
                jester.Timer?.Kill();
                jester.Timer = null;
            }

            var player = Utilities.GetPlayerFromSteamId(steamId);
            if (player == null || !player.IsValid || !player.PawnIsAlive)
            {
                jesters.TryRemove(steamId, out _);
                return;
            }

            bool previousState = jester.Active;
            jester.Active = forceActive ?? !jester.Active;

            if (jester.Active != previousState)
            {
                SetPlayerColor(player);
                player.ExecuteClientCommand("play sounds/weapons/taser/taser_charge_ready");
            }

            var minTime = SkillsInfo.GetValue<float>(skillName, "minTime");
            var maxTime = SkillsInfo.GetValue<float>(skillName, "maxTime");
            float wait = (float)Instance.Random.NextDouble() * (maxTime - minTime) + minTime;

            jester.Timer = Instance.AddTimer(wait, () => ChangeMode(steamId));
        }

        private static void SetPlayerColor(CCSPlayerController? player, bool forceDisable = false)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            var jester = GetJesterInfo(player.SteamID);
            if (jester == null) return;

            var color = jester.Active && !forceDisable ? Color.FromArgb(255, 128, 0, 128) : Color.FromArgb(255, 255, 255, 255);
            pawn.Render = color;
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
        }

        public static void OnTick()
        {
            if (SkillUtils.IsFreezeTime()) return;
            foreach (var player in Utilities.GetPlayers().Where(p => !p.IsBot && p.PawnIsAlive))
            {
                if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) continue;
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player?.SteamID);
                if (playerInfo?.Skill == skillName)
                    UpdateHUD(player);
            }
        }

        private static void UpdateHUD(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            var jester = GetJesterInfo(player.SteamID);
            if (jester == null) return;

            playerInfo.PrintHTML = $"{player.GetTranslation("jester_mode")}: <font color='{(jester.Active ? "#00ff00" : "#ff0000")}'>{player.GetTranslation(jester.Active ? "jester_on" : "jester_off")}</font>";
        }

        public static JesterInfo? GetJesterInfo(ulong steamID)
        {
            if (jesters.TryGetValue(steamID, value: out var info))
                return info;
            return null;
        }

        public class JesterInfo()
        {
            public required ulong SteamId {  get; set; }
            public bool Active { get; set; } = false;
            public Timer? Timer { get; set; } = null;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8f108f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float minTime = 10f, float maxTime = 25f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float MinTime { get; set; } = minTime;
            public float MaxTime { get; set; } = maxTime;
        }
    }
}