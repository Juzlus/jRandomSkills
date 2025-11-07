using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using static src.jRandomSkills;
using System.Collections.Concurrent;
using src.utils;

namespace src.player.skills
{
    public class Fortnite : ISkill
    {
        private const Skills skillName = Skills.Fortnite;
        private static readonly ConcurrentDictionary<ulong, PlayerSkillInfo> SkillPlayerInfo = [];
        private static readonly ConcurrentDictionary<ulong, int> barricades = [];
        private static readonly object setLock = new();

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"), false);
            Instance.ManifestResources.Add(SkillsInfo.GetValue<string>(skillName, "propModel"));
        }

        public static void NewRound()
        {
            lock (setLock)
            {
                SkillPlayerInfo.Clear();
                barricades.Clear();
            }
        }

        public static void OnTick()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                var playerInfo = Instance.SkillPlayer.FirstOrDefault(p => p.SteamID == player.SteamID);
                if (playerInfo?.Skill == skillName)
                    if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
                        UpdateHUD(player, skillInfo);
            }
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryAdd(player.SteamID, new PlayerSkillInfo
            {
                SteamID = player.SteamID,
                CanUse = true,
                Cooldown = DateTime.MinValue,
            });
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            SkillPlayerInfo.TryRemove(player.SteamID, out _);
            SkillUtils.ResetPrintHTML(player);
        }

        private static void UpdateHUD(CCSPlayerController player, PlayerSkillInfo skillInfo)
        {
            float cooldown = 0;
            if (skillInfo != null)
            {
                float time = (int)Math.Ceiling((skillInfo.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) - DateTime.Now).TotalSeconds);
                cooldown = Math.Max(time, 0);

                if (cooldown == 0 && skillInfo?.CanUse == false)
                    skillInfo.CanUse = true;
            }

            var playerInfo = Instance.SkillPlayer.FirstOrDefault(s => s.SteamID == player?.SteamID);
            if (playerInfo == null) return;

            if (cooldown == 0)
                playerInfo.PrintHTML = null;
            else
                playerInfo.PrintHTML = $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{cooldown}</font>")}";
        }

        public static void UseSkill(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn?.CBodyComponent == null) return;

            if (SkillPlayerInfo.TryGetValue(player.SteamID, out var skillInfo))
            {
                if (!player.IsValid || !player.PawnIsAlive) return;
                if (skillInfo.CanUse)
                {
                    skillInfo.CanUse = false;
                    skillInfo.Cooldown = DateTime.Now;
                    CreateBox(player);
                }
            }
        }

        private static void CreateBox(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;
            var box = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic_override");
            if (box == null || playerPawn == null || !playerPawn.IsValid || playerPawn.AbsOrigin == null || playerPawn.AbsRotation == null) return;

            float distance = 50;
            Vector pos = playerPawn.AbsOrigin + SkillUtils.GetForwardVector(playerPawn.AbsRotation) * distance;
            QAngle angle = new(playerPawn.AbsRotation.X, playerPawn.V_angle.Y + 90, playerPawn.AbsRotation.Z);

            box.Entity!.Name = box.Globalname = $"FortniteWall_{Server.TickCount}";
            box.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
            box.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags = (uint)(box.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags & ~(1 << 2));
            box.DispatchSpawn();
            barricades.TryAdd(box.Index, SkillsInfo.GetValue<int>(skillName, "barricadeHealth"));
            Server.NextFrame(() =>
            {
                box.SetModel(SkillsInfo.GetValue<string>(skillName, "propModel"));
                box.Teleport(pos, angle, null);
            });
        }

        public static void OnTakeDamage(DynamicHook h)
        {
            CEntityInstance param = h.GetParam<CEntityInstance>(0);
            CTakeDamageInfo param2 = h.GetParam<CTakeDamageInfo>(1);

            if (param == null || param.Entity == null || param2 == null || param2.Attacker == null || param2.Attacker.Value == null)
                return;

            if (string.IsNullOrEmpty(param.Entity.Name)) return;
            if (!param.Entity.Name.StartsWith("FortniteWall")) return;

            var box = param.As<CDynamicProp>();
            if (box == null || !box.IsValid) return;
            box.EmitSound("Wood_Plank.BulletImpact", volume: 1f);

            if (barricades.TryGetValue(box.Index, out int health))
            {
                health -= (int)param2.Damage;
                barricades.AddOrUpdate(box.Index, health, (k, v) => health);
                if (health <= 0) box.AcceptInput("Kill");
            }
            else box.AcceptInput("Kill");
        }

        public class PlayerSkillInfo
        {
            public ulong SteamID { get; set; }
            public bool CanUse { get; set; }
            public DateTime Cooldown { get; set; }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#1b04cc", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float cooldown = 2f, int barricadeHealth = 115, string propModel = "models/props/de_aztec/hr_aztec/aztec_scaffolding/aztec_scaffold_wall_support_128.vmdl") : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission)
        {
            public float Cooldown { get; set; } = cooldown;
            public int BarricadeHealth { get; set; } = barricadeHealth;
            public string PropModel { get; set; } = propModel;
        }
    }
}