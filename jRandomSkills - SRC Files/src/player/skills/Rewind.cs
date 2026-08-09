using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;

namespace src.player.skills
{
    public class Rewind : ISkill
    {
        private const Skills skillName = Skills.Rewind;

        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            foreach (var skillInfo in SkillPlayerInfo.Values)
                DestroyMarker(skillInfo);

            SkillPlayerInfo.Clear();
        }

        public static void RoundEnd()
        {
            foreach (var skillInfo in SkillPlayerInfo.Values)
                DestroyMarker(skillInfo);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;

            SkillPlayerInfo[player.Index] = new PlayerSkillInfo
            {
                CanUse = true,
                Cooldown = DateTime.MinValue,
            };
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null) return;

            if (SkillPlayerInfo.TryRemove(player.Index, out var skillInfo))
                DestroyMarker(skillInfo);

            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            if (SkillPlayerInfo.TryRemove(playerIndex, out var skillInfo))
                DestroyMarker(skillInfo);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            if (SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo))
            {
                skillInfo.ReturnTick = 0;
                DestroyMarker(skillInfo);
            }
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) return;
            if (!skillInfo.CanUse || skillInfo.ReturnTick != 0) return;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return;

            skillInfo.Mark = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
            skillInfo.MarkAngle = new QAngle(0, pawn.V_angle.Y, 0);
            skillInfo.ReturnTick = Server.TickCount + (int)(SkillsInfo.GetValue<float>(skillName, "returnDelay") * 64);
            skillInfo.CanUse = false;
            skillInfo.Cooldown = DateTime.Now;

            SpawnMarker(player, skillInfo);
            SkillUtils.EmitSoundToPlayer(player, "UIPanorama.buttonclick", SkillsInfo.GetValue<float>(skillName, "soundVolume"));
        }

        public static void OnTick()
        {
            if (SkillPlayerInfo.IsEmpty) return;

            bool hudFrame = SkillUtils.IsHudFrame();

            foreach (var player in PlayerManager.GetTickPlayers())
            {
                if (player == null || !player.IsValid) continue;
                if (!SkillPlayerInfo.TryGetValue(player.Index, out var skillInfo)) continue;
                if (PlayerManager.GetPlayerByIndex(player.Index)?.Skill != skillName) continue;

                if (skillInfo.ReturnTick != 0 && Server.TickCount >= skillInfo.ReturnTick)
                {
                    skillInfo.ReturnTick = 0;
                    Teleport(player, skillInfo);
                    DestroyMarker(skillInfo);
                }

                if (hudFrame)
                    UpdateHUD(player, skillInfo);
            }
        }

        private static void Teleport(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            if (skillInfo.Mark == null) return;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            {
                skillInfo.Mark = null;
                return;
            }

            pawn.Teleport(skillInfo.Mark, skillInfo.MarkAngle, new Vector(0, 0, 0));
            skillInfo.Mark = null;
        }

        private static void SpawnMarker(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            if (skillInfo.Mark == null) return;
            if (EntityManager.OverBudget()) return;

            Color color = Color.White;
            try { color = ColorTranslator.FromHtml(SkillsInfo.GetValue<string>(skillName, "color")); }
            catch { }

            Vector bottom = new(skillInfo.Mark.X, skillInfo.Mark.Y, skillInfo.Mark.Z + 2);
            Vector top = new(skillInfo.Mark.X, skillInfo.Mark.Y, skillInfo.Mark.Z + 68);

            var beam = EntityManager.CreateTrackedBeam(player.Index, bottom, top, color);
            if (beam == null || !beam.IsValid) return;

            beam.Width = .6f;
            beam.EndWidth = .6f;

            Utilities.SetStateChanged(beam, "CBeam", "m_fWidth");
            Utilities.SetStateChanged(beam, "CBeam", "m_fEndWidth");

            skillInfo.MarkerIndex = beam.Index;
        }

        private static void DestroyMarker(PlayerSkillInfo skillInfo)
        {
            if (skillInfo.MarkerIndex == null) return;

            uint markerIndex = skillInfo.MarkerIndex.Value;
            skillInfo.MarkerIndex = null;

            EntityManager.DestroyBeam(markerIndex);
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
            float cooldown = Math.Max(time, 0);

            if (cooldown == 0 && !skillInfo.CanUse)
                skillInfo.CanUse = true;

            var playerInfo = PlayerManager.GetPlayerByIndex(player.Index);
            if (playerInfo == null) return;

            if (skillInfo.ReturnTick != 0)
            {
                int remaining = (int)Math.Ceiling((skillInfo.ReturnTick - Server.TickCount) / 64f);
                playerInfo.PrintHTML = $"<font color='#00FF00'>{player.GetTranslation("rewind_pending_info", Math.Max(remaining, 0))}</font>";
            }
            else if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public class PlayerSkillInfo
        {
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
            public Vector? Mark { get; set; }
            public QAngle? MarkAngle { get; set; }
            public uint? MarkerIndex { get; set; }
            public int ReturnTick { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#4fc3f7", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Uncommon, float returnDelay = 4f, float cooldown = 30f, float soundVolume = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float SoundVolume { get; set; } = soundVolume;
            public float ReturnDelay { get; set; } = returnDelay;
            public float Cooldown { get; set; } = cooldown;
        }
    }
}
