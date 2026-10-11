using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;

namespace src.player.skills
{
    // Press the use key to turn the whole round back by a few seconds: every player goes back to where they
    // stood, with the health and armour they had, and anyone who died in between comes back to life there
    // with the weapons they were holding.
    public class TimeLord : ISkill
    {
        private const Skills skillName = Skills.TimeLord;

        private sealed class Sample
        {
            public int Tick;
            public Vector Position = new();
            public QAngle Angles = new();
            public int Health;
            public int Armor;
            public bool Helmet;
            public List<string> Weapons = [];
            public string? ActiveWeapon;
        }

        private sealed class HolderInfo
        {
            public DateTime Cooldown = DateTime.MinValue;
            public int UsesLeft;
        }

        // Round history of every player on a team, kept only while someone holds the skill.
        private static readonly ConcurrentDictionary<uint, Queue<Sample>> history = [];
        private static readonly ConcurrentDictionary<uint, HolderInfo> holders = [];
        private static int lastSampleTick = int.MinValue;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            history.Clear();
            foreach (var holder in holders.Values)
            {
                holder.UsesLeft = SkillsInfo.GetValue<int>(skillName, "usesPerRound");
                holder.Cooldown = DateTime.MinValue;
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            holders[player.Index] = new HolderInfo { UsesLeft = SkillsInfo.GetValue<int>(skillName, "usesPerRound") };
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            holders.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
            if (holders.IsEmpty) history.Clear();
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            holders.TryRemove(playerIndex, out _);
            history.TryRemove(playerIndex, out _);
        }

        public static void OnTick()
        {
            if (holders.IsEmpty) return;

            if (SkillUtils.IsHudFrame())
            {
                float cooldown = SkillsInfo.GetValue<float>(skillName, "cooldown");
                foreach (var (index, holder) in holders)
                {
                    var player = Utilities.GetPlayerFromIndex((int)index);
                    var info = player != null && player.IsValid ? PlayerManager.GetPlayerByIndex(index) : null;
                    if (info == null) continue;

                    int left = (int)Math.Ceiling((holder.Cooldown.AddSeconds(cooldown) - DateTime.Now).TotalSeconds);
                    if (holder.UsesLeft <= 0)
                        info.PrintHTML = player!.GetTranslation("timelord_used_info");
                    else
                        info.PrintHTML = left > 0 ? $"{player!.GetTranslation("hud_info", $"<font color='#FF0000'>{left}</font>")}" : null;
                }
            }

            // A few samples a second is plenty for a rewind and cheap to keep.
            int sampleTicks = Math.Max(4, SkillsInfo.GetValue<int>(skillName, "sampleTicks"));
            if (Server.TickCount - lastSampleTick < sampleTicks) return;
            lastSampleTick = Server.TickCount;

            if (SkillUtils.IsFreezeTime()) return;

            int keep = Math.Max(1, (int)Math.Ceiling(SkillsInfo.GetValue<float>(skillName, "secondsInBack") * 64f / sampleTicks)) + 1;

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid || player.IsHLTV) continue;
                if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) continue;

                var pawn = player.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;

                var queue = history.GetOrAdd(player.Index, _ => new Queue<Sample>());

                // Dead players add nothing: the last sample from when they were alive is what brings them back.
                if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || pawn.AbsOrigin == null)
                {
                    // Keep the window moving so a death older than the window is not undone.
                    if (queue.Count > 0 && Server.TickCount - queue.Peek().Tick > keep * sampleTicks)
                        queue.Dequeue();
                    continue;
                }

                var sample = new Sample
                {
                    Tick = Server.TickCount,
                    Position = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z),
                    Angles = new QAngle(pawn.V_angle.X, pawn.V_angle.Y, 0),
                    Health = pawn.Health,
                    Armor = pawn.ArmorValue,
                    Helmet = pawn.ItemServices != null && new CCSPlayer_ItemServices(pawn.ItemServices.Handle).HasHelmet,
                };

                var weapons = pawn.WeaponServices;
                if (weapons != null)
                {
                    foreach (var handle in weapons.MyWeapons)
                    {
                        var weapon = handle.Value;
                        if (weapon == null || !weapon.IsValid) continue;
                        string name = weapon.DesignerName;
                        if (string.IsNullOrEmpty(name) || name == "weapon_c4") continue;
                        sample.Weapons.Add(name);
                    }
                    sample.ActiveWeapon = weapons.ActiveWeapon.Value?.DesignerName;
                }

                queue.Enqueue(sample);
                while (queue.Count > keep) queue.Dequeue();
            }
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (!holders.TryGetValue(player.Index, out var holder)) return;

            float cooldown = SkillsInfo.GetValue<float>(skillName, "cooldown");
            if (holder.UsesLeft <= 0 || holder.Cooldown.AddSeconds(cooldown) > DateTime.Now) return;

            float seconds = SkillsInfo.GetValue<float>(skillName, "secondsInBack");
            int targetTick = Server.TickCount - (int)(seconds * 64f);
            int rewound = 0, revived = 0;

            foreach (var (index, queue) in history)
            {
                if (queue.Count == 0) continue;

                var target = Utilities.GetPlayerFromIndex((int)index);
                if (target == null || !target.IsValid || target.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) continue;

                // The sample closest to "secondsInBack ago" (the oldest one when the round is younger than that).
                Sample? sample = null;
                foreach (var s in queue)
                {
                    sample = s;
                    if (s.Tick >= targetTick) break;
                }
                if (sample == null) continue;

                var pawn = target.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;

                if (pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                {
                    Restore(target, sample, false);
                    rewound++;
                }
                else if (SkillsInfo.GetValue<bool>(skillName, "reviveDead"))
                {
                    // Only someone who was alive inside the window comes back.
                    if (Server.TickCount - sample.Tick > seconds * 64f + 64) continue;

                    var captured = sample;
                    var who = target;
                    who.Respawn();
                    Server.NextFrame(() => Restore(who, captured, true));
                    revived++;
                }
            }

            if (rewound == 0 && revived == 0)
            {
                player.PrintToChat($" {ChatColors.Red}{player.GetTranslation("timelord_nothing")}");
                return;
            }

            holder.UsesLeft--;
            holder.Cooldown = DateTime.Now;

            float volume = SkillsInfo.GetValue<float>(skillName, "soundVolume");
            foreach (var other in PlayerManager.GetTickPlayers())
            {
                if (other == null || !other.IsValid || other.IsBot) continue;
                other.PrintToChat($" {ChatColors.Gold}{other.GetTranslation("timelord_used", $"{ChatColors.Lime}{player.PlayerName}{ChatColors.Gold}", (int)seconds, revived)}");
                SkillUtils.EmitSoundToPlayer(other, "Player.Respawn", volume);
            }
        }

        private static void Restore(CCSPlayerController target, Sample sample, bool revived)
        {
            if (target == null || !target.IsValid) return;
            var pawn = target.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            pawn.Teleport(sample.Position, null, new Vector(0, 0, 0));
            pawn.Look(sample.Angles);

            if (SkillsInfo.GetValue<bool>(skillName, "restoreHealth") || revived)
            {
                pawn.Health = Math.Max(1, sample.Health);
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
                pawn.ArmorValue = sample.Armor;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
                if (pawn.ItemServices != null)
                    new CCSPlayer_ItemServices(pawn.ItemServices.Handle).HasHelmet = sample.Helmet;
            }

            if (revived)
            {
                // Back with what they were holding, not the spawn kit.
                target.RemoveWeapons();
                foreach (var weapon in sample.Weapons)
                    target.GiveNamedItem(weapon);
                if (!sample.Weapons.Contains("weapon_knife"))
                    target.GiveNamedItem("weapon_knife");
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#36c9ff", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = 1, Rarity rarity = Rarity.Legendary, float secondsInBack = 7f, float cooldown = 30f, int usesPerRound = 1, int sampleTicks = 8, bool reviveDead = true, bool restoreHealth = true, float soundVolume = 1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float SecondsInBack { get; set; } = secondsInBack;
            public float Cooldown { get; set; } = cooldown;
            public int UsesPerRound { get; set; } = usesPerRound;
            public int SampleTicks { get; set; } = sampleTicks;
            public bool ReviveDead { get; set; } = reviveDead;
            public bool RestoreHealth { get; set; } = restoreHealth;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
