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
            SkillPlayerInfo.TryAdd(player.Index, 0);
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryRemove(player.Index, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;

            var playerInfo = PlayerManager.GetPlayerByIndex(player!.Index);
            if (playerInfo?.Skill == skillName)
                SkillPlayerInfo.TryRemove(player.Index, out _);
        }

        public static void PlayerMakeSound(UserMessage um)
        {
            var soundevent = um.ReadUInt("soundevent_hash");
            if (soundevent != soundEventHash) return;

            var entityIndex = um.ReadUInt("source_entity_index");
            if (entityIndex == 0) return;

            CCSPlayerController? emitter = null;
            foreach (var p in Utilities.GetPlayers().Where(p => p != null && p.IsValid))
            {
                if (p.PlayerPawn.Value?.Index == entityIndex)
                {
                    emitter = p;
                    break;
                }

                var entities = EntityManager.GetPlayerEntities(p.Index, "empty_prop");
                if (entities.Count > 0 && entities[0] == entityIndex)
                {
                    emitter = p;
                    break;
                }
            }

            if (emitter == null)
            {
                foreach (var p in Utilities.GetPlayers().Where(p => p != null && p.IsValid))
                    um.Recipients.Remove(p);
                return;
            }

            foreach (var recipient in Utilities.GetPlayers().Where(p => p != null && p.IsValid))
            {
                bool hasSkill = SkillPlayerInfo.ContainsKey(recipient.Index);

                var bot = PlayerManager.GetPlayerEvent(recipient);
                bool botSkill = bot != null && SkillPlayerInfo.ContainsKey(bot.Index);

                bool isTeammate = recipient.Team == emitter.Team;

                if ((!hasSkill && !botSkill) || isTeammate)
                    um.Recipients.Remove(recipient);
            }
        }

        public static void OnTick()
        {
            if (Server.TickCount % (60 * SkillsInfo.GetValue<int>(skillName, "cooldown")) != 0) return;

            foreach (var player in Utilities.GetPlayers()
                .Where(p => p != null && p.IsValid && p.PlayerPawn.Value != null && p.PlayerPawn.Value.IsValid && p.PlayerPawn.Value.Health > 0))
            {
                var entities = EntityManager.GetPlayerEntities(player.Index, "empty_prop");

                if (entities.Count == 0)
                {
                    player.PlayerPawn.Value!.EmitSound(soundEventName, volume: 1f);
                    continue;
                }

                var entity = Utilities.GetEntityFromIndex<CDynamicProp>((int)entities[0]);
                if (entity != null && entity.IsValid)
                    entity.EmitSound(soundEventName, volume: 1f);
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#e3ed8c", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = false, bool needsTeammates = false, string requiredPermission = "", int maxPerServer = -1, Rarity rarity = Rarity.Common, int cooldown = 2) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, maxPerServer, rarity)
        {
            public int Cooldown { get; set; } = cooldown;
        }
    }
}