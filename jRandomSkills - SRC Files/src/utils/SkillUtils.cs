using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.player;
using src.player.skills;
using System.Collections.Concurrent;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using WASDMenuAPI.Classes;
using WASDSharedAPI;

namespace src.utils
{
    public static class SkillUtils
    {
        private static readonly ConcurrentDictionary<string, ConVar> cvarCache = [];

        public static ConVar? Cvar(string name)
        {
            if (cvarCache.TryGetValue(name, out var cached)) return cached;

            var cvar = ConVar.Find(name);
            if (cvar != null) cvarCache[name] = cvar;
            return cvar;
        }

        public static T CvarValue<T>(string name, T fallback) where T : unmanaged
        {
            var cvar = Cvar(name);
            if (cvar == null) return fallback;

            try { return cvar.GetPrimitiveValue<T>(); }
            catch { return fallback; }
        }

        public static string CvarString(string name, string fallback)
        {
            var cvar = Cvar(name);
            return cvar == null ? fallback : cvar.StringValue;
        }

        private static Lazy<T?> LazySig<T>(string name, Func<string, T> factory) where T : class =>
            new(() =>
            {
                try { return factory(GameData.GetSignature(name)); }
                catch (Exception ex) { Server.PrintToConsole($"[jRandomSkills] gamedata signature '{name}' could not be resolved: {ex.Message}"); return null; }
            });

        private static readonly Lazy<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>?> HEGrenadeProjectile_CreateFunc =
            LazySig<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>>("HEGrenadeProjectile_CreateFunc", s => new(s));
        private static readonly Lazy<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>?> SmokeGrenadeProjectile_CreateFunc =
            LazySig<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>>("SmokeGrenadeProjectile_CreateFunc", s => new(s));
        private static readonly Lazy<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>?> CMolotovProjectile_CreateFunc =
            LazySig<MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>>("CMolotovProjectile_CreateFunc", s => new(s));
        private static readonly Lazy<MemoryFunctionVoid<nint, float, RoundEndReason, nint, nint>?> TerminateRoundFunc =
            LazySig<MemoryFunctionVoid<nint, float, RoundEndReason, nint, nint>>("CCSGameRules_TerminateRound", s => new(s));
        private static readonly Lazy<MemoryFunctionVoid<CBasePlayerPawn, QAngle>?> SnapViewAngles =
            LazySig<MemoryFunctionVoid<CBasePlayerPawn, QAngle>>("SnapViewAngles", s => new(s));
        // private static readonly int collisionRulesChangedOffset = GameData.GetOffset("CBaseEntity_CollisionRulesChanged");

        public static void PrintToChat(CCSPlayerController player, string? msg, string border = "tb", string? title = null, bool ignoreIlliterate = false)
        {
            if (!player.IsValid) return;

            var config = Config.LoadedConfig.ChatMessage;
            float maxWidth = config.MaxWidth;
            char symbol = config.LineSymbol;
            if (string.IsNullOrEmpty(title)) title = player.GetTranslation("jRandomSkills");

            if (!ignoreIlliterate && Illiterate.CheckIlliterateSkill(player))
                msg = Illiterate.GetRandomText(msg);

            if (border.Contains('t') && config.LineShow)
                player.PrintToChat($" {MeansureString.GetTextDashed($"{(config.TagFormat.Contains("{TAG}") ? config.TagFormat.Replace("{TAG}", title) : $"\u0002◢◆◤ {title} ◥◆◣")}", maxWidth, symbol, config.LineColor)}");
            if (!string.IsNullOrEmpty(msg) && config.InfoMessageShow)
                player.PrintToChat($" {config.InfoSkillColor} {msg.Replace("\x02", config.InfoPlayerNameColor).Replace("\x06", config.InfoSkillColor)}");
            if (border.Contains('b') && config.LineShow)
                player.PrintToChat($" {MeansureString.GetTextDashed("", maxWidth, symbol, config.LineColor)}");
        }

        public static void EmitSoundToPlayer(CCSPlayerController? listener, string soundEvent, float volume)
        {
            var target = PlayerManager.GetPlayerFromEvent(listener);
            if (target == null || !target.IsValid) return;

            target.EmitSound(soundEvent, new RecipientFilter(target), volume);
        }

        public static bool IsFreezeTime()
        {
            return jRandomSkills.Instance?.GameRules?.FreezePeriod == true;
        }

        public static bool IsPistolRound()
        {
            var gameRules = jRandomSkills.Instance?.GameRules;
            if (gameRules == null) return false;

            if (gameRules.TotalRoundsPlayed == 0 || gameRules.GameRestart) return true;

            try
            {
                if (!CvarValue("mp_halftime", false)) return false;

                int maxRounds = CvarValue("mp_maxrounds", 0);
                return maxRounds > 0 && maxRounds / 2 == gameRules.TotalRoundsPlayed;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsHudFrame() => Server.TickCount % 4 == 0;

        public static void RegisterSkill(Skills skill, string color, bool display = true)
        {
            if (!SkillData.Skills.Any(s => s.Skill == skill))
            {
                SkillData.Skills.Add(new jSkill_SkillInfo(skill, color, display));
                SkillData.Invalidate();
            }
        }

        public static void UpdateGrenadeCount(CCSPlayerController player, CsItem item, int ammo)
        {
            string? itemString = EnumUtils.GetEnumMemberAttributeValue(item);
            if (string.IsNullOrWhiteSpace(itemString)) return;

            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            if (player.PlayerPawn.Value.WeaponServices == null) return;

            var weapon = player.PlayerPawn.Value.WeaponServices.MyWeapons
                .FirstOrDefault(w => w != null && w.IsValid && w.Value != null && w.Value.IsValid && !string.IsNullOrEmpty(w.Value.DesignerName) && w.Value.DesignerName == itemString);

            if (weapon == null || !weapon.IsValid || weapon.Value == null || !weapon.Value.IsValid) return;

            weapon.Value.Clip1 = ammo;
            Utilities.SetStateChanged(weapon.Value, "CBasePlayerWeapon", "m_iClip1");

            if (ammo == 1) return;

            jRandomSkills.Instance.AddTimer(.1f, () =>
            {
                if (weapon == null || !weapon.IsValid || weapon.Value == null || !weapon.Value.IsValid) return;
                weapon.Value.Clip1 = 1;
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        public static void TryGiveWeapon(CCSPlayerController player, CsItem item, int count = 1, bool existValidator = true)
        {
            string? itemString = EnumUtils.GetEnumMemberAttributeValue(item);
            if (string.IsNullOrWhiteSpace(itemString)) return;

            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            if (player.PlayerPawn.Value.WeaponServices == null) return;

            var exists = player.PlayerPawn.Value.WeaponServices.MyWeapons
                .FirstOrDefault(w => w != null && w.IsValid && w.Value != null && w.Value.IsValid && w.Value.DesignerName == itemString);

            if (exists == null || !existValidator)
                for (int i = 0; i < count; i++)
                    player.GiveNamedItem(item);
        }

        public static double GetDistance(Vector vector1, Vector vector2)
        {
            return Math.Sqrt(Math.Pow(vector2.X - vector1.X, 2) + Math.Pow(vector2.Y - vector1.Y, 2) + Math.Pow(vector2.Z - vector1.Z, 2));
        }

        public static float Distance(this Vector vector1, Vector vector2)
        {
            return (float)GetDistance(vector1, vector2);
        }

        public static float Dot(this Vector vector1, Vector vector2)
        {
            return (vector1.X * vector2.X) + (vector1.Y * vector2.Y) + (vector1.Z * vector2.Z);
        }

        public static Vector Normalize(this Vector vector)
        {
            float length = vector.Length();
            if (length > 0)
                return new Vector(vector.X / length, vector.Y / length, vector.Z / length);
            return Vector.Zero;
        }

        public static string SecondsToTimer(int totalSeconds)
        {
            if (totalSeconds <= 0) return "00:00";
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return $"{minutes:D2}:{seconds:D2}";
        }

        public static void SafeKillEntity<T>(uint? index) where T : CBaseEntity
        {
            if (index == null) return;
            EntityManager.DestroyEntity(index.Value);
        }

        public static bool IsValid<T>(this CHandle<T>? handle) where T : NativeEntity
        {
            return handle != null && handle.IsValid && handle.Value != null;
        }

        public static bool IsValid(this CBaseEntity? ent)
        {
            return ent != null && ent.IsValid;
        }

        public static bool CheckPlayer(this CCSPlayerController? player)
        {
            return player != null
                && player.IsValid
                && player.PlayerPawn?.Value?.IsValid() == true
                && player.PawnIsAlive
                && (player?.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist);
        }


        public static Vector GetForwardVector(QAngle angles)
        {
            float pitch = -angles.X * (float)(Math.PI / 180);
            float yaw = angles.Y * (float)(Math.PI / 180);

            float x = (float)(Math.Cos(pitch) * Math.Cos(yaw));
            float y = (float)(Math.Cos(pitch) * Math.Sin(yaw));
            float z = (float)Math.Sin(pitch);

            return new Vector(x, y, z);
        }

        public static void Look(this CBasePlayerPawn pawn, QAngle angle)
        {
            if (pawn == null || !pawn.IsValid) return;
            SnapViewAngles.Value?.Invoke(pawn, angle);
        }

        //public static void CollisionRulesChanged(CBaseEntity? entity)
        //{
        //    if (entity == null || !entity.IsValid || collisionRulesChangedOffset <= 0) return;

        //    var collisionRulesChanged = new VirtualFunctionVoid<nint>(entity.Handle, collisionRulesChangedOffset);
        //    collisionRulesChanged.Invoke(entity.Handle);
        //}

        public static void ApplyScreenColor(CCSPlayerController? player, int r, int g, int b, int a, int duration, int holdTime, int flags = 1)
        {
            if (player == null || !player.IsValid) return;

            using var msg = UserMessage.FromPartialName("Fade");
            if (msg == null) return;
            int packageColor = (a << 24) | (b << 16) | (g << 8) | r;

            msg.SetInt("duration", duration);
            msg.SetInt("hold_time", holdTime);

            msg.SetInt("flags", flags);
            msg.SetInt("color", packageColor);

            msg.Send(player);
        }

        public static void ChangePlayerScale(CCSPlayerController? player, float scale)
        {
            if (player == null || !player.IsValid) return;
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || !playerPawn.IsValid || playerPawn.CBodyComponent == null || playerPawn.CBodyComponent.SceneNode == null) return;
            var skeleton = playerPawn.CBodyComponent.SceneNode.GetSkeletonInstance();
            if (scale <= 0 || skeleton == null) return;

            skeleton.Scale = scale;
            playerPawn.AcceptInput("SetScale", null, null, scale.ToString(CultureInfo.InvariantCulture));

            Server.NextWorldUpdate(() =>
            {
                if (playerPawn == null || !playerPawn.IsValid) return;
                Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_CBodyComponent");
            });
        }

        public static Vector? GetSpawnPointVector(CCSPlayerController player, bool enemySpawn = false)
        {
            if (player == null || player.Team is CsTeam.None or CsTeam.Spectator) return null;

            CsTeam targetTeam = enemySpawn
                ? (player.Team == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist)
                : player.Team;

            string spawnPointName = targetTeam == CsTeam.CounterTerrorist
                ? "info_player_counterterrorist"
                : "info_player_terrorist";

            var spawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>(spawnPointName).Where(s => s.IsValid && s.Enabled).ToList();
            if (spawns.Count != 0)
            {
                var randomSpawn = spawns[jRandomSkills.Instance.Random.Next(spawns.Count)];
                if (randomSpawn != null && randomSpawn.IsValid && randomSpawn.AbsOrigin != null)
                    return new Vector(randomSpawn.AbsOrigin.X, randomSpawn.AbsOrigin.Y, randomSpawn.AbsOrigin.Z);
            }
            return null;
        }

        public static bool IsBulletDamage(CTakeDamageInfo? info)
        {
            var ability = info?.Ability?.Value;
            if (ability == null || !ability.IsValid) return false;

            return FiresBullets(ability.DesignerName);
        }

        public static HitGroup_t GetHitGroup(CTakeDamageInfo? info)
        {
            if (info == null || info.Handle == nint.Zero) return HitGroup_t.HITGROUP_GENERIC;
            if (!IsBulletDamage(info)) return HitGroup_t.HITGROUP_GENERIC;

            int offset = GameData.GetOffset("CTakeDamageInfo_HitGroup");
            if (offset <= 0) return HitGroup_t.HITGROUP_GENERIC;

            nint hitGroupPointer = Marshal.ReadIntPtr(info.Handle, offset);
            if (hitGroupPointer == nint.Zero) return HitGroup_t.HITGROUP_GENERIC;

            nint hitGroupData = Marshal.ReadIntPtr(hitGroupPointer, 16);
            if (hitGroupData == nint.Zero) return HitGroup_t.HITGROUP_GENERIC;

            return (HitGroup_t)Marshal.ReadInt32(hitGroupData, 56);
        }

        private const float DefaultHeadshotMultiplier = 4f;
        private const float StomachMultiplier = 1.25f;
        private const float LegMultiplier = 0.75f;

        public static float GetAppliedDamageScale(CTakeDamageInfo? info, CCSPlayerPawn? victimPawn)
        {
            if (info == null || victimPawn == null || !victimPawn.IsValid) return 1f;

            var hitGroup = GetHitGroup(info);
            var vdata = GetWeaponVData(info);

            float scale = hitGroup switch
            {
                HitGroup_t.HITGROUP_HEAD => vdata != null && vdata.HeadshotMultiplier > 0 ? vdata.HeadshotMultiplier : DefaultHeadshotMultiplier,
                HitGroup_t.HITGROUP_STOMACH => StomachMultiplier,
                HitGroup_t.HITGROUP_LEFTLEG or HitGroup_t.HITGROUP_RIGHTLEG => LegMultiplier,
                _ => 1f,
            };

            if (vdata == null || vdata.ArmorRatio <= 0 || vdata.ArmorRatio >= 1f) return scale;
            if (victimPawn.ArmorValue <= 0) return scale;
            if (!ArmorCovers(hitGroup, victimPawn)) return scale;

            return scale * vdata.ArmorRatio;
        }

        public static float PredictAppliedDamage(CTakeDamageInfo? info, CCSPlayerPawn? victimPawn)
        {
            if (info == null) return 0f;
            return info.Damage * GetAppliedDamageScale(info, victimPawn);
        }

        public static bool IsPredictedLethal(CTakeDamageInfo? info, CCSPlayerPawn? victimPawn)
        {
            if (victimPawn == null || !victimPawn.IsValid) return false;
            return PredictAppliedDamage(info, victimPawn) >= victimPawn.Health;
        }

        private static bool ArmorCovers(HitGroup_t hitGroup, CCSPlayerPawn victimPawn) => hitGroup switch
        {
            HitGroup_t.HITGROUP_LEFTLEG or HitGroup_t.HITGROUP_RIGHTLEG => false,
            HitGroup_t.HITGROUP_HEAD => victimPawn.ItemServices?.As<CCSPlayer_ItemServices>()?.HasHelmet ?? false,
            _ => true,
        };

        private static CCSWeaponBaseVData? GetWeaponVData(CTakeDamageInfo info)
        {
            var ability = info.Ability?.Value;
            if (ability == null || !ability.IsValid) return null;

            var weapon = ability.As<CCSWeaponBase>();
            if (weapon == null || !weapon.IsValid) return null;

            return weapon.GetVData<CCSWeaponBaseVData>();
        }

        public static void CreateHEGrenadeProjectile(Vector pos, QAngle angle, Vector vel, int teamNum)
        {
            HEGrenadeProjectile_CreateFunc.Value?.Invoke(pos.Handle, angle.Handle, vel.Handle, vel.Handle, IntPtr.Zero, 44, teamNum);
        }

        public static void CreateSmokeGrenadeProjectile(Vector pos, QAngle angle, Vector vel, int teamNum)
        {
            SmokeGrenadeProjectile_CreateFunc.Value?.Invoke(pos.Handle, angle.Handle, vel.Handle, vel.Handle, IntPtr.Zero, 45, teamNum);
        }

        public static void CreateMolotovProjectile(Vector pos, QAngle angle, Vector vel, int teamNum)
        {
            CMolotovProjectile_CreateFunc.Value?.Invoke(pos.Handle, angle.Handle, vel.Handle, vel.Handle, IntPtr.Zero, 46, teamNum);
        }

        // True when this hit would be nullified by the server's friendly-fire rules (same-team hit,
        // mp_friendlyfire 0 and mp_teammates_are_enemies 0). The TakeDamage pre-hook still sees the raw
        // damage, so lethal victim-side skills (SecondLife/Phoenix) must skip it — otherwise they "revive"
        // a teammate that was never going to take damage.
        public static bool IsFriendlyFireBlocked(CTakeDamageInfo? info, CCSPlayerPawn? victimPawn)
        {
            if (info == null || victimPawn == null || !victimPawn.IsValid) return false;

            var attackerEnt = info.Attacker?.Value;
            if (attackerEnt == null || !attackerEnt.IsValid) return false;   // world/no attacker -> real damage
            if (attackerEnt.Handle == victimPawn.Handle) return false;       // self damage applies

            var attackerPawn = new CCSPlayerPawn(attackerEnt.Handle);
            if (!attackerPawn.IsValid || attackerPawn.DesignerName != "player") return false; // non-player inflictor
            if (attackerPawn.TeamNum != victimPawn.TeamNum) return false;    // enemy -> real damage

            bool ff = CvarValue("mp_friendlyfire", false);
            bool tae = CvarValue("mp_teammates_are_enemies", false);
            return !ff && !tae; // same team + FF off -> engine will zero this damage
        }

        public static bool IsFriendlyFireBlocked(Skills skill, CTakeDamageInfo? info, CCSPlayerPawn? victimPawn)
        {
            if (!IsSameTeamHit(info, victimPawn)) return false;
            if (!SkillsInfo.GetValue<bool>(skill, "friendlyFire")) return true;

            bool ff = CvarValue("mp_friendlyfire", false);
            bool tae = CvarValue("mp_teammates_are_enemies", false);
            return !ff && !tae;
        }

        public static bool IsSameTeamHit(CTakeDamageInfo? info, CCSPlayerPawn? victimPawn)
        {
            if (info == null || victimPawn == null || !victimPawn.IsValid) return false;

            var attackerEnt = info.Attacker?.Value;
            if (attackerEnt == null || !attackerEnt.IsValid) return false;
            if (attackerEnt.Handle == victimPawn.Handle) return false;

            var attackerPawn = new CCSPlayerPawn(attackerEnt.Handle);
            if (!attackerPawn.IsValid || attackerPawn.DesignerName != "player") return false;

            return attackerPawn.TeamNum == victimPawn.TeamNum;
        }

        public static float GetTeamDamageMultiplier(Skills skill)
        {
            float reduction = SkillsInfo.GetValue<float>(skill, "dmgReductionForTeamates");
            return 1f - Math.Clamp(reduction, 0f, 1f);
        }

        private static readonly ConcurrentDictionary<uint, (uint AttackerIndex, string? Weapon, int ExpiryTick)> pendingKillCredits = [];

        public static void RegisterKillCredit(uint victimIndex, uint attackerIndex, KillfeedIcons? killfeedIcon = null)
        {
            pendingKillCredits[victimIndex] = (attackerIndex, killfeedIcon == null ? null : KillfeedIconsExtensions.ToIcon((KillfeedIcons)killfeedIcon), Server.TickCount + 64);
        }

        public static bool TryConsumeKillCredit(uint victimIndex, out uint attackerIndex, out string? weapon)
        {
            attackerIndex = 0;
            weapon = null;
            if (!pendingKillCredits.TryRemove(victimIndex, out var credit)) return false;
            if (credit.ExpiryTick < Server.TickCount) return false;

            attackerIndex = credit.AttackerIndex;
            weapon = credit.Weapon;
            return true;
        }

        public static void ClearKillCredits()
        {
            pendingKillCredits.Clear();
        }

        private static readonly HashSet<string> bulletWeapons = new(StringComparer.Ordinal)
        {
            "deagle", "revolver", "glock", "usp_silencer", "cz75a",
            "fiveseven", "p250", "tec9", "elite", "hkp2000",
            "mp9", "mac10", "bizon", "mp7", "ump45", "p90", "mp5sd",
            "famas", "galilar", "m4a1", "m4a1_silencer", "ak47", "aug", "sg556",
            "ssg08", "awp", "scar20", "g3sg1",
            "nova", "xm1014", "mag7", "sawedoff",
            "m249", "negev"
        };

        public static bool FiresBullets(string? weapon)
        {
            if (string.IsNullOrEmpty(weapon)) return false;

            if (weapon.StartsWith("weapon_", StringComparison.Ordinal))
                weapon = weapon["weapon_".Length..];

            return bulletWeapons.Contains(weapon);
        }

        private static readonly HashSet<Skills> curseSkills =
        [
            Skills.Bankrupt, Skills.CarefulBullets, Skills.Darkness, Skills.Deactivator,
            Skills.Deaf, Skills.ExpensiveAmmo, Skills.Giant, Skills.Glitch,
            Skills.Jammer, Skills.JumpBan, Skills.JumpCurse, Skills.LifeSwap,
            Skills.Magnifier, Skills.MoneySwap, Skills.Nightmare, Skills.Poison,
            Skills.JetKick, Skills.PrimaryBan, Skills.Thief, Skills.WildThrow,
            Skills.Voodoo, Skills.Nemesis, Skills.Bounty
        ];

        private static readonly HashSet<string> curseSkillNames = new(curseSkills.Select(s => s.ToString()), StringComparer.Ordinal);

        private static readonly Dictionary<uint, int> curseCounts = [];
        private static readonly Dictionary<uint, uint> curserToVictim = [];
        private static readonly object curseLock = new();

        private static readonly Config.GameModes[] sharedSkillModes =
            [Config.GameModes.TeamSkills, Config.GameModes.SameSkills, Config.GameModes.Debug];

        public static bool CurseLimitEnabled
        {
            get
            {
                if (Config.LoadedConfig.CurseSkillPerPlayer is not int limit || limit <= 0) return false;
                return Array.IndexOf(sharedSkillModes, (Config.GameModes)Config.LoadedConfig.GameMode) < 0;
            }
        }

        public static bool IsCurseSkill(Skills skill) => curseSkills.Contains(skill);

        public static bool IsCurseSkill(string skill) => curseSkillNames.Contains(skill);

        public static void ClearCurses()
        {
            if (!CurseLimitEnabled) return;

            lock (curseLock)
            {
                curseCounts.Clear();
                curserToVictim.Clear();
            }
        }

        public static bool CanCurse(uint victimIndex)
        {
            if (!CurseLimitEnabled) return true;
            int limit = Config.LoadedConfig.CurseSkillPerPlayer!.Value;

            lock (curseLock)
                return !curseCounts.TryGetValue(victimIndex, out int used) || used < limit;
        }

        public static bool TryClaimCurse(uint curserIndex, uint victimIndex, bool force = false)
        {
            lock (curseLock)
            {
                ReleaseCurseLocked(curserIndex);

                curseCounts.TryGetValue(victimIndex, out int used);

                if (CurseLimitEnabled && !force && used >= Config.LoadedConfig.CurseSkillPerPlayer!.Value)
                    return false;

                curseCounts[victimIndex] = used + 1;
                curserToVictim[curserIndex] = victimIndex;
                return true;
            }
        }

        public static void ReleaseCurse(uint curserIndex)
        {
            lock (curseLock) ReleaseCurseLocked(curserIndex);
        }

        public static uint[] GetCursersOf(uint victimIndex)
        {
            lock (curseLock)
                return [.. curserToVictim.Where(kvp => kvp.Value == victimIndex).Select(kvp => kvp.Key)];
        }

        public static void ClearCursesFor(uint playerIndex)
        {
            lock (curseLock)
            {
                ReleaseCurseLocked(playerIndex);
                curseCounts.Remove(playerIndex);

                foreach (var curser in curserToVictim.Where(kvp => kvp.Value == playerIndex).Select(kvp => kvp.Key).ToList())
                    curserToVictim.Remove(curser);
            }
        }

        private static void ReleaseCurseLocked(uint curserIndex)
        {
            if (!curserToVictim.Remove(curserIndex, out uint victimIndex)) return;
            if (!curseCounts.TryGetValue(victimIndex, out int used)) return;

            if (used <= 1) curseCounts.Remove(victimIndex);
            else curseCounts[victimIndex] = used - 1;
        }

        private const uint crosshairBit = 1u << 8;

        private static readonly object hudSuppressionLock = new();
        private static readonly Dictionary<uint, HashSet<string>> crosshairOwners = [];
        private static readonly Dictionary<uint, HashSet<string>> radarOwners = [];

        public static void SetCrosshairHidden(CCSPlayerController? player, string owner, bool hide)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return;

            if (!TryUpdateOwners(crosshairOwners, player.Index, owner, hide, out bool suppressed)) return;

            if (suppressed) pawn.HideHUD |= crosshairBit;
            else pawn.HideHUD &= ~crosshairBit;

            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_iHideHUD");
        }

        public static void SetRadarDisabled(CCSPlayerController? player, string owner, bool disable)
        {
            if (player == null || !player.IsValid) return;
            if (!TryUpdateOwners(radarOwners, player.Index, owner, disable, out bool suppressed)) return;

            player.ReplicateConVar("sv_disable_radar", suppressed ? "1" : "0");
        }

        public static void ClearHudSuppression(uint playerIndex)
        {
            lock (hudSuppressionLock)
            {
                crosshairOwners.Remove(playerIndex);
                radarOwners.Remove(playerIndex);
            }
        }

        public static void ClearAllHudSuppression()
        {
            lock (hudSuppressionLock)
            {
                crosshairOwners.Clear();
                radarOwners.Clear();
            }
        }

        private static bool TryUpdateOwners(Dictionary<uint, HashSet<string>> registry, uint playerIndex, string owner, bool claim, out bool suppressed)
        {
            lock (hudSuppressionLock)
            {
                if (!registry.TryGetValue(playerIndex, out var owners))
                {
                    owners = [];
                    registry[playerIndex] = owners;
                }

                bool before = owners.Count > 0;
                bool changed = claim ? owners.Add(owner) : owners.Remove(owner);

                suppressed = owners.Count > 0;
                if (!suppressed) registry.Remove(playerIndex);

                return changed && before != suppressed;
            }
        }

        public static CCSPlayerController[] GetSelectableEnemies(CCSPlayerController player, bool respectCurseLimit = false)
        {
            if (player == null || !player.IsValid) return [];

            var enemies = GetAliveEnemies(player);
            if (!respectCurseLimit || !CurseLimitEnabled || enemies.Length == 0) return enemies;

            var withCapacity = enemies.Where(p => CanCurse(p.Index)).ToArray();
            return withCapacity.Length > 0 ? withCapacity : enemies;
        }

        public static bool AnyCurseCapacity(CCSPlayerController player)
        {
            if (!CurseLimitEnabled) return true;
            if (player == null || !player.IsValid) return true;

            return GetAliveEnemies(player).Any(p => CanCurse(p.Index));
        }

        private static CCSPlayerController[] GetAliveEnemies(CCSPlayerController player)
        {
            return [.. PlayerManager.GetTickPlayers()
                .Where(p => p != null && p.IsValid)
                .Select(PlayerManager.GetPlayerEvent)
                .Where(p => p != null && p.IsValid && p.Team != player.Team
                    && p.PlayerPawn?.Value != null && p.PlayerPawn.Value.IsValid && p.PlayerPawn.Value.Health > 0
                    && !p.IsHLTV && p.Team != CsTeam.Spectator && p.Team != CsTeam.None)
                .Cast<CCSPlayerController>()];
        }

        private static readonly ConcurrentDictionary<uint, int> healthBeforeHit = [];

        public static void TrackHealthBeforeHit(CBaseEntity? damagedEntity, CTakeDamageInfo? damageInfo)
        {
            if (damagedEntity == null || damageInfo == null || damageInfo.Damage <= 0) return;

            CCSPlayerPawn victimPawn = new(damagedEntity.Handle);
            if (!victimPawn.IsValid || victimPawn.DesignerName != "player") return;

            var victimController = victimPawn.Controller?.Value;
            if (victimController == null || !victimController.IsValid) return;

            var victim = PlayerManager.GetPlayerEvent(victimController.As<CCSPlayerController>());
            if (victim == null || !victim.IsValid) return;

            healthBeforeHit[victim.Index] = victimPawn.Health;
        }

        public static int CapToVictimHealth(CCSPlayerController? victim, int dmgHealth)
        {
            if (victim == null || !victim.IsValid) return dmgHealth;
            if (!healthBeforeHit.TryGetValue(victim.Index, out int healthBefore) || healthBefore <= 0) return dmgHealth;

            return dmgHealth > healthBefore ? healthBefore : dmgHealth;
        }

        public static void ClearHealthBeforeHit() => healthBeforeHit.Clear();

        public static bool TakeHealth(CCSPlayerPawn? pawn, int damage, CCSPlayerController? damageAttacker = null, KillfeedIcons? killfeedIcon = null)
        {
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                return false;

            CCSPlayerController? victim = null;
            jSkill_PlayerInfo? playerInfo = null;

            var player = pawn.Controller.Value;
            if (player != null && player.IsValid)
            {
                victim = player.As<CCSPlayerController>();
                playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo == null) return false;

                if (playerInfo.Skill == Skills.Jester && Jester.GetJesterInfo(player.Index)?.Active == true)
                    return false;

                if (playerInfo.Skill == Skills.GodMode && GodMode.HaveHodMode(player.Index))
                    return false;

                if (playerInfo.Skill == Skills.Armored)
                    damage = (int)Math.Round(damage * (playerInfo.SkillChance ?? 1f));
            }

            int newHealth = (int)(pawn.Health - damage);
            if (newHealth <= 0 && playerInfo != null)
            {
                if (playerInfo.Skill == Skills.SecondLife && SecondLife.TryConsumeRevive(victim, pawn))
                    return true;
                if (playerInfo.Skill == Skills.Phoenix && Phoenix.TryConsumeRevive(victim, pawn))
                    return true;
                if (playerInfo.Skill == Skills.ReZombie && ReZombie.TryBecomeZombie(victim, pawn))
                    return true;
            }

            pawn.Health = newHealth;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

            if (pawn.Health <= 0)
            {
                if (damageAttacker != null && damageAttacker.IsValid && victim != null && victim.IsValid && damageAttacker.Index != victim.Index)
                    RegisterKillCredit(victim.Index, damageAttacker.Index, killfeedIcon);

                Server.NextFrame(() =>
                {
                    if (pawn == null || !pawn.IsValid) return;
                    pawn?.CommitSuicide(false, true);
                });
                return false;
            }

            return true;
        }

        public readonly record struct HiddenPawn(uint Index, CsTeam Team, CCSPlayerPawn Pawn, bool HoldsBomb, uint[] CarriedIndexes);

        public static List<HiddenPawn> ResolveHiddenPawns(ICollection<uint> playerIndexes, uint? bombOwnerIndex)
        {
            List<HiddenPawn> hidden = new(playerIndexes.Count);

            foreach (var playerIndex in playerIndexes)
            {
                var controller = PlayerManager.GetPlayerEvent(Utilities.GetPlayerFromIndex((int)playerIndex));
                if (controller == null || !controller.IsValid) continue;

                var pawn = controller.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;

                hidden.Add(new HiddenPawn(controller.Index, controller.Team, pawn, bombOwnerIndex == controller.Index, ResolveCarriedIndexes(pawn)));
            }

            return hidden;
        }

        private static uint[] ResolveCarriedIndexes(CCSPlayerPawn pawn)
        {
            var weaponServices = pawn.WeaponServices;
            if (weaponServices == null) return [];

            List<uint> indexes = [];

            var activeWeapon = weaponServices.ActiveWeapon?.Value;
            if (activeWeapon != null && activeWeapon.IsValid)
                indexes.Add(activeWeapon.Index);

            if (weaponServices.MyWeapons != null)
                foreach (var handle in weaponServices.MyWeapons)
                {
                    var weapon = handle?.Value;
                    if (weapon == null || !weapon.IsValid) continue;
                    if (indexes.Contains(weapon.Index)) continue;

                    indexes.Add(weapon.Index);
                }

            return [.. indexes];
        }

        public static void HideCarriedEntities(CCheckTransmitInfo info, in HiddenPawn target)
        {
            foreach (var index in target.CarriedIndexes)
                if (info.TransmitEntities.Contains(index))
                    info.TransmitEntities.Remove(index);
        }

        public static void ResetPrintHTML(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return;
            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;
            playerInfo.PrintHTML = null;
        }

        public static CTriggerMultiple? CreateTrigger(string name, float radius, Vector pos, uint ownerPlayerIndex = EntityManager.SystemOwnerIndex)
        {
            return EntityManager.CreateTrackedTrigger(ownerPlayerIndex, name, radius, pos);
        }

        public static void ForceFullUpdate(CCSPlayerController player, List<(uint PlayerIndex, QAngle LastAngle)>? batchList = null, INetworkGameServer? networkGameServer = null)
        {
            if (!Config.LoadedConfig.EnableFullForceUpdate) return;
            if (player == null || !player.IsValid || player.IsBot) return;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            QAngle lastAngle = new(pawn.V_angle.X, pawn.V_angle.Y, pawn.V_angle.Z);

            networkGameServer ??= new INetworkServerService().GetIGameServer();

            var client = networkGameServer.GetClientBySlot(player.Slot);
            if (client == null) return;

            client.ForceFullUpdate();
            // Only skip the angle restore when the captured view is a spawn-time (0,0,0) placeholder;
            // a genuine angle with a single zero component (e.g. yaw exactly 0) must still be restored.
            if (lastAngle.X == 0 && lastAngle.Y == 0 && lastAngle.Z == 0) return;

            uint playerIndex = player.Index;

            if (batchList != null)
            {
                batchList.Add((playerIndex, lastAngle));
                return;
            }

            jRandomSkills.Instance.AddTickTimer(3, () =>
            {
                var target = Utilities.GetPlayerFromIndex((int)playerIndex);
                if (target == null || !target.IsValid) return;

                var targetPawn = target.PlayerPawn?.Value;
                if (targetPawn == null || !targetPawn.IsValid || targetPawn.AbsOrigin == null) return;

                targetPawn.Look(lastAngle);
            });
        }

        private static int lastForceFullUpdateAll = int.MinValue;

        public static void ForceFullUpdateToAll()
        {
            if (!Config.LoadedConfig.EnableFullForceUpdate) return;

            int tickCount = Server.TickCount;
            if (tickCount == lastForceFullUpdateAll) return;

            lastForceFullUpdateAll = tickCount;
            var playersToRestore = new List<(uint PlayerIndex, QAngle LastAngle)>();

            INetworkGameServer networkGameServer = new INetworkServerService().GetIGameServer();
            foreach (var player in Utilities.GetPlayers())
                ForceFullUpdate(player, playersToRestore, networkGameServer);

            if (playersToRestore.Count <= 0) return;

            jRandomSkills.Instance.AddTickTimer(3, () =>
            {
                foreach (var item in playersToRestore)
                {
                    var target = Utilities.GetPlayerFromIndex((int)item.PlayerIndex);
                    if (target == null || !target.IsValid) continue;

                    var targetPawn = target.PlayerPawn?.Value;
                    if (targetPawn == null || !targetPawn.IsValid || targetPawn.AbsOrigin == null) continue;

                    targetPawn.Look(item.LastAngle);
                }
            });
        }

        public static void ForceFullUpdateToAllChunked(int clientsPerFrame = 2)
        {
            if (!Config.LoadedConfig.EnableFullForceUpdate) return;

            var pending = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot).Select(p => p.Index).ToList();
            if (pending.Count == 0) return;

            ForceFullUpdateChunk(pending, 0, Math.Max(clientsPerFrame, 1));
        }

        private static void ForceFullUpdateChunk(List<uint> pending, int start, int clientsPerFrame)
        {
            if (start >= pending.Count) return;

            int end = Math.Min(start + clientsPerFrame, pending.Count);
            var toRestore = new List<(uint PlayerIndex, QAngle LastAngle)>();

            INetworkGameServer networkGameServer = new INetworkServerService().GetIGameServer();
            for (int i = start; i < end; i++)
            {
                var player = Utilities.GetPlayerFromIndex((int)pending[i]);
                if (player == null || !player.IsValid) continue;

                ForceFullUpdate(player, toRestore, networkGameServer);
            }

            if (toRestore.Count > 0)
                jRandomSkills.Instance.AddTickTimer(3, () =>
                {
                    foreach (var item in toRestore)
                    {
                        var target = Utilities.GetPlayerFromIndex((int)item.PlayerIndex);
                        if (target == null || !target.IsValid) continue;

                        var targetPawn = target.PlayerPawn?.Value;
                        if (targetPawn == null || !targetPawn.IsValid || targetPawn.AbsOrigin == null) continue;

                        targetPawn.Look(item.LastAngle);
                    }
                });

            Server.NextFrame(() => ForceFullUpdateChunk(pending, end, clientsPerFrame));
        }

        public static bool SetHealth(CCSPlayerPawn? pawn, int newHealth, int? maxHealth = null)
        {
            if (pawn == null || !pawn.IsValid)
                return false;

            maxHealth ??= pawn.MaxHealth;

            if (pawn.Health == maxHealth)
                return false;

            pawn.Health = Math.Min(newHealth, (int)maxHealth);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

            pawn.MaxHealth = (int)maxHealth;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            return true;
        }

        public static bool AddHealth(CCSPlayerPawn? pawn, int extraHealth, int? maxHealth = null)
        {
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || pawn.Health <= 0)
                return false;

            maxHealth ??= pawn.MaxHealth;

            if (pawn.Health == maxHealth)
                return false;

            int newHealth = (int)(pawn.Health + extraHealth);
            pawn.Health = Math.Min(newHealth, (int)maxHealth);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

            pawn.MaxHealth = (int)maxHealth;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");

            return true;
        }

        public static void RestoreHealth(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid || player.PlayerPawn == null)
                return;

            CBasePlayerPawn? pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                return;

            var p = PlayerManager.GetPlayerFromEvent(player);
            if (p == null || !p.IsValid)
                return;

            pawn.Health = (int)p.PawnHealth;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        public static void SetPlayerInvisibility(CCSPlayerController player, float percentInvisibility)
        {
            if (player == null || !player.IsValid || player.PlayerPawn == null)
                return;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn != null)
            {
                var color = Color.FromArgb(Math.Max(255 - (int)(255 * percentInvisibility), 0), 255, 255, 255);
                playerPawn.Render = color;
                Utilities.SetStateChanged(playerPawn, "CBaseModelEntity", "m_clrRender");
            }
        }

        public static string GetDesignerName(CBasePlayerWeapon? weapon)
        {
            if (weapon == null || !weapon.IsValid) return string.Empty;
            string designerName = weapon.DesignerName;
            ushort index = weapon.AttributeManager.Item.ItemDefinitionIndex;

            designerName = (designerName, index) switch
            {
                var (name, _) when name.Contains("bayonet") => "weapon_knife",
                ("weapon_m4a1", 60) => "weapon_m4a1_silencer",
                ("weapon_hkp2000", 61) => "weapon_usp_silencer",
                ("weapon_deagle", 64) => "weapon_revolver",
                ("weapon_mp7", 23) => "weapon_mp5sd",
                _ => designerName
            };

            return designerName;
        }

        private static IWasdMenuManager? GetMenuManager()
        {
            if (jRandomSkills.Instance.MenuManager == null)
                jRandomSkills.Instance.MenuManager = new WasdManager();

            ApplyMenuVisibleItems();
            return jRandomSkills.Instance.MenuManager;
        }

        private static void ApplyMenuVisibleItems()
        {
            int visibleItems = Config.LoadedConfig.HtmlHudCustomisation.WSADMenuVisibleItems;
            WASDMenuAPI.WasdMenuPlayer.DefaultVisibleOptions = visibleItems < 1 ? 3 : Math.Min(visibleItems, 10);
        }

        public static void CloseMenu(CCSPlayerController? player)
        {
            var manager = GetMenuManager();
            if (manager == null) return;
            manager.CloseMenu(player);
        }

        public static bool HasMenu(CCSPlayerController? player)
        {
            var manager = GetMenuManager();
            if (manager == null) return false;
            return manager.HasMenu(player);
        }

        public static bool SetMenuPaused(CCSPlayerController? player, bool pause)
        {
            var manager = GetMenuManager();
            if (manager == null) return false;
            return manager.SetMenuPaused(player, pause);
        }

        private static string GetInvisibleSignature(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var sb = new System.Text.StringBuilder();

            foreach (char c in id)
                for (int i = 0; i < 8; i++)
                    sb.Append(((c >> i) & 1) == 1 ? "\u200B" : "\u200C");

            return sb.ToString();
        }

        public static void UpdateMenu(CCSPlayerController? player, ConcurrentBag<(string, string)> items)
        {
            if (player == null) return;

            var manager = GetMenuManager();
            if (manager == null) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null) return;

            bool isIlliterate = Illiterate.CheckIlliterateSkill(player);

            Dictionary<string, Action<CCSPlayerController, IWasdMenuOption>> list = [];
            foreach (var item in items)
            {
                string encodedText = isIlliterate
                    ? System.Net.WebUtility.HtmlEncode(Illiterate.GetRandomText(item.Item1)!)
                    : System.Net.WebUtility.HtmlEncode(item.Item1);

                string uniqueKey = GetInvisibleSignature(item.Item2) + $"\u202A{encodedText}\u202C";

                list.TryAdd(uniqueKey, (p, option) =>
                {
                    jRandomSkills.Instance.SkillAction(playerInfo.Skill.ToString(), "TypeSkill", [p, new[] { item.Item2 }]);
                    manager.CloseMenu(p);
                });
            }

            manager.UpdateActiveMenu(player, list);
        }

        public static void CreateMenu(CCSPlayerController? player, ConcurrentBag<(string, string)> enemies, (string, string, bool)? lastElement = null)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo == null || playerInfo.HideHUD >= Server.TickCount) return;

            if (player.IsBot)
            {
                var pool = new List<string>();

                foreach (var enemy in enemies)
                    if (!string.IsNullOrEmpty(enemy.Item2))
                        pool.Add(enemy.Item2);

                if (lastElement != null && !string.IsNullOrEmpty(lastElement.Value.Item2))
                    pool.Add(lastElement.Value.Item2);

                if (pool.Count > 0)
                {
                    string randomTarget = pool[Random.Shared.Next(pool.Count)];
                    jRandomSkills.Instance.SkillAction(playerInfo.Skill.ToString(), "TypeSkill", [player, new[] { randomTarget }]);
                }

                return;
            }

            var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == playerInfo.Skill);
            if (skillData == null) return;

            var manager = GetMenuManager();
            if (manager == null) return;

            var config = Config.LoadedConfig.HtmlHudCustomisation;
            var your_skill = player.GetTranslationWithoutIlliterate("your_skill");
            var emptySymbol = $"<font class='fontSize-{(string.IsNullOrEmpty(your_skill) ? "l" : "ml")}'> </font>";

            string infoLine = string.IsNullOrEmpty(your_skill) || string.IsNullOrEmpty(config.HeaderLineSize)
                ? ""
                : $"<font class='fontWeight-Bold fontSize-{config.HeaderLineSize}' color='{config.HeaderLineColor}'>\u202A{your_skill}:\u202C</font><br>";

            string skillLine = Illiterate.CheckIlliterateSkill(player)
                ? $"<font class='fontWeight-Bold fontSize-{config.SkillLineSize}'>\u202A{Illiterate.GetRandomText(player.GetSkillName(skillData.Skill))}\u202C</font><br>"
                : $"<font class='fontWeight-Bold fontSize-{config.SkillLineSize}' color='{skillData.Color}'>\u202A{player.GetSkillName(skillData.Skill)}\u202C</font><br>";

            var skill_select_info = player.GetTranslation($"{playerInfo.Skill.ToString().ToLowerInvariant()}_select_info");
            string remainingLine = string.IsNullOrWhiteSpace(skill_select_info) || string.IsNullOrEmpty(config.WSADMenuSelectInfoLineSize)
                ? ""
                : $"<font class='fontSize-{config.WSADMenuSelectInfoLineSize}' color='{config.WSADMenuSelectInfoLineColor}'>{skill_select_info}</font><br>";

            var hudContent = infoLine + skillLine + remainingLine;

            string controllsLine = string.IsNullOrEmpty(config.WSADMenuControllsLineSize) ? "" :
                $"{emptySymbol}<font class='fontSize-{config.WSADMenuControllsLineSize}' color='{config.WSADMenuControllsLineColor1}'>{player.GetTranslationWithoutIlliterate($"menu_controlls_scroll")}</font>"
                + $"<font class='fontSize-{config.WSADMenuControllsLineSize}' color='{config.WSADMenuControllsLineColor2}'>{player.GetTranslationWithoutIlliterate($"menu_controlls_padding")}</font>"
                + $"<font class='fontSize-{config.WSADMenuControllsLineSize}' color='{config.WSADMenuControllsLineColor3}'>{player.GetTranslationWithoutIlliterate($"menu_controlls_select")}</font>{emptySymbol}<br>";

            string itemText = $"<font class='fontSize-{config.WSADMenuItemLineSize}' color='{config.WSADMenuItemLineColor}'>{{0}}</font><br>";
            string itemHoverText = $"<font class='fontSize-{config.WSADMenuItemLineSize}'><font color='purple'>[ </font><font color='{config.WSADMenuItemHoverLineColor}'>{{0}}</font><font color='purple'> ]</font></font><br>";

            bool isIlliterate = Illiterate.CheckIlliterateSkill(player);

            IWasdMenu menu = manager.CreateMenu(hudContent, itemText, itemHoverText, controllsLine);
            foreach (var enemy in enemies)
            {
                string encodedEnemyName = isIlliterate
                    ? System.Net.WebUtility.HtmlEncode(Illiterate.GetRandomText(enemy.Item1)!)
                    : System.Net.WebUtility.HtmlEncode(enemy.Item1);

                string uniqueKey = GetInvisibleSignature(enemy.Item2) + $"\u202A{encodedEnemyName}\u202C";

                menu.Add(uniqueKey, (p, option) =>
                {
                    jRandomSkills.Instance.SkillAction(playerInfo.Skill.ToString(), "TypeSkill", [p, new[] { enemy.Item2 }]);
                    manager.CloseMenu(p);
                });
            }

            if (lastElement != null)
            {
                string lastText = lastElement.Value.Item1;
                string lastColor = string.Empty;

                if (lastText.Length > 8 && lastText[0] == '#' && lastText[7] == '|')
                {
                    lastColor = lastText[..8];
                    lastText = lastText[8..];
                }

                string encodedLastElement = isIlliterate
                    ? System.Net.WebUtility.HtmlEncode(Illiterate.GetRandomText(lastText)!)
                    : System.Net.WebUtility.HtmlEncode(lastText);

                menu.Add($"{lastColor}\u202A{encodedLastElement}\u202C", (p, option) =>
                {
                    jRandomSkills.Instance.SkillAction(playerInfo.Skill.ToString(), "TypeSkill", [p, new[] { lastElement.Value.Item2 }]);
                    if (lastElement.Value.Item3)
                        manager.CloseMenu(p);
                });
            }

            manager.OpenMainMenu(player, menu);
        }

        public static void ToogleDoor(CBaseEntity entity, CBasePlayerPawn pawn)
        {
            if (pawn == null || !pawn.IsValid) return;
            if (entity == null || !entity.IsValid) return;

            if (!entity.DesignerName.StartsWith("prop_door_rotating", StringComparison.Ordinal)) return;

            var door = new CPropDoorRotating(entity.Handle);
            if (door == null || !door.IsValid) return;

            if (door.DoorState == DoorState_t.DOOR_STATE_CLOSED || door.DoorState == DoorState_t.DOOR_STATE_CLOSING)
                door.AcceptInput("use", pawn, door, "!activator");

            else if (door.DoorState == DoorState_t.DOOR_STATE_OPEN || door.DoorState == DoorState_t.DOOR_STATE_OPENING)
                door.AcceptInput("close");
        }

        public static void SetTeamScores(short ctScore, short tScore, RoundEndReason roundEndReason)
        {
            if (jRandomSkills.Instance == null || jRandomSkills.Instance.GameRules == null) return;
            UpdateServerTeamScores(ctScore, tScore);
            TerminateRoundFunc.Value?.Invoke(jRandomSkills.Instance.GameRules.Handle, 5f, roundEndReason, 0, 0);
        }

        public static void TerminateRound(CsTeam winnerTeam, CCSPlayerController? bonusPlayer = null)
        {
            if (jRandomSkills.Instance == null || jRandomSkills.Instance.GameRules == null) return;
            var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            var ctTeam = teams.FirstOrDefault(t => t.IsValid && (CsTeam)t.TeamNum == CsTeam.CounterTerrorist);
            var tTeams = teams.FirstOrDefault(t => t.IsValid && (CsTeam)t.TeamNum == CsTeam.Terrorist);
            if (ctTeam == null || tTeams == null) return;

            short ctScore = (short)(winnerTeam == CsTeam.CounterTerrorist ? ctTeam.Score + 1 : ctTeam.Score);
            short tScore = (short)(winnerTeam == CsTeam.Terrorist ? tTeams.Score + 1 : tTeams.Score);

            UpdateServerTeamScores(ctScore, tScore);
            jRandomSkills.Instance.GameRules?.TerminateRound(5f, winnerTeam == CsTeam.CounterTerrorist ? RoundEndReason.BombDefused : RoundEndReason.TargetBombed);

            AwardRoundEndMoney(winnerTeam, bonusPlayer);
        }

        private static void AwardRoundEndMoney(CsTeam winnerTeam, CCSPlayerController? bonusPlayer = null)
        {
            int winnerReward = winnerTeam == CsTeam.CounterTerrorist
                ? CvarValue("cash_team_win_by_defusing_bomb", 3500)
                : CvarValue("cash_team_terrorist_win_bomb", 3500);

            if (winnerReward <= 0) return;

            int maxMoney = CvarValue("mp_maxmoney", 16000);
            int personalBonus = bonusPlayer == null
                ? 0
                : winnerTeam == CsTeam.CounterTerrorist
                    ? CvarValue("cash_player_defused_bomb", 300)
                    : CvarValue("cash_player_bomb_planted", 300);

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;
                if (player.Team != winnerTeam) continue;

                var moneyServices = player.InGameMoneyServices;
                if (moneyServices == null) continue;

                int reward = winnerReward;
                if (bonusPlayer != null && bonusPlayer.IsValid && player.Index == bonusPlayer.Index)
                    reward += personalBonus;

                moneyServices.Account = Math.Clamp(moneyServices.Account + reward, 0, maxMoney);
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
            }
        }

        private static void UpdateServerTeamScores(short ctScore, short tScore)
        {
            if (jRandomSkills.Instance == null || jRandomSkills.Instance.GameRules == null) return;
            int totalRoundsPlayed = ctScore + tScore;
            int maxRounds = CvarValue("mp_maxrounds", 24);
            int halfRounds = maxRounds / 2;
            int overtimeMaxRounds = CvarValue("mp_overtime_maxrounds", 6);
            int overtimeLimit = CvarValue("mp_overtime_limit", 1);

            var gameRulesProxy = jRandomSkills.Instance.GameRules;
            gameRulesProxy.TotalRoundsPlayed = totalRoundsPlayed;
            gameRulesProxy.ITotalRoundsPlayed = totalRoundsPlayed;
            gameRulesProxy.RoundsPlayedThisPhase = totalRoundsPlayed;

            gameRulesProxy.TeamIntroPeriod = false;
            if (gameRulesProxy.GamePhase == 1 && totalRoundsPlayed < halfRounds)
            {
                gameRulesProxy.GamePhase = 0;
                gameRulesProxy.SwapTeamsOnRestart = true;
                gameRulesProxy.SwitchingTeamsAtRoundReset = true;
                gameRulesProxy.RoundsPlayedThisPhase = 0;
                gameRulesProxy.TeamIntroPeriod = true;
            }

            if (totalRoundsPlayed < halfRounds)
                gameRulesProxy.GamePhase = 0;
            else if (gameRulesProxy.GamePhase == 0)
            {
                gameRulesProxy.GamePhase = 1;
                gameRulesProxy.SwapTeamsOnRestart = true;
                gameRulesProxy.SwitchingTeamsAtRoundReset = true;
                gameRulesProxy.RoundsPlayedThisPhase = 0;
                gameRulesProxy.TeamIntroPeriod = true;
            }

            var structOffset = jRandomSkills.Instance.GameRules.Handle + Schema.GetSchemaOffset("CCSGameRules", "m_bMapHasBombZone") + 0x02;
            var matchStruct = Marshal.PtrToStructure<MCCSMatch>(structOffset);

            matchStruct.m_totalScore = (short)totalRoundsPlayed;
            matchStruct.m_actualRoundsPlayed = (short)totalRoundsPlayed;
            gameRulesProxy.MatchInfoDecidedTime = Server.CurrentTime;

            matchStruct.m_ctScoreTotal = ctScore;
            gameRulesProxy.AccountCT = ctScore;
            matchStruct.m_terroristScoreTotal = tScore;
            gameRulesProxy.AccountTerrorist = tScore;

            if (gameRulesProxy.GamePhase == 0)
            {
                matchStruct.m_ctScoreFirstHalf = ctScore;
                matchStruct.m_terroristScoreFirstHalf = tScore;
            }
            else
            {
                matchStruct.m_ctScoreSecondHalf = ctScore;
                matchStruct.m_terroristScoreSecondHalf = tScore;
            }

            if (totalRoundsPlayed >= maxRounds)
            {
                if (gameRulesProxy.OvertimePlaying == 0)
                {
                    gameRulesProxy.OvertimePlaying = 1;
                    gameRulesProxy.SwapTeamsOnRestart = true;
                    gameRulesProxy.SwitchingTeamsAtRoundReset = true;
                }
                else
                {
                    int roundsInOvertime = totalRoundsPlayed - maxRounds;
                    if (roundsInOvertime % overtimeMaxRounds == 0)
                    {
                        int currentOvertime = roundsInOvertime / overtimeMaxRounds;
                        if (currentOvertime < overtimeLimit)
                        {
                            gameRulesProxy.SwapTeamsOnRestart = true;
                            gameRulesProxy.SwitchingTeamsAtRoundReset = true;
                        }
                    }
                }
            }
            gameRulesProxy.OvertimePlaying = 0;

            Marshal.StructureToPtr(matchStruct, structOffset, true);
            UpdateClientTeamScores(matchStruct);
        }

        private static void UpdateClientTeamScores(MCCSMatch match)
        {
            var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            var ctTeam = teams.FirstOrDefault(t => t.IsValid && (CsTeam)t.TeamNum == CsTeam.CounterTerrorist);
            var tTeams = teams.FirstOrDefault(t => t.IsValid && (CsTeam)t.TeamNum == CsTeam.Terrorist);

            if (ctTeam != null && tTeams != null)
            {
                ctTeam.Score = match.m_ctScoreTotal;
                ctTeam.ScoreFirstHalf = match.m_ctScoreFirstHalf;
                ctTeam.ScoreSecondHalf = match.m_ctScoreSecondHalf;
                ctTeam.ScoreOvertime = match.m_ctScoreOvertime;
                Utilities.SetStateChanged(ctTeam, "CTeam", "m_iScore");
                Utilities.SetStateChanged(ctTeam, "CCSTeam", "m_scoreFirstHalf");
                Utilities.SetStateChanged(ctTeam, "CCSTeam", "m_scoreSecondHalf");
                Utilities.SetStateChanged(ctTeam, "CCSTeam", "m_scoreOvertime");

                tTeams.Score = match.m_terroristScoreTotal;
                tTeams.ScoreFirstHalf = match.m_terroristScoreFirstHalf;
                tTeams.ScoreSecondHalf = match.m_terroristScoreSecondHalf;
                tTeams.ScoreOvertime = match.m_terroristScoreOvertime;
                Utilities.SetStateChanged(tTeams, "CTeam", "m_iScore");
                Utilities.SetStateChanged(tTeams, "CCSTeam", "m_scoreFirstHalf");
                Utilities.SetStateChanged(tTeams, "CCSTeam", "m_scoreSecondHalf");
                Utilities.SetStateChanged(tTeams, "CCSTeam", "m_scoreOvertime");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MCCSMatch
    {
        public short m_totalScore;
        public short m_actualRoundsPlayed;
        public short m_nOvertimePlaying;
        public short m_ctScoreFirstHalf;
        public short m_ctScoreSecondHalf;
        public short m_ctScoreOvertime;
        public short m_ctScoreTotal;
        public short m_terroristScoreFirstHalf;
        public short m_terroristScoreSecondHalf;
        public short m_terroristScoreOvertime;
        public short m_terroristScoreTotal;
        public short unknown;
        public int m_phase;
    }
}