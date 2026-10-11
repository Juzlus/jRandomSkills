using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    // Press the use key: you stand still and look down from a bird's-eye camera. WASD flies the camera, the
    // attack key drops a bomb on the spot under it: a smoke most of the time, sometimes an HE, rarely a molotov.
    // Press the use key again (or run out of bombs / time) to come back.
    public class AirBombs : ISkill
    {
        private const Skills skillName = Skills.AirBombs;
        private const string cameraModel = "models/sprays/spray_plane.vmdl";
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];

        private class PlayerSkillInfo
        {
            public uint PlayerIndex;
            public uint? Camera;
            public uint OriginalView;
            public Vector CameraPos = new();
            public Vector StandOrigin = new();
            public int StartTick;
            public int BombsLeft;
            public int NextDropTick;
            public DateTime Cooldown = DateTime.MinValue;
            public PlayerButtons LastButtons;
        }

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(cameraModel);
        }

        public static void NewRound()
        {
            foreach (var info in SkillPlayerInfo.Values) EndView(info, false);
            SkillPlayerInfo.Clear();
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillPlayerInfo[player.Index] = new PlayerSkillInfo { PlayerIndex = player.Index, BombsLeft = SkillsInfo.GetValue<int>(skillName, "bombsPerUse") };
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (SkillPlayerInfo.TryRemove(player.Index, out var info)) EndView(info, false);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            if (SkillPlayerInfo.TryRemove(playerIndex, out var info)) EndView(info, false);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var victim = PlayerManager.GetPlayerEvent(@event.Userid);
            if (victim != null && victim.IsValid && SkillPlayerInfo.TryGetValue(victim.Index, out var info))
                EndView(info, false);
        }

        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (!SkillPlayerInfo.TryGetValue(player.Index, out var info)) return;

            if (info.Camera != null) { EndView(info, true); return; }   // back down
            if (info.Cooldown.AddSeconds(SkillsInfo.GetValue<float>(skillName, "cooldown")) > DateTime.Now) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.CameraServices == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            if ((pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) == 0) return;

            var camera = EntityManager.CreateTrackedDynamicProp(player.Index);
            if (camera == null || !camera.IsValid) return;

            // As high as the ceiling allows.
            float height = SkillsInfo.GetValue<float>(skillName, "height");
            Vector origin = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z);
            Vector top = new(origin.X, origin.Y, origin.Z + height);
            var up = RayTrace.TraceShape(player, new Vector(origin.X, origin.Y, origin.Z + 40f), top);
            if (up.HasValue && up.Value.DidHit)
                top = new Vector(origin.X, origin.Y, Math.Max(origin.Z + 80f, up.Value.EndPosZ - 24f));

            var camNode = camera.CBodyComponent?.SceneNode?.Owner?.Entity;
            if (camNode != null) camNode.Flags = (uint)(camNode.Flags & ~(1 << 2));
            camera.SetModel(cameraModel);
            camera.Teleport(top, new QAngle(89f, pawn.V_angle.Y, 0));
            camera.DispatchSpawn();
            camera.Render = Color.FromArgb(1, 255, 255, 255);
            Utilities.SetStateChanged(camera, "CBaseModelEntity", "m_clrRender");

            info.OriginalView = pawn.CameraServices.ViewEntity.Raw;
            info.Camera = camera.Index;
            info.CameraPos = top;
            info.StandOrigin = origin;
            info.StartTick = Server.TickCount;
            info.BombsLeft = SkillsInfo.GetValue<int>(skillName, "bombsPerUse");
            info.NextDropTick = Server.TickCount + 16;
            info.LastButtons = player.Buttons;

            pawn.CameraServices.ViewEntity.Raw = camera.EntityHandle.Raw;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
            BlockWeapon(player, true);
            player.PrintToChat($" {ChatColors.Lime}{player.GetTranslation("airbombs_start", info.BombsLeft)}");
        }

        public static void OnTick()
        {
            if (SkillPlayerInfo.IsEmpty) return;

            float speed = SkillsInfo.GetValue<float>(skillName, "cameraSpeed") / 64f;
            float range = SkillsInfo.GetValue<float>(skillName, "range");
            int maxTicks = (int)(SkillsInfo.GetValue<float>(skillName, "maxSeconds") * 64);
            float cooldown = SkillsInfo.GetValue<float>(skillName, "cooldown");

            foreach (var info in SkillPlayerInfo.Values)
            {
                var player = Utilities.GetPlayerFromIndex((int)info.PlayerIndex);
                if (player == null || !player.IsValid) continue;

                if (info.Camera == null)
                {
                    if (SkillUtils.IsHudFrame())
                    {
                        var pi = PlayerManager.GetPlayerByIndex(player.Index);
                        if (pi != null)
                        {
                            int left = (int)Math.Ceiling((info.Cooldown.AddSeconds(cooldown) - DateTime.Now).TotalSeconds);
                            pi.PrintHTML = left > 0 ? $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{left}</font>")}" : null;
                        }
                    }
                    continue;
                }

                var pawn = player.PlayerPawn.Value;
                var camera = Utilities.GetEntityFromIndex<CDynamicProp>((int)info.Camera);
                if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || camera == null || !camera.IsValid)
                {
                    EndView(info, false);
                    continue;
                }
                if (Server.TickCount - info.StartTick > maxTicks || info.BombsLeft <= 0 && Server.TickCount > info.NextDropTick + 32)
                {
                    EndView(info, true);
                    continue;
                }

                // The player stays put; the keys move the camera instead.
                if (pawn.AbsOrigin != null && SkillUtils.GetDistance(pawn.AbsOrigin, info.StandOrigin) > 1.0)
                    pawn.Teleport(info.StandOrigin, null, new Vector(0, 0, 0));
                else
                    pawn.Teleport(null, null, new Vector(0, 0, 0));

                var buttons = player.Buttons;
                float f = (buttons.HasFlag(PlayerButtons.Forward) ? 1f : 0f) - (buttons.HasFlag(PlayerButtons.Back) ? 1f : 0f);
                float r = (buttons.HasFlag(PlayerButtons.Moveright) ? 1f : 0f) - (buttons.HasFlag(PlayerButtons.Moveleft) ? 1f : 0f);
                float yaw = pawn.V_angle.Y * MathF.PI / 180f;
                (float sy, float cy) = MathF.SinCos(yaw);
                float dx = (cy * f + sy * r) * speed, dy = (sy * f - cy * r) * speed;

                Vector next = new(info.CameraPos.X + dx, info.CameraPos.Y + dy, info.CameraPos.Z);
                if (SkillUtils.GetDistance(new Vector(next.X, next.Y, info.StandOrigin.Z), info.StandOrigin) <= range)
                {
                    // Stay out of walls.
                    var t = RayTrace.TraceShape(player, info.CameraPos, next);
                    if (!(t.HasValue && t.Value.DidHit)) info.CameraPos = next;
                }
                camera.Teleport(info.CameraPos, new QAngle(89f, pawn.V_angle.Y, 0));

                if (SkillUtils.IsHudFrame())
                {
                    var pi = PlayerManager.GetPlayerByIndex(player.Index);
                    if (pi != null) pi.PrintHTML = $"<font color='#FFD700'>{player.GetTranslation("airbombs_hud", info.BombsLeft)}</font>";
                }

                // A fresh attack press drops one bomb.
                bool attack = buttons.HasFlag(PlayerButtons.Attack) && !info.LastButtons.HasFlag(PlayerButtons.Attack);
                info.LastButtons = buttons;
                if (attack && info.BombsLeft > 0 && Server.TickCount >= info.NextDropTick)
                {
                    info.BombsLeft--;
                    info.NextDropTick = Server.TickCount + (int)(SkillsInfo.GetValue<float>(skillName, "dropInterval") * 64);
                    Drop(player, pawn, info.CameraPos);
                }
            }
        }

        private static void Drop(CCSPlayerController player, CCSPlayerPawn pawn, Vector from)
        {
            // Where the bomb lands: straight down from the camera.
            Vector ground = new(from.X, from.Y, from.Z - 60f);
            var down = RayTrace.TraceShape(player, from, new Vector(from.X, from.Y, from.Z - 6000f));
            if (down.HasValue && down.Value.DidHit)
                ground = new Vector(down.Value.EndPosX, down.Value.EndPosY, down.Value.EndPosZ);

            float wSmoke = SkillsInfo.GetValue<float>(skillName, "weightSmoke");
            float wHe = SkillsInfo.GetValue<float>(skillName, "weightHe");
            float wMolotov = SkillsInfo.GetValue<float>(skillName, "weightMolotov");
            double roll = Instance.Random.NextDouble() * (wSmoke + wHe + wMolotov);
            string kind = roll < wSmoke ? "smoke" : roll < wSmoke + wHe ? "he" : "molotov";

            Vector spawn = new(ground.X, ground.Y, ground.Z + 48f);
            Vector fall = new(0, 0, -200f);
            int team = pawn.TeamNum;

            switch (kind)
            {
                case "smoke":
                    SkillUtils.CreateSmokeGrenadeProjectile(spawn, QAngle.Zero, fall, team);
                    break;
                case "molotov":
                    SkillUtils.CreateMolotovProjectile(spawn, QAngle.Zero, fall, team);
                    break;
                default:
                    // The HE goes off where it lands, a moment after the drop.
                    float damage = SkillsInfo.GetValue<float>(skillName, "heDamage");
                    float radius = SkillsInfo.GetValue<float>(skillName, "heRadius");
                    var owner = pawn;
                    Instance.AddTimer(0.6f, () =>
                    {
                        if (owner == null || !owner.IsValid) return;
                        SkillUtils.CreateExplosion(new Vector(ground.X, ground.Y, ground.Z + 16f), damage, radius, owner);
                    }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                    break;
            }

            player.PrintToChat($" {ChatColors.Lime}{player.GetTranslation("airbombs_dropped_" + kind)}");
            SkillUtils.EmitSoundToPlayer(player, "UIPanorama.buttonclick", SkillsInfo.GetValue<float>(skillName, "soundVolume"));
        }

        private static void EndView(PlayerSkillInfo info, bool startCooldown)
        {
            var player = Utilities.GetPlayerFromIndex((int)info.PlayerIndex);

            if (info.Camera != null)
            {
                var camera = Utilities.GetEntityFromIndex<CDynamicProp>((int)info.Camera);
                if (camera != null && camera.IsValid) EntityManager.DestroyEntity(camera.Index);
                info.Camera = null;
            }

            if (player != null && player.IsValid)
            {
                var pawn = player.PlayerPawn.Value;
                if (pawn != null && pawn.IsValid && pawn.CameraServices != null && info.OriginalView != 0)
                {
                    pawn.CameraServices.ViewEntity.Raw = info.OriginalView;
                    Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
                }
                BlockWeapon(player, false);
                SkillUtils.ResetPrintHTML(player);
            }

            info.OriginalView = 0;
            if (startCooldown) info.Cooldown = DateTime.Now;
        }

        private static void BlockWeapon(CCSPlayerController player, bool block)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null) return;
            foreach (var weapon in pawn.WeaponServices.MyWeapons)
            {
                var w = weapon?.Value;
                if (w == null || !w.IsValid) continue;
                w.NextPrimaryAttackTick = block ? int.MaxValue : Server.TickCount;
                w.NextSecondaryAttackTick = block ? int.MaxValue : Server.TickCount;
                Utilities.SetStateChanged(w, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
                Utilities.SetStateChanged(w, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
            }
        }

        public class SkillConfig(Skills skill = skillName, bool active = true, string color = "#6fa8dc", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Epic, int bombsPerUse = 3, float cooldown = 25f, float height = 700f, float cameraSpeed = 700f, float range = 2500f, float maxSeconds = 20f, float dropInterval = 1f, float weightSmoke = 6f, float weightHe = 3f, float weightMolotov = 1f, float heDamage = 90f, float heRadius = 350f, float soundVolume = .5f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            public int BombsPerUse { get; set; } = bombsPerUse;
            public float Cooldown { get; set; } = cooldown;
            public float Height { get; set; } = height;
            public float CameraSpeed { get; set; } = cameraSpeed;
            public float Range { get; set; } = range;
            public float MaxSeconds { get; set; } = maxSeconds;
            public float DropInterval { get; set; } = dropInterval;
            public float WeightSmoke { get; set; } = weightSmoke;
            public float WeightHe { get; set; } = weightHe;
            public float WeightMolotov { get; set; } = weightMolotov;
            public float HeDamage { get; set; } = heDamage;
            public float HeRadius { get; set; } = heRadius;
            public float SoundVolume { get; set; } = soundVolume;
        }
    }
}
