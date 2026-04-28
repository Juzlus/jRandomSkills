using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using static src.jRandomSkills;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace src.player.skills
{
    public class Phoenix : ISkill
    {
        private const Skills skillName = Skills.Phoenix;
        private static readonly object setLock = new();
        private static readonly ConcurrentDictionary<ulong, Timer> timers = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo?.Skill == skillName)
            {
                if (Instance.Random.NextDouble() <= playerInfo.SkillChance)
                {
                    lock (setLock)
                    {
                        ulong steamID = player.SteamID;
                        int team = player.TeamNum;

                        if (team != 2 && team != 3) return;

                        player.Respawn();

                        Server.NextFrame(() => {
                            lock (setLock)
                            {
                                var player = Utilities.GetPlayerFromSteamId(steamID);
                                if (player == null || !player.IsValid || !player.PlayerPawn.IsValid) return;

                                var pawn = player.PlayerPawn.Value;
                                if (pawn == null || !pawn.IsValid) return;

                                bool isBlock = team != player.TeamNum || player.TeamChanged;

                                player.Respawn();

                                if (isBlock)
                                {
                                    pawn.Flags |= (uint)Flags_t.FL_FROZEN;
                                    pawn.Teleport(new Vector(0, 0, -1000), new QAngle(90, 0, 0));

                                    bool isFreezeTime = Instance.GameRules != null && Instance.GameRules.FreezePeriod == true;

                                    if (!isFreezeTime)
                                    {
                                        Server.NextFrame(() =>
                                        {
                                            if (pawn == null || !pawn.IsValid) return;
                                            pawn.CommitSuicide(false, true);
                                        });
                                        return;
                                    }

                                    ulong steamId = player.SteamID;

                                    if (!timers.ContainsKey(player.SteamID))
                                    {
                                        var timer = Instance.AddTimer(1f, () =>
                                        {
                                            if (player == null || !player.IsValid || pawn == null || !pawn.IsValid)
                                            {
                                                if (timers.TryRemove(steamId, out var t))
                                                    t.Kill();
                                                return;
                                            }

                                            bool isFreezeTime = Instance.GameRules != null && Instance.GameRules.FreezePeriod == true;

                                            if (!isFreezeTime)
                                            {
                                                if (timers.TryRemove(steamId, out var t))
                                                    t.Kill();

                                                pawn.CommitSuicide(false, true);
                                                return;
                                            }

                                        }, TimerFlags.STOP_ON_MAPCHANGE | TimerFlags.REPEAT);

                                        timers.TryAdd(player.SteamID, timer);
                                    }

                                    return;
                                }
                            }
                        });

                        SkillUtils.PrintToChat(player, player.GetTranslation("phoenix_respawn"));
                    }
                }
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;
            float newChance = (float)Instance.Random.NextDouble() * (SkillsInfo.GetValue<float>(skillName, "ChanceTo") - SkillsInfo.GetValue<float>(skillName, "ChanceFrom")) + SkillsInfo.GetValue<float>(skillName, "ChanceFrom");
            playerInfo.SkillChance = newChance;
            SkillUtils.PrintToChat(player, $"{ChatColors.DarkRed}{player.GetSkillName(skillName)}{ChatColors.Lime}: {player.GetSkillDescription(skillName, newChance)}",
                border: !Utilities.GetPlayers().Any(p => p.Team == player.Team && !p.IsBot && p != player) ? "tb" : "t");
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#ff5C0A", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, float chanceFrom = .2f, float chanceTo = .4f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public float ChanceFrom { get; set; } = chanceFrom;
            public float ChanceTo { get; set; } = chanceTo;
        }
    }
}