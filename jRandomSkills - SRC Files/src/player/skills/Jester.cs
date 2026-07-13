using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
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
        private static readonly ConcurrentDictionary<uint, JesterInfo> jesters = [];

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

                    var player = Utilities.GetPlayerFromIndex((int)jester.PlayerIndex);
                    if (player != null && player.IsValid)
                    {
                        SkillUtils.ResetPrintHTML(player);

                        var pawn = player.PlayerPawn.Value;
                        if (pawn == null || !pawn.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

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
            var jester = GetJesterInfo(player.Index);
            if (jester == null) return;

            jester.Generation++;
            jester.Active = false;
            jester.Timer = null;

            jesters.TryRemove(player.Index, out _);

            Server.NextWorldUpdate(() =>
            {
                SkillUtils.ResetPrintHTML(player);
                SetPlayerColor(player, true);
                
            });
        }

        public static void BombBegindefuse(EventBombBegindefuse @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            uint playerIndex = player.Index;
            var jester = GetJesterInfo(playerIndex);
            if (jester == null) return;

            if (jester.Timer != null)
            {
                jester.Timer?.Kill();
                jester.Timer = null;
            }

            jester.Timer = Instance.AddTimer(1, () => {
                ChangeMode(playerIndex, false);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        public static void BombBeginplant(EventBombBeginplant @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            uint playerIndex = player.Index;
            var jester = GetJesterInfo(playerIndex);
            if (jester == null) return;

            if (jester.Timer != null)
            {
                jester.Timer?.Kill();
                jester.Timer = null;
            }

            jester.Timer = Instance.AddTimer(1, () => {
                if (player != null && player.IsValid)
                    ChangeMode(playerIndex, false);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        public static void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);

            if (!Instance.IsPlayerValid(victim)) return;
            var jesterVictim = GetJesterInfo(victim!.Index);

            if (!Instance.IsPlayerValid(attacker))
            {
                if (jesterVictim != null && jesterVictim.Active)
                {
                    SkillUtils.RestoreHealth(victim);
                    RestoreArmor(victim, @event.DmgArmor);
                }
                return;
            }

            var jesterAttacker = GetJesterInfo(attacker!.Index);
            if ((jesterVictim != null && jesterVictim.Active) || (jesterAttacker != null && jesterAttacker.Active))
            {
                SkillUtils.RestoreHealth(victim);
                RestoreArmor(victim, @event.DmgArmor);
            }
        }

        private static void RestoreArmor(CCSPlayerController? victim, int dmgArmor)
        {
            if (victim == null || !victim.IsValid || dmgArmor <= 0) return;

            var pawn = victim.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return;

            pawn.ArmorValue += dmgArmor;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var minTime = SkillsInfo.GetValue<float>(skillName, "minTime");
            var maxTime = SkillsInfo.GetValue<float>(skillName, "maxTime");
            float wait = (float)Instance.Random.NextDouble() * (maxTime - minTime) + minTime;

            uint playerIndex = player.Index;

            if (!jesters.TryGetValue(playerIndex, out var jester))
            {
                jester = new JesterInfo
                {
                    PlayerIndex = playerIndex,
                    Active = false,
                    Generation = 0
                };

                jesters[playerIndex] = jester;
            }

            jester.Generation++;
            int generation = jester.Generation;

            jester.Timer = Instance.AddTimer(wait, () =>
            {
                if (!jesters.TryGetValue(playerIndex, out var info))
                    return;

                if (info.Generation != generation)
                    return;

                ChangeMode(playerIndex);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        private static void ChangeMode(uint playerIndex, bool? forceActive = null)
        {
            if (!jesters.TryGetValue(playerIndex, out var jester)) return;

            var player = Utilities.GetPlayerFromIndex((int)playerIndex);
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            {
                jesters.TryRemove(playerIndex, out _);
                return;
            }

            bool previousState = jester.Active;
            jester.Active = forceActive ?? !jester.Active;

            if (jester.Active != previousState)
            {
                SetPlayerColor(player);
             
                var playerEvent = PlayerManager.GetPlayerFromEvent(player);
                if (playerEvent != null && playerEvent.IsValid)
                    playerEvent.ExecuteClientCommand("play sounds/weapons/taser/taser_charge_ready");
            }

            var minTime = SkillsInfo.GetValue<float>(skillName, "minTime");
            var maxTime = SkillsInfo.GetValue<float>(skillName, "maxTime");
            float wait = (float)Instance.Random.NextDouble() * (maxTime - minTime) + minTime;

            jester.Generation++;
            int generation = jester.Generation;

            jester.Timer = Instance.AddTimer(wait, () =>
            {
                if (!jesters.TryGetValue(playerIndex, out var info))
                    return;

                if (info.Generation != generation)
                    return;

                ChangeMode(playerIndex);
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        private static void SetPlayerColor(CCSPlayerController? player, bool forceDisable = false)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            var jester = GetJesterInfo(player.Index);
            if (!forceDisable && jester == null) return;

            var color = jester?.Active == true && !forceDisable ? Color.FromArgb(255, 128, 0, 128) : Color.FromArgb(255, 255, 255, 255);
            pawn.Render = color;
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
        }

        public static void OnTick()
        {
            if (SkillUtils.IsFreezeTime()) return;
            foreach (var player in Utilities.GetPlayers())
            {
                var playeEvent = PlayerManager.GetPlayerEvent(player);
                if (playeEvent == null || !playeEvent.IsValid || playeEvent.PlayerPawn.Value == null || !playeEvent.PlayerPawn.Value.IsValid) continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(playeEvent.Index);

                if (playerInfo?.Skill == skillName)
                    UpdateHUD(playeEvent);
            }
        }

        private static void UpdateHUD(CCSPlayerController player)
        {
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            var jester = GetJesterInfo(player.Index);
            if (jester == null) return;

            playerInfo.PrintHTML = $"{player.GetTranslation("jester_mode")}: <font color='{(jester.Active ? "#00ff00" : "#ff0000")}'>{player.GetTranslation(jester.Active ? "jester_on" : "jester_off")}</font>";
        }

        public static JesterInfo? GetJesterInfo(uint playerIndex)
        {
            if (jesters.TryGetValue(playerIndex, value: out var info))
                return info;
            return null;
        }

        public class JesterInfo()
        {
            public required uint PlayerIndex { get; set; }
            public bool Active { get; set; } = false;
            public int Generation { get; set; } = 0;
            public Timer? Timer { get; set; } = null;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#8f108f", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float minTime = 10f, float maxTime = 25f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float MinTime { get; set; } = minTime;
            public float MaxTime { get; set; } = maxTime;
        }
    }
}