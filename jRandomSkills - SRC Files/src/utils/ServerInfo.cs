using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;

namespace src.utils
{
    // The server owner's own chat lines (Config.ServerInfo): shown on join and repeated on a timer.
    public static class ServerInfo
    {
        private static CounterStrikeSharp.API.Modules.Timers.Timer? advertTimer;

        public static void Load()
        {
            advertTimer?.Kill();
            advertTimer = null;

            int interval = Config.LoadedConfig.ServerInfo.AdvertIntervalSeconds;
            if (interval <= 0 || Config.LoadedConfig.ServerInfo.Lines.Count == 0) return;

            advertTimer = Instance.AddTimer(interval, () =>
            {
                foreach (var player in Utilities.GetPlayers())
                    if (player != null && player.IsValid && !player.IsBot)
                        SendTo(player);
            }, TimerFlags.REPEAT);
        }

        public static void SendTo(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.IsBot) return;

            foreach (var line in Config.LoadedConfig.ServerInfo.Lines)
                if (!string.IsNullOrWhiteSpace(line))
                    player.PrintToChat($" {ChatColors.Gold}{line}");
        }
    }
}
