using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using src.player.skills;
using src.utils;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using static CounterStrikeSharp.API.Core.Listeners;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player
{
    public static partial class Event
    {
        private static Timer? setSkillTimer = null;
        private const int MaxSetSkillRetries = 6;
        private static int setSkillRetries;
        private static int gameRulesPolls;
        private static DateTime freezeTimeEnd = DateTime.MinValue;
        private static bool isTransmitRegistered = false;
        public static readonly jSkill_SkillInfo noneSkill = new(Skills.None, SkillsInfo.GetValue<string>(Skills.None, "color"), false);

        private static jSkill_SkillInfo ctSkill = noneSkill;
        private static jSkill_SkillInfo tSkill = noneSkill;
        private static jSkill_SkillInfo allSkill = noneSkill;
        private static List<jSkill_SkillInfo> debugSkills = [.. SkillData.Skills];

        public static readonly SkillsInfo.DefaultSkillInfo[] terroristSkills = [.. SkillsInfo.LoadedConfig.Where(s => s.OnlyTeam == (int)CsTeam.Terrorist)];
        public static readonly SkillsInfo.DefaultSkillInfo[] counterterroristSkills = [.. SkillsInfo.LoadedConfig.Where(s => s.OnlyTeam == (int)CsTeam.CounterTerrorist)];
        private static readonly SkillsInfo.DefaultSkillInfo[] allTeamsSkills = [.. SkillsInfo.LoadedConfig.Where(s => s.OnlyTeam == 0)];

        private static readonly ConcurrentDictionary<uint, HashSet<Skills>> playersSkills = [];
        public static readonly ConcurrentDictionary<uint, jSkill_SkillInfo> staticSkills = [];
        public static readonly ConcurrentDictionary<uint, uint> playersCustomFOV = [];
        private static readonly object setLock = new();

        public static void Load()
        {
            Instance.RegisterEventHandler<EventPlayerConnectFull>(PlayerConnectFull);
            Instance.RegisterEventHandler<EventPlayerDisconnect>(PlayerDisconnect);
            // Instance.RegisterEventHandler<EventPlayerChat>(PlayerChat);
            Instance.RegisterEventHandler<EventPlayerSpawned>(PlayerSpawned);
            Instance.RegisterEventHandler<EventRoundStart>(RoundStart);
            Instance.RegisterEventHandler<EventRoundEnd>(RoundEnd);

            Instance.RegisterEventHandler<EventPlayerDeath>(PlayerDeathPre, HookMode.Pre);
            Instance.RegisterEventHandler<EventPlayerDeath>(PlayerDeath);
            Instance.RegisterEventHandler<EventPlayerBlind>(PlayerBlind);
            Instance.RegisterEventHandler<EventPlayerHurt>(PlayerHurtPre, HookMode.Pre);
            Instance.RegisterEventHandler<EventPlayerHurt>(PlayerHurt);
            Instance.RegisterEventHandler<EventPlayerJump>(PlayerJump);
            Instance.RegisterEventHandler<EventBotTakeover>(BotTakeover);

            Instance.RegisterEventHandler<EventWeaponFire>(WeaponFire);
            Instance.RegisterEventHandler<EventItemEquip>(WeaponEquip);
            Instance.RegisterEventHandler<EventItemPickup>(WeaponPickup);
            Instance.RegisterEventHandler<EventWeaponReload>(WeaponReload);
            Instance.RegisterEventHandler<EventGrenadeThrown>(GrenadeThrown);

            Instance.RegisterEventHandler<EventBombBeginplant>(BombBeginplant);
            Instance.RegisterEventHandler<EventBombAbortplant>(BombAbortplant);
            Instance.RegisterEventHandler<EventBombPlanted>(BombPlanted);
            Instance.RegisterEventHandler<EventBombBegindefuse>(BombBegindefuse);

            Instance.RegisterEventHandler<EventDecoyStarted>(DecoyStarted);
            Instance.RegisterEventHandler<EventDecoyDetonate>(DecoyDetonate);

            Instance.RegisterEventHandler<EventSmokegrenadeDetonate>(SmokegrenadeDetonate);
            Instance.RegisterEventHandler<EventSmokegrenadeExpired>(SmokegrenadeExpired);

            Instance.RegisterListener<OnPlayerButtonsChanged>(CheckUseSkill);
            Instance.RegisterListener<OnEntitySpawned>(EntitySpawned);
            Instance.RegisterListener<OnTick>(OnTick);
            Instance.RegisterListener<OnClientPutInServer>(OnPlayerConnectedBot);

            Instance.HookUserMessage(208, PlayerMakeSound);
            Instance.HookUserMessage(207, GetPrintToCenterHtml);

            Instance.RegisterListener<OnEntityTakeDamagePre>(OnEntityTakeDamagePre);
            Instance.RegisterListener<OnEntityTakeDamagePost>(OnEntityTakeDamagePost);

            PerfLog.ContextProvider = PerfContext;

            Instance.RegisterEventHandler<EventBulletImpact>(BulletImpact);

            // These come from CounterStrikeSharp's own gamedata. After a CS2 update they can fail with
            // "Invalid function pointer" until CounterStrikeSharp is updated; the plugin keeps loading and
            // only the skills that need the missing hook are left out of the draw.
            startTouchHooked = TryHook("CBaseTrigger_StartTouchFunc", () => VirtualFunctions.CBaseTrigger_StartTouchFunc.Hook(OnTriggerEnter, HookMode.Post));
            endTouchHooked = TryHook("CBaseTrigger_EndTouchFunc", () => VirtualFunctions.CBaseTrigger_EndTouchFunc.Hook(OnTriggerExit, HookMode.Pre));
            canAcquireHooked = TryHook("CCSPlayer_ItemServices_CanAcquireFunc", () => VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Hook(OnWeaponCanAcquire, HookMode.Pre));

            var unavailable = Enum.GetValues<Skills>().Where(IsSkillMissingHooks).ToArray();
            if (unavailable.Length > 0)
                Instance.Logger.LogWarning("[TiredPowers] Some CounterStrikeSharp hooks are unavailable (update CounterStrikeSharp to match the current CS2 build). These skills are disabled until then: {Skills}", string.Join(", ", unavailable));

            // Disabled after CS2 updates started crashing Linux servers on player join.
            // The hooked native signature is only used to block weapon drops for Iana clones.
            // Keeping the plugin alive is safer than installing a stale global hook at load time.
        }

        public static void Unload()
        {
            TryUnhook(() => Instance.RemoveListener<OnEntityTakeDamagePre>(OnEntityTakeDamagePre));
            TryUnhook(() => Instance.RemoveListener<OnEntityTakeDamagePost>(OnEntityTakeDamagePost));
            if (startTouchHooked) TryUnhook(() => VirtualFunctions.CBaseTrigger_StartTouchFunc.Unhook(OnTriggerEnter, HookMode.Post));
            if (endTouchHooked) TryUnhook(() => VirtualFunctions.CBaseTrigger_EndTouchFunc.Unhook(OnTriggerExit, HookMode.Pre));
            if (canAcquireHooked) TryUnhook(() => VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Unhook(OnWeaponCanAcquire, HookMode.Pre));
            TryUnhook(() => Instance.UnhookUserMessage(208, PlayerMakeSound));
            TryUnhook(() => Instance.RemoveListener<CheckTransmit>(CheckTransmit));
            TryUnhook(NoRecoil.RestoreSpread);
        }

        private static bool startTouchHooked;
        private static bool endTouchHooked;
        private static bool canAcquireHooked;

        private static readonly Skills[] triggerHookSkills = [Skills.ThrowingKnife];
        private static readonly Skills[] canAcquireHookSkills = [Skills.Iana, Skills.ReZombie, Skills.ThrowingKnife];

        // True for skills whose native hook could not be installed on this server.
        public static bool IsSkillMissingHooks(Skills skill)
        {
            if ((!startTouchHooked || !endTouchHooked) && triggerHookSkills.Contains(skill)) return true;
            if (!canAcquireHooked && canAcquireHookSkills.Contains(skill)) return true;
            return false;
        }

        private static bool TryHook(string name, Action hook)
        {
            try
            {
                hook();
                return true;
            }
            catch (Exception ex)
            {
                Instance.Logger.LogError("[TiredPowers] Could not hook {Name}: {Message}", name, ex.Message);
                return false;
            }
        }

        private static void TryUnhook(Action unhook)
        {
            try { unhook(); }
            catch (Exception ex) { Server.PrintToConsole($"[TiredPowers] unhook failed: {ex.Message}"); }
        }

        private static readonly Skills[] lateDamageSkills = [Skills.SecondLife, Skills.Phoenix, Skills.ReZombie, Skills.SixthSense];

        private static readonly HashSet<Skills> tickFailuresLogged = [];

        private static void InvokeSkill(Skills skill, string methodName, object[] args)
        {
            try
            {
                Instance.SkillAction(skill.ToString(), methodName, args);
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[TiredPowers] {skill}.{methodName} failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private static void DispatchToActiveSkills(string methodName, params object[] args)
        {
            var seen = new HashSet<Skills>();
            foreach (var p in Instance.SkillPlayer)
            {
                if (p.IsDrawing) continue;
                foreach (var skill in p.AllSkills())
                    if (seen.Add(skill))
                        InvokeSkill(skill, methodName, args);
            }
        }

        // Turns a player's skill (and any extra skills) off and clears them; use instead of a bare DisableSkill.
        public static void DisableAllSkills(jSkill_PlayerInfo info, CCSPlayerController player)
        {
            Instance.SkillAction(info.Skill.ToString(), "DisableSkill", [player]);

            var extras = info.ExtraSkills;
            if (extras.Length == 0) return;
            info.ExtraSkills = [];
            foreach (var extra in extras)
                Instance.SkillAction(extra.ToString(), "DisableSkill", [player]);
        }

        public static void UseAllSkills(jSkill_PlayerInfo info, CCSPlayerController player)
        {
            Instance.SkillAction(info.Skill.ToString(), "UseSkill", [player]);
            foreach (var extra in info.ExtraSkills)
                Instance.SkillAction(extra.ToString(), "UseSkill", [player]);
        }

        private static void ReleaseCursesTargeting(uint victimIndex)
        {
            foreach (uint curserIndex in SkillUtils.GetCursersOf(victimIndex))
            {
                if (curserIndex == victimIndex) continue;

                var curser = Utilities.GetPlayerFromIndex((int)curserIndex);
                if (curser == null || !curser.IsValid) continue;

                if (PlayerManager.GetPlayerByIndex(curserIndex)?.Skill is not Skills curserSkill || curserSkill == Skills.None)
                    continue;

                try
                {
                    Instance.SkillAction(curserSkill.ToString(), "DisableSkill", [curser]);
                }
                catch (Exception ex)
                {
                    Server.PrintToConsole($"[TiredPowers] {curserSkill}.DisableSkill failed while releasing curse on leaving player: {(ex.InnerException ?? ex).Message}");
                }
            }
        }

        private static void DispatchCheckTransmit(object[] args)
        {
            var seen = new HashSet<Skills>();
            foreach (var p in Instance.SkillPlayer)
                foreach (var skill in p.AllSkills())
                    if (seen.Add(skill))
                        InvokeSkill(skill, "CheckTransmit", args);
        }

        private static void DispatchOnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo, object[] args, bool post = false)
        {
            var seen = new HashSet<Skills>();
            List<Skills>? deferred = null;

            foreach (var p in Instance.SkillPlayer)
            {
                if (p.IsDrawing) continue;

                foreach (var skill in p.AllSkills())
                {
                    if (!seen.Add(skill)) continue;

                    if (Array.IndexOf(lateDamageSkills, skill) >= 0)
                    {
                        (deferred ??= []).Add(skill);
                        continue;
                    }

                    InvokeOnTakeDamage(skill, damagedEntity, damageInfo, args, post);
                }
            }

            if (deferred == null) return;
            foreach (var skill in deferred)
                InvokeOnTakeDamage(skill, damagedEntity, damageInfo, args, post);
        }

        private static void InvokeOnTakeDamage(Skills skill, CBaseEntity damagedEntity, CTakeDamageInfo damageInfo, object[] args, bool post)
        {
            if (!Config.DebugEnabled(DebugCategory.Damage))
            {
                InvokeSkill(skill, post ? "OnTakeDamagePost" : "OnTakeDamage", args);
                return;
            }

            float before = damageInfo == null ? 0f : damageInfo.Damage;

            InvokeSkill(skill, post ? "OnTakeDamagePost" : "OnTakeDamage", args);

            float after = damageInfo == null ? 0f : damageInfo.Damage;
            if (Math.Abs(before - after) > 0.01f)
                Debug.WriteToDebug($"[DMG] {skill} changed damage {before:0.#} -> {after:0.#}{DescribeDamageTarget(damagedEntity)}", DebugCategory.Damage);
        }

        private static string DescribeDamageTarget(CBaseEntity damagedEntity)
        {
            try
            {
                if (damagedEntity == null || !damagedEntity.IsValid) return string.Empty;

                var pawn = damagedEntity.As<CCSPlayerPawn>();
                if (pawn == null || !pawn.IsValid || pawn.DesignerName != "player") return string.Empty;

                var controller = pawn.Controller.Value?.As<CCSPlayerController>();
                if (controller == null || !controller.IsValid) return string.Empty;

                uint routedIndex = PlayerManager.GetPlayerEvent(controller)?.Index ?? controller.Index;
                string routing = routedIndex == controller.Index
                    ? string.Empty
                    : $" routedIdx={routedIndex} routedSkill={PlayerManager.GetPlayerByIndex(routedIndex)?.Skill}";

                return $" on {controller.PlayerName} [idx={controller.Index} skill={PlayerManager.GetPlayerByIndex(controller.Index)?.Skill}{routing}]";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static HookResult PlayerMakeSound(UserMessage um)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("PlayerMakeSound", um);
                return HookResult.Continue;
            }
        }

        private static HookResult GetPrintToCenterHtml(UserMessage um)
        {
            if (!Config.LoadedConfig.HideHudForOtherPlugins) return HookResult.Continue;

            int tickCount = Server.TickCount;
            if (tickCount % 10 != 0) return HookResult.Continue;
            
            lock (setLock)
            {
                if (um.ReadUInt("eventid") != 226)
                    return HookResult.Continue;

                var debug = um.DebugString;
                var match = Regex.Match(debug, @"val_string:\s*""(.*?)""");

                if (match.Success)
                {
                    var html = match.Groups[1].Value.Replace("\\'", "'");
                    if (string.IsNullOrEmpty(html)) return HookResult.Continue;
                    
                    bool isOtherPlugin = !html.StartsWith("<jRS/>");

                    var player = um.Recipients.FirstOrDefault();
                    if (player == null || !player.IsValid) return HookResult.Continue;

                    var playerInfo = PlayerManager.GetPlayerByIndex(PlayerManager.GetPlayerEvent(player)?.Index ?? player.Index);
                    if (playerInfo == null) return HookResult.Continue;

                    if (isOtherPlugin && playerInfo.HideHUD != int.MaxValue)
                    {
                        playerInfo.HideHUD = tickCount + 15;
                        SkillUtils.SetMenuPaused(player, true);
                    }
                }

                return HookResult.Continue;
            }
        }

        private static HookResult WeaponFire(EventWeaponFire @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("WeaponFire", @event);
                return HookResult.Continue;
            }
        }

        private static bool IsEventAlive(GameEvent? @event) => @event != null && @event.Handle != nint.Zero;

        private static HookResult WeaponEquip(EventItemEquip @event, GameEventInfo info)
        {
            lock (setLock)
            {
                if (!IsEventAlive(@event)) return HookResult.Continue;

                DispatchToActiveSkills("WeaponEquip", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponPickup(EventItemPickup @event, GameEventInfo info)
        {
            lock (setLock)
            {
                if (!IsEventAlive(@event)) return HookResult.Continue;

                DispatchToActiveSkills("WeaponPickup", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult WeaponReload(EventWeaponReload @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("WeaponReload", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult GrenadeThrown(EventGrenadeThrown @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("GrenadeThrown", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerHurtPre(EventPlayerHurt @event, GameEventInfo info)
        {
            lock (setLock)
            {
                try
                {
                    if (@event.DmgHealth <= 0 && @event.DmgArmor <= 0) return HookResult.Continue;

                    var victim = PlayerManager.GetPlayerEvent(@event.Userid);
                    if (victim == null || !victim.IsValid) return HookResult.Continue;

                    var victimInfo = PlayerManager.GetPlayerByIndex(victim.Index);
                    if (victimInfo == null || victimInfo.IsDrawing) return HookResult.Continue;

                    bool suppressed = AskSkillSuppressesHit(victimInfo.Skill, @event);

                    if (!suppressed)
                    {
                        var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
                        if (attacker != null && attacker.IsValid && attacker.Index != victim.Index)
                        {
                            var attackerInfo = PlayerManager.GetPlayerByIndex(attacker.Index);
                            if (attackerInfo != null && !attackerInfo.IsDrawing && attackerInfo.Skill != victimInfo.Skill)
                                suppressed = AskSkillSuppressesHit(attackerInfo.Skill, @event);
                        }
                    }

                    if (!suppressed) return HookResult.Continue;

                    if (@event.DmgArmor > 0)
                    {
                        var pawn = victim.PlayerPawn?.Value;
                        if (pawn != null && pawn.IsValid)
                        {
                            pawn.ArmorValue += @event.DmgArmor;
                            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
                        }
                    }

                    @event.DmgHealth = 0;
                    @event.DmgArmor = 0;
                }
                catch (Exception ex)
                {
                    Debug.WriteToDebug($"PlayerHurtPre failed: {ex.Message}");
                }

                return HookResult.Continue;
            }
        }

        private static bool AskSkillSuppressesHit(Skills skill, EventPlayerHurt @event)
        {
            if (skill == Skills.None) return false;
            return (bool?)Instance.SkillAction(skill.ToString(), "PlayerHurtPre", [@event]) == true;
        }

        private static HookResult PlayerHurt(EventPlayerHurt @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("PlayerHurt", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerJump(EventPlayerJump @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("PlayerJump", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BotTakeover(EventBotTakeover @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("BotTakeover", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult PlayerBlind(EventPlayerBlind @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("PlayerBlind", @event);
                return HookResult.Continue;
            }
        }

        private static readonly Dictionary<Skills, string> _skillNames =
            Enum.GetValues<Skills>().ToDictionary(s => s, s => s.ToString());
        private static readonly HashSet<Skills> _activeSkillsSet = [];
        private static readonly List<Skills> _activeSkillsList = [];
        private static readonly Comparison<Skills> _tickOrderCmp = (a, b) => TickOrder(a).CompareTo(TickOrder(b));
        private static HashSet<Skills>? _freezeDisabledSkills;

        // AreaReaper and ChillOut depend on other skills' tick results, so they must tick last.
        private static int TickOrder(Skills s) => s == Skills.AreaReaper ? 2 : s == Skills.ChillOut ? 1 : 0;

        private static HashSet<Skills> BuildFreezeDisabledSkills()
        {
            var set = new HashSet<Skills>();
            foreach (var s in SkillData.Skills)
                if (SkillsInfo.GetValue<bool>(s.Skill, "disableOnFreezeTime"))
                    set.Add(s.Skill);
            return set;
        }

        public static void InvalidateFreezeDisabledCache() => _freezeDisabledSkills = null;

        private const double stallThresholdMs = 100.0;
        private static long lastTickTimestamp;

        private static DateTime lastStallConsole = DateTime.MinValue;

        // Always on: a gap of 150 ms or more between two ticks is printed to the console (at most once per 5 s)
        // together with the plugin's own share of the previous tick, so a stall can be attributed or ruled out.
        private static void ReportStall()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            long previous = lastTickTimestamp;
            lastTickTimestamp = now;

            if (previous == 0) return;

            double gapMs = (now - previous) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (gapMs < stallThresholdMs || PlayerManager.IsServerIdle()) return;

            var (tracked, owners) = EntityManager.GetStatistics();
            string line = $"STALL gap={gapMs:F0}ms tick={Server.TickCount} plugin-last-tick={lastPluginTickMs:F1}ms ({lastSlowest}) tracked={tracked} owners={owners}{PerfContext()}";
            PerfLog.Info(line);

            if (gapMs >= 150 && (DateTime.Now - lastStallConsole).TotalSeconds >= 5)
            {
                lastStallConsole = DateTime.Now;
                Server.PrintToConsole($"[TiredPowers] {line}");
            }
        }

        private static double lastPluginTickMs;
        private static string lastSlowest = "";
        private static DateTime lastSlowConsole = DateTime.MinValue;

        public static string PerfContext()
        {
            try
            {
                int players = 0, alive = 0, bots = 0;
                foreach (var player in PlayerManager.GetTickPlayers())
                {
                    if (player == null || !player.IsValid || player.IsHLTV) continue;
                    if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) continue;

                    players++;
                    if (player.IsBot) bots++;
                    if (player.PawnIsAlive) alive++;
                }

                var gameRules = jRandomSkills.Instance?.GameRules;
                int round = gameRules == null || gameRules.Handle == IntPtr.Zero ? -1 : gameRules.TotalRoundsPlayed;

                return $" players={players} alive={alive} bots={bots} skills={_activeSkillsList.Count} round={round}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void OnTick()
        {
            ReportStall();
            NoRecoil.RestoreSpread();
            ScreenText.OnTick();

            long perfStart = PerfLog.Start();
            long perfStart0 = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (setLock)
            {
                _activeSkillsSet.Clear();
                _activeSkillsList.Clear();
                foreach (var p in Instance.SkillPlayer)
                {
                    if (p.IsDrawing) continue;
                    foreach (var skill in p.AllSkills())
                        if (_activeSkillsSet.Add(skill))
                            _activeSkillsList.Add(skill);
                }

                _activeSkillsList.Sort(_tickOrderCmp);

                bool freeze = SkillUtils.IsFreezeTime();
                _freezeDisabledSkills ??= BuildFreezeDisabledSkills();

                Skills slowestSkill = Skills.None;
                double slowestMs = 0;

                foreach (var skill in _activeSkillsList)
                {
                    if (freeze && _freezeDisabledSkills.Contains(skill)) continue;
                    long skillStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    try
                    {
                        Instance.SkillAction(_skillNames[skill], "OnTick");
                    }
                    catch (Exception ex)
                    {
                        // Without this one throwing skill cancels every later skill's tick, every frame.
                        // Logged once per skill per round; at 64 ticks a repeat would flood the console.
                        if (tickFailuresLogged.Add(skill))
                            Server.PrintToConsole($"[TiredPowers] {skill}.OnTick failed: {ex.InnerException?.Message ?? ex.Message}");
                    }
                    double skillMs = (System.Diagnostics.Stopwatch.GetTimestamp() - skillStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    if (skillMs > slowestMs) { slowestMs = skillMs; slowestSkill = skill; }
                }

                double totalMs = (System.Diagnostics.Stopwatch.GetTimestamp() - perfStart0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                lastPluginTickMs = totalMs;
                lastSlowest = slowestSkill == Skills.None ? "-" : $"{slowestSkill} {slowestMs:F1}ms";

                // The plugin's own tick work took long: name the skill. At most one line per 5 s.
                if (totalMs >= 40 && (DateTime.Now - lastSlowConsole).TotalSeconds >= 5)
                {
                    lastSlowConsole = DateTime.Now;
                    Server.PrintToConsole($"[TiredPowers] SLOW TICK plugin={totalMs:F1}ms slowest={lastSlowest}{PerfContext()}");
                }
            }
            PerfLog.Sample("OnTick(skills)", perfStart);
            ReportLaunches();
        }

        // A vertical speed rise well above the server's jump impulse in one tick is never the player's own
        // movement; one console line names the skills active at that moment plus the server's jump cvars.
        private static readonly Dictionary<uint, (float Vz, bool OnGround, DateTime LastLog)> launchTrack = [];
        // Horizontal speed in the air: the game adds at most sv_airaccelerate * 30 / 64 (about 6 u/s) per tick,
        // so a bigger gain while airborne is an outside push.
        private static readonly Dictionary<uint, (float Speed2D, int Boosts, float Gained, DateTime LastLog, float Vx, float Vy)> airTrack = [];
        // After a reported boost: where the pawn was and what speed was reported, to measure the real travel.
        private static readonly Dictionary<uint, (int Tick, float X, float Y, float Reported)> airCheck = [];

        private static void ReportLaunches()
        {
            try
            {
                float impulse = SkillUtils.CvarValue("sv_jump_impulse", 301.99f);
                foreach (var player in PlayerManager.GetTickPlayers())
                {
                    if (player == null || !player.IsValid || player.IsBot) continue;
                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || pawn.AbsVelocity == null) continue;

                    float vz = pawn.AbsVelocity.Z;
                    bool onGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
                    launchTrack.TryGetValue(player.Index, out var prev);

                    // Rise above what one jump can give (a landing + jump in the same tick starts from 0, not from
                    // the fall speed, so the fall part is not counted).
                    float from = prev.OnGround || prev.Vz < 0 ? Math.Max(prev.Vz, 0f) : prev.Vz;
                    float rise = vz - from;
                    if (rise > impulse + 40f && (DateTime.Now - prev.LastLog).TotalSeconds >= 3)
                    {
                        prev.LastLog = DateTime.Now;
                        var info = PlayerManager.GetPlayerByIndex(player.Index);
                        string own = info == null ? "-" : string.Join("+", info.AllSkills().Where(k => k != Skills.None));
                        string active = string.Join(",", _activeSkillsList.Where(k => k != Skills.None));
                        int humans = PlayerManager.GetTickPlayers().Count(p => p != null && p.IsValid && !p.IsBot);
                        float speed2D = MathF.Sqrt(pawn.AbsVelocity.X * pawn.AbsVelocity.X + pawn.AbsVelocity.Y * pawn.AbsVelocity.Y);
                        bool jump = (player.Buttons & PlayerButtons.Jump) != 0;
                        string cvars = $"sv_jump_impulse={impulse:F0} sv_enablebunnyhopping={SkillUtils.CvarString("sv_enablebunnyhopping", "?")} sv_autobunnyhopping={SkillUtils.CvarString("sv_autobunnyhopping", "?")} sv_staminajumpcost={SkillUtils.CvarString("sv_staminajumpcost", "?")} sv_gravity={SkillUtils.CvarString("sv_gravity", "?")}";
                        Server.PrintToConsole($"[TiredPowers] LAUNCH {player.PlayerName}: vz {prev.Vz:F0} -> {vz:F0} (rise {rise:F0}, more than one jump) wasOnGround={prev.OnGround} jumpHeld={jump} speed2D={speed2D:F0} gravity={pawn.ActualGravityScale:F2} velMod={pawn.VelocityModifier:F2} moveType={pawn.MoveType} | own skills: {own} | skills active this round: {active} | {humans} humans | {cvars}");
                    }

                    launchTrack[player.Index] = (vz, onGround, prev.LastLog);

                    float horizontal = MathF.Sqrt(pawn.AbsVelocity.X * pawn.AbsVelocity.X + pawn.AbsVelocity.Y * pawn.AbsVelocity.Y);
                    airTrack.TryGetValue(player.Index, out var air);
                    if (!onGround && !prev.OnGround && pawn.MoveType == MoveType_t.MOVETYPE_WALK)
                    {
                        float gain = horizontal - air.Speed2D;
                        if (gain > 40f)
                        {
                            air.Boosts++;
                            air.Gained += gain;
                            if ((DateTime.Now - air.LastLog).TotalSeconds >= 3)
                            {
                                air.LastLog = DateTime.Now;
                                var info = PlayerManager.GetPlayerByIndex(player.Index);
                                string own = info == null ? "-" : string.Join("+", info.AllSkills().Where(k => k != Skills.None));
                                string active = string.Join(",", _activeSkillsList.Where(k => k != Skills.None));
                                int humans = PlayerManager.GetTickPlayers().Count(p => p != null && p.IsValid && !p.IsBot);
                                // Where the push came from: its direction against the view, and what was close.
                                float dvx = pawn.AbsVelocity.X - air.Vx, dvy = pawn.AbsVelocity.Y - air.Vy;
                                float pushYaw = MathF.Atan2(dvy, dvx) * 180f / MathF.PI;
                                float relYaw = pushYaw - pawn.V_angle.Y;
                                while (relYaw > 180) relYaw -= 360; while (relYaw < -180) relYaw += 360;
                                string nearest = "-";
                                if (pawn.AbsOrigin != null)
                                {
                                    float best = float.MaxValue; string who = "";
                                    foreach (var other in PlayerManager.GetTickPlayers())
                                    {
                                        if (other == null || !other.IsValid || other.Index == player.Index) continue;
                                        var op = other.PlayerPawn.Value;
                                        if (op == null || !op.IsValid || op.AbsOrigin == null || op.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;
                                        float d = (op.AbsOrigin - pawn.AbsOrigin).Length();
                                        if (d < best) { best = d; who = other.PlayerName; }
                                    }
                                    if (who.Length > 0) nearest = $"{who} at {best:F0}u";
                                }
                                var ground = pawn.GroundEntity?.Value;
                                string touching = ground != null && ground.IsValid ? ground.DesignerName : "nothing";
                                string pos = pawn.AbsOrigin == null ? "?" : $"{pawn.AbsOrigin.X:F0} {pawn.AbsOrigin.Y:F0} {pawn.AbsOrigin.Z:F0}";
                                // m_vecVelocity is what the movement code holds; m_vecAbsVelocity is derived. A gap between
                                // the two means the reported number is an artifact, not a real speed.
                                float moveSpeed = pawn.Velocity == null ? -1 : MathF.Sqrt(pawn.Velocity.X * pawn.Velocity.X + pawn.Velocity.Y * pawn.Velocity.Y);
                                Server.PrintToConsole($"[TiredPowers] AIRBOOST {player.PlayerName}: abs speed {air.Speed2D:F0} -> {horizontal:F0} (+{gain:F0} in one tick while airborne; {air.Boosts} such ticks, +{air.Gained:F0} total since last line) movement velocity now {moveSpeed:F0} push dir {relYaw:F0} deg from view (0=forward, 180=from behind) vz={vz:F0} velMod={pawn.VelocityModifier:F2} gravity={pawn.ActualGravityScale:F2} touching={touching} nearest={nearest} pos={pos} map={Server.MapName} | own skills: {own} | skills active this round: {active} | {humans} humans");
                                air.Boosts = 0; air.Gained = 0;
                                if (pawn.AbsOrigin != null)
                                    airCheck[player.Index] = (Server.TickCount, pawn.AbsOrigin.X, pawn.AbsOrigin.Y, horizontal);
                            }
                        }
                    }
                    air.Speed2D = horizontal;
                    air.Vx = pawn.AbsVelocity.X; air.Vy = pawn.AbsVelocity.Y;
                    airTrack[player.Index] = air;

                    // Eight ticks after a reported boost: how far did the pawn really move?
                    if (airCheck.TryGetValue(player.Index, out var chk) && Server.TickCount - chk.Tick >= 8 && pawn.AbsOrigin != null)
                    {
                        airCheck.Remove(player.Index);
                        float dx = pawn.AbsOrigin.X - chk.X, dy = pawn.AbsOrigin.Y - chk.Y;
                        float moved = MathF.Sqrt(dx * dx + dy * dy);
                        float real = moved / ((Server.TickCount - chk.Tick) / 64f);
                        Server.PrintToConsole($"[TiredPowers] AIRBOOST CHECK {player.PlayerName}: moved {moved:F0}u in {Server.TickCount - chk.Tick} ticks = {real:F0} u/s real travel (reported {chk.Reported:F0} u/s at the boost, abs now {horizontal:F0}) => {(real > chk.Reported * 0.8f ? "REAL boost" : "reporting artifact, no real boost")}");
                    }
                }
            }
            catch { }
        }

        private static void OnPlayerConnectedBot(int playerSlot)
        {
            lock (setLock)
            {
                var player = Utilities.GetPlayerFromSlot(playerSlot);
                if (player == null || !player.IsValid) return;

                if (player.IsBot && !Config.LoadedConfig.EnableBotSkills)
                    return;

                var existing = Instance.SkillPlayer.FirstOrDefault(p => p.PlayerIndex == player.Index);
                if (existing != null)
                {
                    PlayerManager.Register(existing);
                    return;
                }

                var playerInfo = new jSkill_PlayerInfo
                {
                    IsBot = player.IsBot,
                    PlayerName = player.PlayerName,
                    PlayerIndex = player.Index,
                    Skill = Skills.None,
                    SpecialSkill = Skills.None,
                    IsDrawing = false,
                    SkillChance = 1,
                    PrintHTML = null,
                    HideHUD = int.MinValue,
                    SkillUsed = false,
                };

                UpdateSkillHudExpired(playerInfo, Skills.None);

                PlayerManager.Register(playerInfo);
            }
        }

        private static HookResult PlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            // Sign-on work runs while the client's net channel is most fragile; anything slow here is reported.
            long connectStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                return PlayerConnectFullCore(@event);
            }
            finally
            {
                double ms = System.Diagnostics.Stopwatch.GetElapsedTime(connectStart).TotalMilliseconds;
                if (ms >= 20)
                    Server.PrintToConsole($"[TiredPowers] SLOW CONNECT: the plugin's connect handler took {ms:0}ms for {@event.Userid?.PlayerName ?? "?"} (should be <5ms).");
            }
        }

        private static HookResult PlayerConnectFullCore(EventPlayerConnectFull @event)
        {
            lock (setLock)
            {
                var player = PlayerManager.GetPlayerEvent(@event.Userid);
                if (player == null || !player.IsValid) return HookResult.Continue;

                Localization.PreResolveLanguage(player);

                string welcomeMsg = player.GetTranslationWithoutIlliterate("welcome_message", "welcome");
                foreach (string line in welcomeMsg.Split("\n"))
                    player.PrintToChat($" {ChatColors.Green}" + line.Replace("{PLAYER}", $" {ChatColors.Red}\u202A{player.PlayerName}\u202C{ChatColors.Green}", StringComparison.OrdinalIgnoreCase)
                                            .Replace("{SERVER_NAME}", $" {ChatColors.Red}{SkillUtils.CvarString("hostname", "Default Server")}{ChatColors.Green}", StringComparison.OrdinalIgnoreCase)
                                            .Replace("{VERSION}", $" {ChatColors.Red}v{Instance.ModuleVersion}{ChatColors.Green}", StringComparison.OrdinalIgnoreCase)
                                            .Replace("{SKILLS_COUNT}", $" {ChatColors.Red}{SkillData.Skills.Count - 1}{ChatColors.Green}", StringComparison.OrdinalIgnoreCase));

                ServerInfo.SendTo(player);

                // Retakes off: put the player on the smaller team instead of leaving them in the team menu
                // (with retakes on, its queue handles joining).
                if (Config.LoadedConfig.AutoAssignTeamOnJoin && !Instance.IsRetakesActive && !player.IsBot)
                {
                    Instance.AddTimer(1.0f, () =>
                    {
                        if (player == null || !player.IsValid || player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist) return;

                        var players = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsHLTV).ToList();
                        int ts = players.Count(p => p.Team == CsTeam.Terrorist);
                        int cts = players.Count(p => p.Team == CsTeam.CounterTerrorist);
                        player.ChangeTeam(ts < cts ? CsTeam.Terrorist : cts < ts ? CsTeam.CounterTerrorist : (Instance.Random.Next(2) == 0 ? CsTeam.Terrorist : CsTeam.CounterTerrorist));
                    }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                }

                return HookResult.Continue;
            }
        }

        private static HookResult PlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            lock (setLock)
            {
                var player = PlayerManager.GetPlayerEvent(@event.Userid);
                if (player == null || !player.IsValid) return HookResult.Continue;

                var skillPlayer = PlayerManager.GetPlayerByIndex(player!.Index);
                if (skillPlayer == null) return HookResult.Continue;

                LogDisconnect(@event, player, skillPlayer);

                DisableAllSkills(skillPlayer, player);

                uint leavingIndex = player.Index;

                ReleaseCursesTargeting(leavingIndex);

                foreach (var skill in SkillData.Skills)
                    Instance.SkillAction(skill.Skill.ToString(), "PlayerDisconnect", [leavingIndex]);

                SkillUtils.ClearCursesFor(leavingIndex);

                ScreenText.Remove(player.Index);
                PlayerManager.UnregisterPlayer(player.Index);
                EntityManager.DestroyPlayerEntities(player.Index);
                SkillUtils.ClearHudSuppression(player.Index);

                return HookResult.Continue;
            }
        }

        // One console line per human disconnect with what the plugin was pushing to that client: the centre-HTML
        // HUD is the only thing it sends on a schedule, so a net overflow kick can be read against it.
        private static void LogDisconnect(EventPlayerDisconnect @event, CCSPlayerController player, jSkill_PlayerInfo skillPlayer)
        {
            try
            {
                if (player.IsBot) return;

                int reason = @event.Reason;
                string reasonName = reason switch
                {
                    1 => "shutdown", 2 => "by user", 3 => "kicked", 4 => "lost", 5 => "OVERFLOW",
                    6 => "steam logon", 7 => "steam auth", 23 => "timed out", 39 => "net overflow", _ => "code"
                };

                var cache = skillPlayer.HudCache;
                int sends = (cache?.WindowSends ?? 0) + (cache?.PrevWindowSends ?? 0);
                long bytes = (cache?.WindowBytes ?? 0) + (cache?.PrevWindowBytes ?? 0);
                double seconds = cache == null ? 0 : Math.Clamp((Server.TickCount - cache.WindowStartTick) / 64.0 + (cache.PrevWindowSends > 0 ? 10 : 0), 0.1, 20);
                bool image = cache?.Content?.Contains("<img", StringComparison.Ordinal) == true;

                var rules = Instance?.GameRules;
                string phase = rules == null ? "?" : rules.WarmupPeriod ? "warmup" : rules.FreezePeriod ? "freeze" : rules.GamePhase >= 5 ? "match end" : "live";
                double sinceFreezeEnd = (DateTime.Now - freezeTimeEnd).TotalSeconds;
                int humans = Utilities.GetPlayers().Count(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV);

                string line = $"[TiredPowers] DISCONNECT {player.PlayerName} reason={reason} ({reasonName}) | HUD to this client: {sends} msgs, {bytes / 1024.0:F1} KB in last {seconds:F0} s ({sends / seconds:F1}/s), last msg {cache?.LastBytes ?? 0} B, image={image}, resend every {Config.LoadedConfig.HtmlHudCustomisation.HudResendTicks} ticks | {phase}, {sinceFreezeEnd:F0} s after freeze end, {humans} humans, skill={skillPlayer.Skill}";
                Server.PrintToConsole(line);
                PerfLog.Info(line);
            }
            catch { }
        }

        private static HookResult PlayerSpawned(EventPlayerSpawned @event, GameEventInfo info)
        {
            lock (setLock)
            {
                var player = PlayerManager.GetPlayerEvent(@event.Userid);
                if (player == null || !player.IsValid) return HookResult.Continue;

                var skillPlayer = PlayerManager.GetPlayerByIndex(player!.Index);
                if (skillPlayer == null) return HookResult.Continue;

                if (setSkillTimer != null)
                {
                    skillPlayer.IsDrawing = true;
                    return HookResult.Continue;
                }

                skillPlayer.IsDrawing = false;
                if (Instance?.GameRules != null &&
                    Instance?.GameRules.WarmupPeriod == false &&
                    skillPlayer.Skill == Skills.None &&
                    skillPlayer.SpecialSkill == Skills.None)
                    SetRandomSkill(player);
                return HookResult.Continue;
            }
        }

        public static void RestorePlayer(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid) return;

            SkillUtils.ClearHudSuppression(player.Index);

            pawn.HideHUD = (uint)(pawn.HideHUD & ~(1 << 8));
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_iHideHUD");

            player.ReplicateConVar("sv_disable_radar", "0");
            // Gravity back to normal: a lowered scale (Astronaut, Gravity Decoy) that outlived its skill made every
            // jump go further for the rest of the map.
            if (pawn.ActualGravityScale != 1f)
                pawn.ActualGravityScale = 1f;

            if (SkillsInfo.GetValue<bool>(Skills.Magnifier, "active") && player.DesiredFOV == SkillsInfo.GetValue<uint>(Skills.Magnifier, "customFOV"))
            {
                player.DesiredFOV = playersCustomFOV.TryGetValue(player.Index, out uint fov) ? fov : 0;
                Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
            }
        }

        private static HookResult PlayerDeathPre(EventPlayerDeath @event, GameEventInfo info)
        {
            try
            {
                var victim = @event.Userid;
                if (victim == null || !victim.IsValid) return HookResult.Continue;

                Ninja.RevealOnDeath(victim.Index);
                Ghost.RevealOnDeath(victim.Index);
                C4Camouflage.RevealOnDeath(victim.Index);

                if (!SkillUtils.TryConsumeKillCredit(victim.Index, out uint attackerIndex, out string? weapon))
                    return HookResult.Continue;

                var attacker = Utilities.GetPlayerFromIndex((int)attackerIndex);
                if (attacker == null || !attacker.IsValid || attacker.Index == victim.Index)
                    return HookResult.Continue;

                @event.Attacker = attacker;

                if (!string.IsNullOrEmpty(weapon))
                    @event.Weapon = weapon;

                var matchStats = attacker.ActionTrackingServices?.MatchStats;
                if (matchStats != null)
                {
                    matchStats.Kills++;
                    Utilities.SetStateChanged(attacker, "CCSPlayerController", "m_pActionTrackingServices");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteToDebug($"PlayerDeathPre kill credit failed: {ex.Message}");
            }

            return HookResult.Continue;
        }

        private static HookResult PlayerDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            long perfStart = PerfLog.Start();
            var result = PlayerDeathCore(@event, info);
            PerfLog.End("PlayerDeath total", perfStart, 2.0);
            return result;
        }

        private static HookResult PlayerDeathCore(EventPlayerDeath @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("PlayerDeath", @event);

                var victim = PlayerManager.GetPlayerEvent(@event.Userid);
                if (victim == null) return HookResult.Continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(victim.Index);
                if (playerInfo == null || playerInfo.IsDrawing) return HookResult.Continue;
                DisableAllSkills(playerInfo, victim);

                var attacker = PlayerManager.GetPlayerEvent(@event.Attacker);
                if (attacker == null || victim == attacker) return HookResult.Continue;

                if (victim == attacker) return HookResult.Continue;
                if (Config.LoadedConfig.KillerSkillChatInfo)
                {
                    var attackerInfo = PlayerManager.GetPlayerByIndex(attacker!.Index);
                    if (attackerInfo != null)
                    {
                        var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == attackerInfo.Skill);
                        var specialSkillData = SkillData.Skills.FirstOrDefault(s => s.Skill == attackerInfo.SpecialSkill);
                        if (skillData == null || specialSkillData == null) return HookResult.Continue;
                        string skillDesc = victim.GetSkillDescription(skillData.Skill);

                        SkillUtils.PrintToChat(victim,
                            $"{ChatColors.DarkRed}{(attackerInfo.SpecialSkill == Skills.None ? victim.GetSkillName(skillData.Skill) : $"{victim.GetSkillName(specialSkillData.Skill)} -> {victim.GetSkillName(skillData.Skill)}")}{ChatColors.Lime} - {skillDesc}",
                            title: $"{victim.GetTranslationWithoutIlliterate("enemy_skill")} {ChatColors.DarkRed}\u202A{attacker.PlayerName}\u202C{ChatColors.Lime}");
                    }
                }
                return HookResult.Continue;
            }
        }

        private static void CheckUseSkill(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            lock (setLock)
            {
                string? button = Config.LoadedConfig.AlternativeSkillButton;
                if (string.IsNullOrEmpty(button) || button.Length < 2) return;

                string buttonName = $"{char.ToUpperInvariant(button[0])}{button[1..].ToLowerInvariant()}";
                if (!Enum.TryParse<PlayerButtons>(buttonName, out var skillButton)) return;

                if ((pressed & skillButton) == 0) return;

                if (SkillUtils.HasMenu(player)) return;
                if (src.modules.GunsModule.IsExternalMenuOpen(player) || src.menu.SimpleMenu.IsExternalMenuOpen(player)) return;

                var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
                if (playerInfo == null || playerInfo.IsDrawing) return;

                if (SkillsInfo.GetValue<bool>(playerInfo.Skill, "disableOnFreezeTime") && SkillUtils.IsFreezeTime())
                    return;

                if (skillButton == PlayerButtons.Use)
                {
                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid) return;
                    if (pawn.AbsOrigin == null || pawn.AbsRotation == null) return;

                    if (pawn.IsDefusing) return;
                    if (IsAimingAtPlantedBomb(player, pawn)) return;
                    // E also plants the bomb.
                    if (pawn.WeaponServices?.ActiveWeapon.Value?.DesignerName == "weapon_c4") return;

                    Vector eyePos = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
                    Vector endPos = eyePos + SkillUtils.GetForwardVector(pawn.EyeAngles) * 80;

                    ulong mask = (ulong)Masks.SolidBrushOnly | (ulong)(Contents.Player | Contents.Npc);
                    ulong contents = 0;
                    var result = RayTrace.TraceShape(player, eyePos, endPos, mask, contents);

                    if (result.HasValue && result.Value.DidHit)
                    {
                        var entity = Activator.CreateInstance(typeof(CBaseEntity), result.Value.HitEntity) as CBaseEntity;
                        if (entity == null || !entity.IsValid) return;

                        string designer = entity.DesignerName;

                        if (designer.Contains("door"))
                        {
                            if (playerInfo.Skill == Skills.ThirdEye && !ThirdEye.IsOrginalCamera(player))
                                SkillUtils.ToogleDoor(entity, pawn);
                            return;
                        }

                        if (designer == "planted_c4" && player.Team == CsTeam.CounterTerrorist) return;

                        if (designer.Contains("button") || designer.Contains("weapon") || designer.Contains("blocker")) return;
                    }
                }

                Debug.WriteToDebug($"Player {player.PlayerName} used the skill: {playerInfo.Skill} by PlayerButtons: {pressed}", DebugCategory.Skill);
                UseAllSkills(playerInfo, player);
            }
        }

        private const float BombBlockRange = 100f;
        private const float BombBlockCos = 0.7071f;

        private static bool IsAimingAtPlantedBomb(CCSPlayerController player, CCSPlayerPawn pawn)
        {
            if (player.Team != CsTeam.CounterTerrorist) return false;
            if (pawn.AbsOrigin == null) return false;

            foreach (var bomb in Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4"))
            {
                if (bomb == null || !bomb.IsValid || bomb.AbsOrigin == null) continue;
                if (!bomb.BombTicking || bomb.BombDefused) continue;

                float dx = bomb.AbsOrigin.X - pawn.AbsOrigin.X;
                float dy = bomb.AbsOrigin.Y - pawn.AbsOrigin.Y;
                float dz = bomb.AbsOrigin.Z - (pawn.AbsOrigin.Z + pawn.ViewOffset.Z);

                float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distance > BombBlockRange || distance <= 0.01f) continue;

                var forward = SkillUtils.GetForwardVector(pawn.EyeAngles);
                float dot = (forward.X * dx + forward.Y * dy + forward.Z * dz) / distance;

                if (dot >= BombBlockCos) return true;
            }

            return false;
        }

        private static HookResult BulletImpact(EventBulletImpact @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("BulletImpact", @event);
                return HookResult.Continue;
            }
        }

        private static readonly Dictionary<uint, jSkill_SkillInfo> nextRoundPicks = [];

        public static void UpdateSkillHUD(CCSPlayerController? player, jSkill_PlayerInfo? skillPlayer, string? headerLine, string? centerLine, string? extraLine, bool isDescription, string? statusLine = null)
        {
            lock (setLock)
            {
                if (player == null || !player.IsValid) return;

                if (Illiterate.CheckIlliterateSkill(player))
                {
                    centerLine = Illiterate.GetRandomText(centerLine);
                    extraLine = Illiterate.GetRandomText(extraLine);
                }

                var config = Config.LoadedConfig.HtmlHudCustomisation;

                string? notice = skillPlayer != null && skillPlayer.CenterNoticeUntil > DateTime.Now ? skillPlayer.CenterNotice : null;
                bool noticeExclusive = notice != null && skillPlayer!.CenterNoticeExclusive;
                if (noticeExclusive) notice = "<!x>" + notice;

                var cache = skillPlayer?.HudCache;
                if (cache != null
                    && cache.Content != null
                    && cache.PlayerIndex == player.Index
                    && ReferenceEquals(cache.Config, config)
                    && cache.IsDescription == isDescription
                    && string.Equals(cache.Header, headerLine, StringComparison.Ordinal)
                    && string.Equals(cache.Center, centerLine, StringComparison.Ordinal)
                    && string.Equals(cache.Extra, extraLine, StringComparison.Ordinal)
                    && string.Equals(cache.Status, statusLine, StringComparison.Ordinal)
                    && string.Equals(cache.Notice, notice, StringComparison.Ordinal))
                {
                    // Unchanged content: refresh HudResendTicks apart, not every HUD frame (fewer reliable messages per
                    // player, no net-channel overflow). The centre hint fades about 0.1 s after the last message, so
                    // more than ~6 ticks between sends makes it blink. A picture only stays up when re-sent every tick.
                    int resendTicks = cache.Content.Contains("<img", StringComparison.Ordinal) ? 1 : Math.Clamp(config.HudResendTicks, 1, 6);
                    if (Server.TickCount - cache.LastSentTick >= resendTicks)
                    {
                        cache.LastSentTick = Server.TickCount;
                        cache.CountSend(cache.Content.Length, Server.TickCount);
                        player.PrintToCenterHtml(cache.Content);
                    }
                    return;
                }

                var emptySymbol = $"<font class='fontSize-{(string.IsNullOrEmpty(headerLine) || string.IsNullOrEmpty(config.HeaderLineSize) ? "l" : "ml")}'> </font>";
                var emptySymbol2 = $"<font class='fontSize-ml'> </font>";

                string infoLine = string.IsNullOrEmpty(headerLine) || string.IsNullOrEmpty(config.HeaderLineSize)
                    ? ""
                    : $"<font class='fontWeight-Bold fontSize-{config.HeaderLineSize}' color='{config.HeaderLineColor}'>{headerLine}:</font><br>";

                string skillLine = $"{emptySymbol2}<font class='fontWeight-Bold fontSize-{config.SkillLineSize}'>{centerLine}</font>{emptySymbol2}";

                var extraLineSize = isDescription ? config.SkillDescriptionLineSize : config.InfoLineSize;
                string remainingLine = string.IsNullOrWhiteSpace(extraLine) || string.IsNullOrEmpty(extraLineSize)
                    ? ""
                    : $"<br>{emptySymbol}<font class='fontSize-{extraLineSize}' color='{(isDescription ? config.SkillDescriptionLineColor : config.InfoLineColor)}'>{extraLine}</font>{emptySymbol}";

                // Cooldown / status text (a skill's PrintHTML) on its own line under the description.
                string statusHtml = string.IsNullOrWhiteSpace(statusLine) || string.IsNullOrEmpty(config.InfoLineSize)
                    ? ""
                    : $"<br>{emptySymbol}<font class='fontSize-{config.InfoLineSize}' color='{config.InfoLineColor}'>{statusLine}</font>{emptySymbol}";

                // The notice (retakes site call, plant prompt) is ready-made HTML and sits at the TOP of the box,
                // above the skill, so it reads as a separate banner and never covers the skill text.
                string noticeLine = string.IsNullOrWhiteSpace(notice) ? "" : $"{emptySymbol2}{notice}{emptySymbol2}<br>";

                // The centre HTML box is anchored at its bottom and has a limited height: with extra lines fewer
                // lift lines are added so the top of the text is never pushed out of view.
                int extraLines = Math.Max(0, (noticeLine + infoLine + skillLine + remainingLine + statusHtml).Split("<br>").Length - 3);
                int liftLines = Math.Max(0, config.VerticalOffsetLines - extraLines);
                string lift = liftLines > 0 ? string.Concat(Enumerable.Repeat("<br> ", liftLines)) : "";

                var hudContent = "<jRS/>" + noticeLine + infoLine + skillLine + remainingLine + statusHtml + lift;

                // Round-start announcement: the box carries only the banner and is lifted high on screen, so it reads
                // as its own alert at the top; the skill lines come back when it expires.
                if (noticeExclusive)
                {
                    string bigLift = string.Concat(Enumerable.Repeat("<br> ", Math.Max(0, config.NoticeLiftLines)));
                    hudContent = "<jRS/>" + notice!["<!x>".Length..] + bigLift;
                }

                // Keep the message small: an oversized centre-HTML message overflows the client's reliable channel
                // and gets the player kicked. Drop the lift first, then the status line, then shorten the description.
                const int MaxHudBytes = 900;
                if (hudContent.Length > MaxHudBytes && !noticeExclusive)
                {
                    hudContent = "<jRS/>" + noticeLine + infoLine + skillLine + remainingLine + statusHtml;
                    if (hudContent.Length > MaxHudBytes)
                        hudContent = "<jRS/>" + noticeLine + infoLine + skillLine + remainingLine;
                    if (hudContent.Length > MaxHudBytes && !string.IsNullOrEmpty(remainingLine))
                    {
                        int room = Math.Max(0, MaxHudBytes - ("<jRS/>" + noticeLine + infoLine + skillLine).Length - 60);
                        string shortExtra = extraLine!.Length > room ? extraLine[..room] + "…" : extraLine;
                        hudContent = "<jRS/>" + noticeLine + infoLine + skillLine
                            + $"<br>{emptySymbol}<font class='fontSize-{extraLineSize}' color='{config.SkillDescriptionLineColor}'>{shortExtra}</font>{emptySymbol}";
                    }
                }
                if (hudContent.Length > MaxHudBytes)
                    hudContent = hudContent[..MaxHudBytes];

                if (skillPlayer != null)
                {
                    cache ??= skillPlayer.HudCache = new HudCacheEntry();
                    cache.PlayerIndex = player.Index;
                    cache.Config = config;
                    cache.Header = headerLine;
                    cache.Center = centerLine;
                    cache.Extra = extraLine;
                    cache.Status = statusLine;
                    cache.Notice = notice;
                    cache.IsDescription = isDescription;
                    cache.Content = hudContent;
                    cache.LastSentTick = Server.TickCount;
                    cache.CountSend(hudContent.Length, Server.TickCount);
                }

                player.PrintToCenterHtml(hudContent);
            }
        }
    }
}
