using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using RayTraceAPI;
using src.player.skills;
using src.utils;
using System.Collections.Concurrent;
using static CounterStrikeSharp.API.Core.Listeners;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player
{
    public static partial class Event
    {
        private static HookResult BombBeginplant(EventBombBeginplant @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("BombBeginplant", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BombAbortplant(EventBombAbortplant @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("BombAbortplant", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BombPlanted(EventBombPlanted @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("BombPlanted", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult BombBegindefuse(EventBombBegindefuse @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("BombBegindefuse", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult DecoyStarted(EventDecoyStarted @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("DecoyStarted", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult DecoyDetonate(EventDecoyDetonate @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("DecoyDetonate", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult SmokegrenadeDetonate(EventSmokegrenadeDetonate @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("SmokegrenadeDetonate", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult SmokegrenadeExpired(EventSmokegrenadeExpired @event, GameEventInfo info)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("SmokegrenadeExpired", @event);
                return HookResult.Continue;
            }
        }

        private static HookResult OnTakeDamage(DynamicHook h)
        {
            lock (setLock)
            {
                DispatchOnTakeDamage(h);

                if (Fortnite.skillInThisRound == true &&
                    !Instance.SkillPlayer.Any(p => !p.IsDrawing && p.Skill == Skills.Fortnite))
                    Instance.SkillAction("Fortnite", "OnTakeDamage", [h]);

                return HookResult.Continue;
            }
        }

        private static HookResult OnTriggerEnter(DynamicHook hook)
        {
            lock (setLock)
            {
                CBaseTrigger trigger = hook.GetParam<CBaseTrigger>(0);
                CBaseEntity entity = hook.GetParam<CBaseEntity>(1);

                DispatchToActiveSkills("OnTriggerEnter", trigger, entity);
                return HookResult.Continue;
            }
        }

        private static HookResult OnTriggerExit(DynamicHook hook)
        {
            lock (setLock)
            {
                CBaseTrigger trigger = hook.GetParam<CBaseTrigger>(0);
                CBaseEntity entity = hook.GetParam<CBaseEntity>(1);

                DispatchToActiveSkills("OnTriggerExit", trigger, entity);
                return HookResult.Continue;
            }
        }

        private static HookResult OnWeaponCanAcquire(DynamicHook hook)
        {
            lock (setLock)
            {
                CCSPlayer_ItemServices itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
                if (itemServices == null || itemServices.Pawn.Value == null || !itemServices.Pawn.Value.IsValid) return HookResult.Continue;

                CEconItemView econItem = hook.GetParam<CEconItemView>(1);
                if (econItem == null) return HookResult.Continue;

                CBasePlayerPawn pawn = itemServices.Pawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.Controller.Value == null || !pawn.Controller.Value.IsValid) return HookResult.Continue;

                CCSPlayerController player = pawn.Controller.Value.As<CCSPlayerController>();
                if (player == null || !player.IsValid) return HookResult.Continue;

                var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
                if (playerInfo == null) return HookResult.Continue;

                CCSWeaponBaseVData vdata = VirtualFunctions.GetCSWeaponDataFromKeyFunc.Invoke(-1, econItem.ItemDefinitionIndex.ToString());
                if (vdata == null || vdata.Handle == IntPtr.Zero) return HookResult.Continue;

                var activeSkills = Instance.SkillPlayer
                    .Where(p => !p.IsDrawing)
                    .Select(p => p.Skill.ToString())
                    .Distinct();

                bool block = false;
                foreach (string skillName in activeSkills)
                {
                    bool? result = (bool?)Instance.SkillAction(skillName, "OnWeaponCanAcquire", [hook, player, econItem, vdata]);
                    if (result == true)
                    {
                        block = true;
                        break;
                    }
                }

                return block ? HookResult.Handled : HookResult.Continue;
            }
        }

        private static HookResult WeaponDrop(DynamicHook hook)
        {
            lock (setLock)
            {
                CCSPlayerController player = hook.GetParam<CCSPlayerController>(0);
                if (player == null || !player.IsValid)
                    return HookResult.Continue;

                var activeSkills = Instance.SkillPlayer
                    .Where(p => !p.IsDrawing)
                    .Select(p => p.Skill.ToString())
                    .Distinct();

                bool block = false;
                foreach (string skillName in activeSkills)
                {
                    bool? result = (bool?)Instance.SkillAction(skillName, "WeaponDrop", [hook, player]);
                    if (result == true)
                    {
                        block = true;
                        break;
                    }
                }
                return block ? HookResult.Handled : HookResult.Continue;
            }
        }

        private static void EntitySpawned(CEntityInstance entity)
        {
            lock (setLock)
            {
                DispatchToActiveSkills("OnEntitySpawned", entity);
            }
        }

        public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList infoList)
        {
            long perfStart = PerfLog.Start();
            lock (setLock)
            {
                try
                {
                    // Keep dying entities out of snapshots until the engine processes the kill.
                    var dying = EntityManager.GetRecentlyDestroyedSnapshot();
                    if (dying.Count > 0)
                    {
                        foreach (var (info, player) in infoList)
                        {
                            if (player == null || !player.IsValid) continue;
                            foreach (var entityIndex in dying)
                                if (info.TransmitEntities.Contains(entityIndex))
                                    info.TransmitEntities.Remove(entityIndex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Server.PrintToConsole($"[jRandomSkills] CheckTransmit dying-filter failed: {ex.Message}");
                }

                DispatchToActiveSkills("CheckTransmit", infoList);
            }
            PerfLog.Sample("CheckTransmit", perfStart);
        }

        public static void EnableTransmit()
        {
            if (!isTransmitRegistered)
            {
                Instance?.RegisterListener<CheckTransmit>(CheckTransmit);
                isTransmitRegistered = true;
            }
        }
    }
}
