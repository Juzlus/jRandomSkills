using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using src.player;
using src.player.skills;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WASDMenuAPI.Classes;
using WASDSharedAPI;

namespace src.utils
{
    public static class SkillUtils
    {
        private static readonly MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int> HEGrenadeProjectile_CreateFunc = new(GameData.GetSignature("HEGrenadeProjectile_CreateFunc"));
        private static readonly MemoryFunctionVoid<nint, float, RoundEndReason, nint, nint> TerminateRoundFunc = new(GameData.GetSignature("CCSGameRules_TerminateRound"));

        public static void PrintToChat(CCSPlayerController player, string msg, string border = "tb", string title = jRandomSkills.Tag)
        {
            if (border.Contains('t'))
                player.PrintToChat($" {ChatColors.Green}―――――――――――{ChatColors.DarkRed}◢◆◤ {title} ◥◆◣{ChatColors.Green}―――――――――――");
            if (!string.IsNullOrEmpty(msg))
                player.PrintToChat($" {ChatColors.Green} {msg}");
            if (border.Contains('b'))
                player.PrintToChat($" {ChatColors.Green}―――――――――――――――{new string('―', title.Length)}―――――――――――――――");
        }

        public static bool IsFreezeTime()
        {
            return jRandomSkills.Instance?.GameRules?.FreezePeriod == true;
        }

        public static void RegisterSkill(Skills skill, string color, bool display = true)
        {
            if (!SkillData.Skills.Any(s => s.Skill == skill))
                SkillData.Skills.Add(new jSkill_SkillInfo(skill, color, display));
        }

        public static void TryGiveWeapon(CCSPlayerController player, CsItem item, int count = 1)
        {
            string? itemString = EnumUtils.GetEnumMemberAttributeValue(item);
            if (string.IsNullOrWhiteSpace(itemString)) return;

            if (player == null || !player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.Value.IsValid) return;
            if (player.PlayerPawn.Value.WeaponServices == null) return;

            var exists = player.PlayerPawn.Value.WeaponServices.MyWeapons
                .FirstOrDefault(w => w != null && w.IsValid && w.Value != null && w.Value.IsValid && w.Value.DesignerName == itemString);
            if (exists == null)
                for (int i = 0; i < count; i++)
                    player.GiveNamedItem(item);
        }

        public static double GetDistance(Vector vector1, Vector vector2)
        {
            return Math.Sqrt(Math.Pow(vector2.X - vector1.X, 2) + Math.Pow(vector2.Y - vector1.Y, 2) + Math.Pow(vector2.Z - vector1.Z, 2));
        }

        public static string SecondsToTimer(int totalSeconds)
        {
            if (totalSeconds <= 0) return "00:00";
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return $"{minutes:D2}:{seconds:D2}";
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

        public static void ApplyScreenColor(CCSPlayerController player, int r, int g, int b, int a, int duration, int holdTime, int flags = 1)
        {
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
            playerPawn.AcceptInput("SetScale", null, null, scale.ToString());
            Server.NextFrame(() => Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_CBodyComponent"));
        }

        public static Vector? GetSpawnPointVector(CCSPlayerController player, bool enemySpawn = false)
        {
            var spawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>(player.Team == CsTeam.Terrorist && !enemySpawn
                ? "info_player_terrorist" : "info_player_counterterrorist").ToList();
            if (spawns.Count != 0)
            {
                var randomSpawn = spawns[jRandomSkills.Instance.Random.Next(spawns.Count)];
                if (randomSpawn != null && randomSpawn.IsValid && randomSpawn.AbsOrigin != null)
                    return randomSpawn.AbsOrigin;
            }
            return null;
        }

        public static void CreateHEGrenadeProjectile(Vector pos, QAngle angle, Vector vel, int teamNum)
        {
            HEGrenadeProjectile_CreateFunc.Invoke(pos.Handle, angle.Handle, vel.Handle, vel.Handle, IntPtr.Zero, 44, teamNum);
        }

        public static void TakeHealth(CCSPlayerPawn? pawn, int damage)
        {
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                return;

            if (pawn.Controller.Value != null && pawn.Controller.Value.IsValid)
            {
                var playerInfo = jRandomSkills.Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == pawn.Controller.Value.SteamID);
                if (playerInfo == null) return;
                if (playerInfo.Skill == Skills.Jester && Jester.GetJesterMode())
                    return;
            }

            int newHealth = (int)(pawn.Health - damage);
            pawn.Health = newHealth;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

            if (pawn.Health <= 0)
                Server.NextFrame(() =>
                {
                    pawn?.CommitSuicide(false, true);
                });
        }

        public static void ResetPrintHTML(CCSPlayerController? player)
        {
            var playerInfo = jRandomSkills.Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;
            playerInfo.PrintHTML = null;
        }

        public static CTriggerMultiple? CreateTrigger(string name, float radius, Vector pos)
        {
            if (pos == null) return null;

            var trigger = Utilities.CreateEntityByName<CTriggerMultiple>("trigger_multiple");
            if (trigger == null || trigger.AbsOrigin == null) return null;

            trigger.Collision.SolidType = SolidType_t.SOLID_CAPSULE;
            trigger.Collision.SolidFlags = 0;
            trigger.Spawnflags = 1;
            trigger.Globalname = $"{name}_{trigger.Index}";
            trigger.Collision.SolidFlags = 1;

            trigger.AbsOrigin.X = pos.X;
            trigger.AbsOrigin.Y = pos.Y;
            trigger.AbsOrigin.Z = pos.Z;

            trigger.Collision.CapsuleRadius = radius;
            trigger.Collision.BoundingRadius = radius;

            trigger.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_TRIGGER;
            trigger.Collision.EnablePhysics = 1;
            trigger.Collision.TriggerBloat = 0;

            trigger.Collision.SurroundType = SurroundingBoundsType_t.USE_OBB_COLLISION_BOUNDS;
            trigger.Collision.CollisionAttribute.CollisionFunctionMask = 39;
            trigger.Collision.CollisionAttribute.CollisionGroup = 2;

            trigger.DispatchSpawn();
            return trigger;
        }

        public static void AddHealth(CCSPlayerPawn? pawn, int extraHealth, int maxHealth = 100)
        {
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                return;

            int newHealth = (int)(pawn.Health + extraHealth);
            pawn.Health = Math.Min(newHealth, maxHealth);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

            pawn.MaxHealth = maxHealth;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");
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
                _ => designerName
            };

            return designerName;
        }

        private static IWasdMenuManager? GetMenuManager()
        {
            if (jRandomSkills.Instance.MenuManager == null)
                jRandomSkills.Instance.MenuManager = new WasdManager();
            return jRandomSkills.Instance.MenuManager;
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

        public static void UpdateMenu(CCSPlayerController? player, ConcurrentBag<(string, string)> items)
        {
            if (player == null) return;

            var manager = GetMenuManager();
            if (manager == null) return;

            var playerInfo = jRandomSkills.Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null) return;

            Dictionary<string, Action<CCSPlayerController, IWasdMenuOption>> list = [];
            foreach (var item in items)
                list.TryAdd(item.Item1, (p, option) =>
                {
                    jRandomSkills.Instance.SkillAction(playerInfo.Skill.ToString(), "TypeSkill", [p, new[] { item.Item2 }]);
                    manager.CloseMenu(p);
                });

            manager.UpdateActiveMenu(player, list);
        }

        public static void CreateMenu(CCSPlayerController? player, ConcurrentBag<(string, string)> enemies, (string, string, bool)? lastElement = null)
        {
            if (player == null || !player.IsValid) return;

            var playerInfo = jRandomSkills.Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
            if (playerInfo == null || !playerInfo.DisplayHUD) return;

            var skillData = SkillData.Skills.FirstOrDefault(s => s.Skill == playerInfo.Skill);
            if (skillData == null) return;

            var manager = GetMenuManager();
            if (manager == null) return;

            var config = Config.LoadedConfig.HtmlHudCustomisation;
            var your_skill = player.GetTranslation("your_skill");
            var emptySymbol = $"<font class='fontSize-{(string.IsNullOrEmpty(your_skill) ? "l" : "ml")}'> </font>";

            string infoLine = string.IsNullOrEmpty(your_skill)
                ? ""
                : $"<font class='fontWeight-Bold fontSize-{config.HeaderLineSize}' color='{config.HeaderLineColor}'>{your_skill}:</font><br>";

            string skillLine = $"<font class='fontWeight-Bold fontSize-{config.SkillLineSize}' color='{skillData.Color}'>{player.GetSkillName(skillData.Skill)}</font><br>";

            var skill_select_info = player.GetTranslation($"{playerInfo.Skill.ToString().ToLower()}_select_info");
            string remainingLine = string.IsNullOrWhiteSpace(skill_select_info)
                ? ""
                : $"<font class='fontSize-{config.WSADMenuSelectInfoLineSize}' color='{config.WSADMenuSelectInfoLineColor}'>{skill_select_info}</font><br>";

            var hudContent = infoLine + skillLine + remainingLine;

            string controllsLine = 
                $"{emptySymbol}<font class='fontSize-{config.WSADMenuControllsLineSize}' color='{config.WSADMenuControllsLineColor1}'>{player.GetTranslation($"menu_controlls_scroll")}</font>"
                + $"<font class='fontSize-{config.WSADMenuControllsLineSize}' color='{config.WSADMenuControllsLineColor2}'>{player.GetTranslation($"menu_controlls_padding")}</font>"
                + $"<font class='fontSize-{config.WSADMenuControllsLineSize}' color='{config.WSADMenuControllsLineColor3}'>{player.GetTranslation($"menu_controlls_select")}</font>{emptySymbol}";

            string itemText = $"<font class='fontSize-{config.WSADMenuItemLineSize}' color='{config.WSADMenuItemLineColor}'>{{0}}</font><br>";
            string itemHoverText = $"<font class='fontSize-{config.WSADMenuItemLineSize}'><font color='purple'>[ </font><font color='{config.WSADMenuItemHoverLineColor}'>{{0}}</font><font color='purple'> ]</font></font><br>";

            IWasdMenu menu = manager.CreateMenu(hudContent, itemText, itemHoverText, controllsLine);
            foreach (var enemy in enemies)
                menu.Add(enemy.Item1, (p, option) =>
                {
                    jRandomSkills.Instance.SkillAction(playerInfo.Skill.ToString(), "TypeSkill", [p, new[] { enemy.Item2 }]);
                    manager.CloseMenu(p);
                });
            if (lastElement != null)
                menu.Add(lastElement.Value.Item1, (p, option) =>
                {
                    jRandomSkills.Instance.SkillAction(playerInfo.Skill.ToString(), "TypeSkill", [p, new[] { lastElement.Value.Item2 }]);
                    if (lastElement.Value.Item3)
                        manager.CloseMenu(p);
                });
            manager.OpenMainMenu(player, menu);
        }

        public static void SetTeamScores(short ctScore, short tScore, RoundEndReason roundEndReason)
        {
            if (jRandomSkills.Instance == null || jRandomSkills.Instance.GameRules == null) return;
            UpdateServerTeamScores(ctScore, tScore);
            TerminateRoundFunc.Invoke(jRandomSkills.Instance.GameRules.Handle, 5f, roundEndReason, 0, 0);
        }

        public static void TerminateRound(CsTeam winnerTeam)
        {
            if (jRandomSkills.Instance == null || jRandomSkills.Instance.GameRules == null) return;
            var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            var ctTeam = teams.First(t => t.IsValid && (CsTeam)t.TeamNum == CsTeam.CounterTerrorist);
            var tTeams = teams.First(t => t.IsValid && (CsTeam)t.TeamNum == CsTeam.Terrorist);
            if (ctTeam == null || tTeams == null) return;

            short ctScore = (short)(winnerTeam == CsTeam.CounterTerrorist ? ctTeam.Score + 1 : ctTeam.Score);
            short tScore = (short)(winnerTeam == CsTeam.Terrorist ? tTeams.Score + 1 : tTeams.Score);

            UpdateServerTeamScores(ctScore, tScore);
            TerminateRoundFunc.Invoke(jRandomSkills.Instance.GameRules.Handle, 5f, winnerTeam == CsTeam.CounterTerrorist ? RoundEndReason.BombDefused : RoundEndReason.TargetBombed, 0, 0);
        }

        private static void UpdateServerTeamScores(short ctScore, short tScore)
        {
            if (jRandomSkills.Instance == null || jRandomSkills.Instance.GameRules == null) return;
            int totalRoundsPlayed = ctScore + tScore;
            int maxRounds = ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 24;
            int halfRounds = maxRounds / 2;
            int overtimeMaxRounds = ConVar.Find("mp_overtime_maxrounds")?.GetPrimitiveValue<int>() ?? 6;
            int overtimeLimit = ConVar.Find("mp_overtime_limit")?.GetPrimitiveValue<int>() ?? 1;

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
                        if ( currentOvertime < overtimeLimit)
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
            var ctTeam = teams.First(t => t.IsValid && (CsTeam)t.TeamNum == CsTeam.CounterTerrorist);
            var tTeams = teams.First(t => t.IsValid && (CsTeam)t.TeamNum == CsTeam.Terrorist);

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