using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using static src.jRandomSkills;

namespace src.player.skills
{
    public class Coins : ISkill
    {
        private const Skills skillName = Skills.Coins;
        private const string coinModel = "models/gameplay/cash_stack/cash_stack.vmdl";
        private const string pickupSound = "CollectableCoin.Collect";
        private const int DefaultKillAward = 300;

        private sealed class Coin
        {
            public uint Index;
            public int Value;
            public int PickupTick;
            public int ExpireTick;
            public CsTeam Team;
        }

        private static readonly List<Coin> coins = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(coinModel);
        }

        public static void NewRound()
        {
            lock (setLock)
                coins.Clear();
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = @event.Userid;
            var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
            if (victim == null || !victim.IsValid || attacker == null || !attacker.IsValid) return;
            if (victim.Key() == attacker.Key() || victim.Team == attacker.Team) return;
            if (PlayerManager.GetPlayer(attacker)?.Skill != skillName) return;

            var victimPawn = victim.PlayerPawn?.Value;
            if (victimPawn == null || !victimPawn.IsValid || victimPawn.AbsOrigin == null) return;

            int total = (int)MathF.Round(KillAward(attacker) * Math.Max(0f, SkillsInfo.GetValue<float>(skillName, "multiplier")));
            if (total <= 0) return;

            Vector origin = new(victimPawn.AbsOrigin.X, victimPawn.AbsOrigin.Y, victimPawn.AbsOrigin.Z + 40f);
            uint ownerKey = attacker.Key();
            CsTeam team = attacker.Team;
            Server.NextFrame(() => Burst(ownerKey, team, origin, total));
        }

        private static int KillAward(CCSPlayerController attacker)
        {
            var weapon = attacker.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon?.Value;
            if (weapon == null || !weapon.IsValid) return DefaultKillAward;

            var vdata = weapon.As<CCSWeaponBase>().GetVData<CCSWeaponBaseVData>();
            return vdata != null && vdata.KillAward > 0 ? vdata.KillAward : DefaultKillAward;
        }

        private static void Burst(uint ownerKey, CsTeam team, Vector origin, int total)
        {
            int coinValue = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "coinValue"));
            int maxCoins = Math.Max(1, SkillsInfo.GetValue<int>(skillName, "maxCoins"));
            int count = Math.Clamp((int)Math.Ceiling(total / (double)coinValue), 1, maxCoins);
            float speed = Math.Max(0f, SkillsInfo.GetValue<float>(skillName, "launchSpeed"));
            int lifetime = (int)(Math.Max(1f, SkillsInfo.GetValue<float>(skillName, "lifetime")) * 64);
            int tick = Server.TickCount;

            for (int i = 0; i < count; i++)
            {
                int value = total / count + (i < total % count ? 1 : 0);
                if (value <= 0) continue;

                var prop = EntityManager.CreateTrackedPhysicsProp(ownerKey);
                if (prop == null || !prop.IsValid) break;

                using (var keys = new CEntityKeyValues())
                {
                    keys.SetString("model", coinModel);
                    keys.SetVector("origin", origin);
                    prop.DispatchSpawn(keys);
                }
                if (!prop.IsValid) continue;

                if (prop.Collision != null)
                {
                    prop.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
                    prop.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
                    Utilities.SetStateChanged(prop, "CCollisionProperty", "m_collisionAttribute");
                }

                float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                float push = speed * (.5f + (float)Random.Shared.NextDouble() * .5f);
                Vector velocity = new(MathF.Cos(angle) * push, MathF.Sin(angle) * push, speed * (.8f + (float)Random.Shared.NextDouble() * .4f));
                QAngle rotation = new(0, (float)(Random.Shared.NextDouble() * 360), 0);
                prop.Teleport(origin, rotation, velocity);

                lock (setLock)
                    coins.Add(new Coin { Index = prop.Index, Value = value, PickupTick = tick + 24, ExpireTick = tick + lifetime, Team = team });
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % 4 != 0) return;

            lock (setLock)
            {
                if (coins.Count == 0) return;

                int tick = Server.TickCount;
                float radius = Math.Max(8f, SkillsInfo.GetValue<float>(skillName, "pickupRadius"));
                float radiusSq = radius * radius;
                var alive = PlayerManager.GetAliveSnapshot();

                for (int i = coins.Count - 1; i >= 0; i--)
                {
                    var coin = coins[i];
                    var prop = Utilities.GetEntityFromIndex<CPhysicsPropMultiplayer>((int)coin.Index);
                    if (prop == null || !prop.IsValid || prop.AbsOrigin == null)
                    {
                        coins.RemoveAt(i);
                        continue;
                    }

                    if (tick >= coin.ExpireTick)
                    {
                        coins.RemoveAt(i);
                        EntityManager.DestroyEntity(coin.Index);
                        continue;
                    }

                    if (tick < coin.PickupTick) continue;

                    var pos = prop.AbsOrigin;
                    foreach (var player in alive)
                    {
                        if (player.Team != coin.Team) continue;

                        float dx = player.X - pos.X, dy = player.Y - pos.Y, dz = player.Z + 32f - pos.Z;
                        if (dx * dx + dy * dy + dz * dz > radiusSq) continue;

                        Collect(player.Controller, coin.Value);
                        coins.RemoveAt(i);
                        EntityManager.DestroyEntity(coin.Index);
                        break;
                    }
                }
            }
        }

        private static void Collect(CCSPlayerController player, int value)
        {
            var collector = PlayerManager.GetPlayerFromEvent(player) ?? player;
            var money = collector.InGameMoneyServices;
            if (money == null) return;

            int maxMoney = SkillUtils.CvarValue("mp_maxmoney", 16000);
            money.Account = Math.Min(money.Account + value, Math.Max(maxMoney, money.Account));
            Utilities.SetStateChanged(collector, "CCSPlayerController", "m_pInGameMoneyServices");

            SkillUtils.EmitSoundToPlayer(collector, pickupSound, SkillsInfo.GetValue<float>(skillName, "soundVolume"));
            collector.PrintToCenter($"+${value}");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#f2c94c", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, float multiplier = 1f, int coinValue = 100, int maxCoins = 10, float launchSpeed = 220f, float pickupRadius = 56f, float lifetime = 30f, float soundVolume = .7f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float Multiplier { get; set; } = multiplier;
            public int CoinValue { get; set; } = coinValue;
            public int MaxCoins { get; set; } = maxCoins;
            public float LaunchSpeed { get; set; } = launchSpeed;
            public float PickupRadius { get; set; } = pickupRadius;
            public float Lifetime { get; set; } = lifetime;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
