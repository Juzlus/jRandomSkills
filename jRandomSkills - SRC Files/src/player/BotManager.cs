using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.utils
{
    public static class BotManager
    {
        private static Timer? _skillTimer;
        private static Timer? _rotationTimer;
        private static readonly Random _random = new();

        private const float SkillInterval = 2f;
        private const float RotationInterval = 45f;

        public static void Initialize()
        {
            if (!Config.LoadedConfig.EnableBotSkills) return;

            Stop();
            _skillTimer = Instance.AddTimer(SkillInterval, OnBotUseSkillTimer, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

            if (!Config.LoadedConfig.EnableBotKickDebug) return;
           
            _rotationTimer = Instance.AddTimer(RotationInterval, OnBotRotationTimer, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        public static void Stop()
        {
            if (!Config.LoadedConfig.EnableBotSkills) return;

            _skillTimer?.Kill();
            _skillTimer = null;

            if (!Config.LoadedConfig.EnableBotKickDebug) return;

            _rotationTimer?.Kill();
            _rotationTimer = null;
        }

        private static void OnBotUseSkillTimer()
        {
            var activeBots = Utilities.GetPlayers()
                .Where(p => p != null && p.IsValid && p.IsBot && !p.IsHLTV && p.LifeState == (byte)LifeState_t.LIFE_ALIVE && p.Team != CsTeam.Spectator)
                .ToList();

            if (activeBots.Count == 0) return;

            var randomBot = activeBots[_random.Next(activeBots.Count)];
            if (randomBot == null || !randomBot.IsValid) return;
            if (randomBot.Index != randomBot.OriginalControllerOfCurrentPawn.Value?.Index) return;
            if (randomBot.PlayerPawn?.Value == null || !randomBot.PlayerPawn.Value.IsValid || randomBot.PlayerPawn.Value.IsDefusing) return;

            var bot_info = PlayerManager.GetPlayerByIndex(randomBot.Index);
            if (bot_info == null) return;

            Instance.SkillAction(bot_info.Skill.ToString(), "UseSkill", [randomBot]);
        }

        private static void OnBotRotationTimer()
        {
            var allBots = Utilities.GetPlayers()
                .Where(p => p != null && p.IsValid && p.IsBot && !p.IsHLTV)
                .ToList();

            if (allBots.Count > 0)
            {
                var botToKick = allBots[_random.Next(allBots.Count)];
                Server.ExecuteCommand($"kickid {botToKick.UserId}");
            }

            Server.NextFrame(() => Server.ExecuteCommand("bot_add"));
        }
    }
}