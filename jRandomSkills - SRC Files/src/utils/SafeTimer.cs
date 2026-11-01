using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.utils
{
    public static class SafeTimer
    {
        private static readonly object gate = new();
        private static readonly Dictionary<Timer, bool> live = [];

        public static Timer Add(float interval, Action callback, TimerFlags? flags = null)
        {
            bool repeat = flags?.HasFlag(TimerFlags.REPEAT) == true;
            bool stopOnMapChange = flags?.HasFlag(TimerFlags.STOP_ON_MAPCHANGE) == true;

            Timer? timer = null;
            timer = jRandomSkills.Instance.AddTimer(interval, () =>
            {
                if (!repeat && timer != null)
                    lock (gate)
                        live.Remove(timer);

                callback();
            }, flags);

            lock (gate)
                live[timer] = stopOnMapChange;

            return timer;
        }

        public static Timer AddTick(int ticks, Action callback, TimerFlags? flags = null)
        {
            return Add(ticks * Server.TickInterval, callback, flags);
        }

        public static void Kill(Timer? timer)
        {
            if (timer == null) return;

            lock (gate)
                if (!live.Remove(timer)) return;

            timer.Kill();
        }

        public static void ForgetMapTimers()
        {
            lock (gate)
                foreach (var timer in live.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList())
                    live.Remove(timer);
        }
    }
}
