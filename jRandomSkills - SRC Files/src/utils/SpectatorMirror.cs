using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace src.utils
{
    public sealed class SpectatorMirror(Action<CCSPlayerController, uint> apply, Action<CCSPlayerController> clear, int refreshTicks = 0)
    {
        private readonly Dictionary<uint, uint> viewers = [];
        private readonly List<uint> stale = [];
        private readonly object mirrorLock = new();

        public void Update(Func<uint, bool> isAffected)
        {
            lock (mirrorLock)
            {
                var observers = SkillUtils.GetFirstPersonObservers();
                if (viewers.Count == 0 && observers.Count == 0) return;

                bool refresh = refreshTicks > 0 && Server.TickCount % refreshTicks == 0;

                stale.Clear();
                foreach (var (viewerKey, targetKey) in viewers)
                    if (!observers.TryGetValue(viewerKey, out var observer) || observer.TargetKey != targetKey || !isAffected(targetKey))
                        stale.Add(viewerKey);

                foreach (var viewerKey in stale)
                {
                    viewers.Remove(viewerKey);
                    var viewer = PlayerManager.GetController(viewerKey);
                    if (viewer != null && viewer.IsValid)
                        clear(viewer);
                }

                foreach (var (viewerKey, observer) in observers)
                {
                    if (!isAffected(observer.TargetKey)) continue;
                    if (viewers.ContainsKey(viewerKey) && !refresh) continue;

                    viewers[viewerKey] = observer.TargetKey;
                    apply(observer.Viewer, observer.TargetKey);
                }
            }
        }

        public void Reset()
        {
            lock (mirrorLock)
            {
                foreach (var viewerKey in viewers.Keys)
                {
                    var viewer = PlayerManager.GetController(viewerKey);
                    if (viewer != null && viewer.IsValid)
                        clear(viewer);
                }

                viewers.Clear();
            }
        }

        public void Forget(uint viewerKey)
        {
            lock (mirrorLock)
                viewers.Remove(viewerKey);
        }
    }
}
