using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;

namespace src.player.skills
{
    public class SoundMaker : ISkill
    {
        private const Skills skillName = Skills.SoundMaker;
        private static readonly ConcurrentDictionary<uint, byte> SkillPlayerInfo = [];
        private static readonly object setLock = new();
        private static readonly List<CCSPlayerController> listeners = [];
        private static readonly List<CCSPlayerController> targets = [];
        private static bool emitting;

        private const string soundEventName = "Hostage.Pain";
        private const uint soundEventHash = 1876781570;

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
        }

        public static void NewRound()
        {
            lock (setLock)
                SkillPlayerInfo.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.Key(), 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryRemove(player.Key(), out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayer(player);
            if (playerInfo?.Skill == skillName)
                SkillPlayerInfo.TryRemove(player.Key(), out _);
        }

        public static void PlayerMakeSound(UserMessage um)
        {
            if (emitting || SkillPlayerInfo.IsEmpty) return;

            var soundevent = um.ReadUInt("soundevent_hash");
            if (soundevent != soundEventHash) return;

            var entityIndex = um.ReadUInt("source_entity_index");
            if (entityIndex == 0) return;

            CCSPlayerController? emitter = null;
            foreach (var p in PlayerManager.GetTickPlayers().Where(p => p != null && p.IsValid))
            {
                if (p.PlayerPawn.Value?.Index == entityIndex)
                {
                    emitter = p;
                    break;
                }

                var entities = EntityManager.GetPlayerEntities(p.Key(), "empty_prop");
                if (entities.Count > 0 && entities[0] == entityIndex)
                {
                    emitter = p;
                    break;
                }
            }

            List<CCSPlayerController> allowed = [];

            if (emitter != null && CanEmit(emitter, out _))
                foreach (var recipient in um.Recipients)
                {
                    if (recipient == null || !recipient.IsValid) continue;
                    if (recipient.Team == emitter.Team) continue;
                    if (!IsListener(recipient)) continue;

                    allowed.Add(recipient);
                }

            um.Recipients.Clear();
            foreach (var recipient in allowed)
                um.Recipients.Add(recipient);
        }

        private static bool IsListener(CCSPlayerController recipient)
        {
            if (SkillPlayerInfo.ContainsKey(recipient.Key())) return true;

            var bot = PlayerManager.GetPlayerEvent(recipient);
            return bot != null && bot.IsValid && SkillPlayerInfo.ContainsKey(bot.Key());
        }

        public static void OnTick()
        {
            if (SkillPlayerInfo.IsEmpty) return;

            if (Server.TickCount % 60 != 0) return;

            int cooldown = SkillsInfo.GetValue<int>(skillName, "cooldown");
            if (cooldown < 1) cooldown = 1;
            if ((Server.TickCount / 60) % cooldown != 0) return;

            float volume = SkillsInfo.GetValue<float>(skillName, "soundVolume");
            var players = PlayerManager.GetTickPlayers();

            listeners.Clear();
            foreach (var player in players)
                if (player != null && player.IsValid && !player.IsBot && IsListener(player))
                    listeners.Add(player);

            if (listeners.Count == 0) return;

            foreach (var player in players)
            {
                if (!CanEmit(player, out var pawn)) continue;

                targets.Clear();
                foreach (var listener in listeners)
                    if (listener.Team != player.Team)
                        targets.Add(listener);

                if (targets.Count == 0) continue;

                CBaseEntity source = pawn!;
                var entities = EntityManager.GetPlayerEntities(player.Key(), "empty_prop");

                if (entities.Count > 0)
                {
                    var entity = Utilities.GetEntityFromIndex<CDynamicProp>((int)entities[0]);
                    if (entity == null || !entity.IsValid || IsAtWorldOrigin(entity.AbsOrigin)) continue;
                    source = entity;
                }

                emitting = true;
                try
                {
                    source.EmitSound(soundEventName, new RecipientFilter([.. targets]), volume);
                }
                finally
                {
                    emitting = false;
                }
            }
        }

        private static bool CanEmit(CCSPlayerController? player, out CCSPlayerPawn? pawn)
        {
            pawn = null;

            if (player == null || !player.IsValid || player.IsHLTV) return false;
            if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return false;

            var playerPawn = player.PlayerPawn?.Value;
            if (playerPawn == null || !playerPawn.IsValid) return false;
            if (playerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || playerPawn.Health <= 0) return false;
            if (IsAtWorldOrigin(playerPawn.AbsOrigin)) return false;

            pawn = playerPawn;
            return true;
        }

        private static bool IsAtWorldOrigin(Vector? origin)
        {
            if (origin == null) return true;
            return MathF.Abs(origin.X) < 1f && MathF.Abs(origin.Y) < 1f && MathF.Abs(origin.Z) < 1f;
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e3ed8c", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Common, int cooldown = 2, float soundVolume = 1f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public float SoundVolume { get; set; } = soundVolume;
            public int Cooldown { get; set; } = cooldown;
        }
    }
}