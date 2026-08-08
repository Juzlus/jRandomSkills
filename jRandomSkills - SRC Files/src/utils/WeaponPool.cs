namespace src.utils
{
    public static class WeaponPool
    {
        private static readonly object buildLock = new();
        private static bool built;

        private static HashSet<string> rifles = new(StringComparer.Ordinal);
        private static HashSet<string> pistols = new(StringComparer.Ordinal);
        private static HashSet<string> grenades = new(StringComparer.Ordinal);
        private static HashSet<string> all = new(StringComparer.Ordinal);

        private static string[] rifleArray = [];
        private static string[] pistolArray = [];
        private static string[] grenadeArray = [];
        private static string[] allArray = [];

        public static HashSet<string> Rifles { get { Build(); return rifles; } }
        public static HashSet<string> Pistols { get { Build(); return pistols; } }
        public static HashSet<string> Grenades { get { Build(); return grenades; } }
        public static HashSet<string> All { get { Build(); return all; } }

        public static string[] RifleList { get { Build(); return rifleArray; } }
        public static string[] PistolList { get { Build(); return pistolArray; } }
        public static string[] GrenadeList { get { Build(); return grenadeArray; } }
        public static string[] AllList { get { Build(); return allArray; } }

        public static bool IsRifle(string? weapon) => weapon != null && Rifles.Contains(Normalize(weapon));
        public static bool IsPistol(string? weapon) => weapon != null && Pistols.Contains(Normalize(weapon));
        public static bool IsGrenade(string? weapon) => weapon != null && Grenades.Contains(Normalize(weapon));
        public static bool IsWeapon(string? weapon) => weapon != null && All.Contains(Normalize(weapon));

        public static string Normalize(string weapon)
        {
            if (string.IsNullOrEmpty(weapon)) return string.Empty;
            return weapon.StartsWith("weapon_", StringComparison.Ordinal) ? weapon : "weapon_" + weapon;
        }

        public static void Invalidate()
        {
            lock (buildLock) built = false;
        }

        private static void Build()
        {
            if (built) return;

            lock (buildLock)
            {
                if (built) return;

                var pools = Config.LoadedConfig?.Weapons;

                rifles = Collect(pools?.Rifle);
                pistols = Collect(pools?.Pistol);
                grenades = Collect(pools?.Grenade);

                all = new HashSet<string>(rifles, StringComparer.Ordinal);
                all.UnionWith(pistols);

                rifleArray = [.. rifles];
                pistolArray = [.. pistols];
                grenadeArray = [.. grenades];
                allArray = [.. all];

                built = true;
            }
        }

        private static HashSet<string> Collect(List<string>? source)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (source == null) return set;

            foreach (var entry in source)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                set.Add(Normalize(entry.Trim()));
            }

            return set;
        }
    }
}
