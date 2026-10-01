using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using jRandomSkills.src.utils;
using src.utils;
using System.Collections.Concurrent;
using System.Drawing;
using static src.jRandomSkills;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace src.player.skills
{
    // Test skill (not in the draw; admins hand it out with css_setskill): press the use key for an AWP, and
    // every AWP shot becomes a bullet you fly from a camera, steering it with the mouse until it hits.
    public class GuidedBullet : ISkill
    {
        private const Skills skillName = Skills.GuidedBullet;
        private const string cameraModel = "models/sprays/spray_plane.vmdl";
        private static readonly ConcurrentDictionary<uint, PlayerSkillInfo> SkillPlayerInfo = [];

        public static void LoadSkill()
        {
            SkillUtils.RegisterSkill(skillName, SkillsInfo.GetValue<string>(skillName, "color"));
            Instance.AddToManifest(cameraModel);
        }

        public static void NewRound()
        {
            foreach (var info in SkillPlayerInfo.Values)
                EndFlight(info, false);
            SkillPlayerInfo.Clear();
        }

        public static void RoundEnd()
        {
            foreach (var info in SkillPlayerInfo.Values)
                EndFlight(info, false);
        }

        public static void EnableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            SkillPlayerInfo[player.Index] = new PlayerSkillInfo { PlayerIndex = player.Index };
        }

        public static void DisableSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (SkillPlayerInfo.TryRemove(player.Index, out var info))
                EndFlight(info, false);
            SkillUtils.ResetPrintHTML(player);
        }

        public static void PlayerDisconnect(uint playerIndex)
        {
            if (SkillPlayerInfo.TryRemove(playerIndex, out var info))
                EndFlight(info, false);
        }

        public static void PlayerDeath(EventPlayerDeath @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (player == null || !player.IsValid) return;
            if (SkillPlayerInfo.TryGetValue(player.Index, out var info))
                EndFlight(info, false);
        }

        // Use key: hand out the AWP and switch to it.
        public static void UseSkill(CCSPlayerController player)
        {
            if (player == null || !player.IsValid) return;
            if (PlayerManager.GetPlayerByIndex(player.Index)?.HasSkill(skillName) != true) return;
            if (!SkillPlayerInfo.TryGetValue(player.Index, out var info) || info.Camera != null) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.WeaponServices == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;

            bool hasAwp = pawn.WeaponServices.MyWeapons.Any(w => w?.Value?.IsValid == true && w.Value.DesignerName == "weapon_awp");
            if (!hasAwp)
            {
                foreach (var weapon in pawn.WeaponServices.MyWeapons.ToArray())
                {
                    var w = weapon?.Value;
                    if (w == null || !w.IsValid) continue;
                    var vdata = w.GetVData<CCSWeaponBaseVData>();
                    if (vdata?.GearSlot == gear_slot_t.GEAR_SLOT_RIFLE)
                    {
                        pawn.WeaponServices.ActiveWeapon.Raw = w.EntityHandle.Raw;
                        player.DropActiveWeapon();
                    }
                }
                player.GiveNamedItem("weapon_awp");
            }

            player.ExecuteClientCommand("slot1");
            info.Armed = true;
            player.PrintToChat($" {ChatColors.Lime}{player.GetTranslation("guidedbullet_ready")}");
        }

        public static void WeaponFire(EventWeaponFire @event)
        {
            var player = PlayerManager.GetPlayerEvent(@event.Userid);
            if (!Instance.IsPlayerValid(player) || @event.Weapon != "weapon_awp") return;
            if (PlayerManager.GetPlayerByIndex(player!.Index)?.HasSkill(skillName) != true) return;
            if (!SkillPlayerInfo.TryGetValue(player.Index, out var info) || info.Camera != null) return;
            if (Server.TickCount < info.NextShotTick) return;

            // The real bullet leaves the barrel this tick; its damage is swallowed in OnTakeDamage.
            info.SuppressUntilTick = Server.TickCount + 2;
            StartFlight(player, info);
        }

        public static void OnTakeDamage(CBaseEntity damagedEntity, CTakeDamageInfo damageInfo)
        {
            if (damageInfo == null || damageInfo.Handle == nint.Zero || !SkillUtils.IsBulletDamage(damageInfo)) return;
            if (damageInfo.Ability?.Value?.DesignerName != "weapon_awp") return;

            var attackerEnt = damageInfo.Attacker?.Value;
            if (attackerEnt == null || !attackerEnt.IsValid) return;
            var attackerPawn = attackerEnt.As<CCSPlayerPawn>();
            if (attackerPawn == null || !attackerPawn.IsValid || attackerPawn.DesignerName != "player") return;
            var controller = attackerPawn.Controller?.Value;
            if (controller == null || !controller.IsValid) return;

            var attacker = PlayerManager.GetPlayerEvent(controller.As<CCSPlayerController>());
            if (attacker == null || !SkillPlayerInfo.TryGetValue(attacker.Index, out var info)) return;

            if (Server.TickCount <= info.SuppressUntilTick)
                damageInfo.Damage = 0;
        }

        private static void StartFlight(CCSPlayerController player, PlayerSkillInfo info)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null || pawn.CameraServices == null) return;

            var camera = EntityManager.CreateTrackedDynamicProp(player.Index);
            if (camera == null || !camera.IsValid) return;

            Vector eye = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
            QAngle angles = new(pawn.EyeAngles.X, pawn.EyeAngles.Y, 0);
            Vector start = eye + SkillUtils.GetForwardVector(angles) * 40f;

            var camNode = camera.CBodyComponent?.SceneNode?.Owner?.Entity;
            if (camNode != null)
                camNode.Flags = (uint)(camNode.Flags & ~(1 << 2));

            camera.SetModel(cameraModel);
            camera.Teleport(start, angles);
            camera.DispatchSpawn();
            camera.Render = Color.FromArgb(1, 255, 255, 255);
            Utilities.SetStateChanged(camera, "CBaseModelEntity", "m_clrRender");

            info.OriginalView = pawn.CameraServices.ViewEntity.Raw;
            info.Camera = camera.Index;
            info.Position = start;
            info.Direction = SkillUtils.GetForwardVector(angles);
            info.StartTick = Server.TickCount;

            pawn.CameraServices.ViewEntity.Raw = camera.EntityHandle.Raw;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");

            Freeze(pawn, true);
            BlockWeapon(player, true);
        }

        public static void OnTick()
        {
            if (SkillPlayerInfo.IsEmpty) return;

            float speed = SkillsInfo.GetValue<float>(skillName, "speed") / 64f;
            float turn = Math.Clamp(SkillsInfo.GetValue<float>(skillName, "turnRate"), 0.01f, 1f);
            float hitRadius = SkillsInfo.GetValue<float>(skillName, "hitRadius");
            int maxTicks = (int)(SkillsInfo.GetValue<float>(skillName, "maxFlightTime") * 64);

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
                            int left = (int)Math.Ceiling((info.NextShotTick - Server.TickCount) / 64f);
                            pi.PrintHTML = left > 0 ? $"{player.GetTranslation("hud_info", $"<font color='#FF0000'>{left}</font>")}" : null;
                        }
                    }
                    continue;
                }

                var pawn = player.PlayerPawn.Value;
                var camera = Utilities.GetEntityFromIndex<CDynamicProp>((int)info.Camera);
                if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || camera == null || !camera.IsValid)
                {
                    EndFlight(info, false);
                    continue;
                }

                if (Server.TickCount - info.StartTick > maxTicks)
                {
                    EndFlight(info, true);
                    continue;
                }

                // Steer towards where the player looks; the mouse keeps turning the pawn while the view is on the camera.
                Vector look = SkillUtils.GetForwardVector(pawn.EyeAngles);
                Vector dir = new(
                    info.Direction.X + (look.X - info.Direction.X) * turn,
                    info.Direction.Y + (look.Y - info.Direction.Y) * turn,
                    info.Direction.Z + (look.Z - info.Direction.Z) * turn);
                float len = dir.Length();
                if (len > 0.0001f) dir = new Vector(dir.X / len, dir.Y / len, dir.Z / len);
                info.Direction = dir;

                Vector next = info.Position + dir * speed;

                var hit = FindHitPlayer(player, info.Position, next, hitRadius);
                if (hit != null)
                {
                    int damage = SkillsInfo.GetValue<int>(skillName, "damage");
                    SkillUtils.TakeHealth(hit.PlayerPawn.Value, damage, player, KillfeedIcons.AWP);
                    player.PrintToChat($" {ChatColors.Lime}{player.GetTranslation("guidedbullet_hit", hit.PlayerName)}");
                    EndFlight(info, true);
                    continue;
                }

                var trace = RayTrace.TraceShape(player, info.Position, next);
                if (trace.HasValue && trace.Value.DidHit)
                {
                    pawn.EmitSound("SolidMetal.BulletImpact", volume: 1f);
                    EndFlight(info, true);
                    continue;
                }

                info.Position = next;
                camera.Teleport(next, new QAngle(pawn.EyeAngles.X, pawn.EyeAngles.Y, 0));

                if (SkillUtils.IsHudFrame())
                {
                    var pi = PlayerManager.GetPlayerByIndex(player.Index);
                    if (pi != null)
                    {
                        float left = (maxTicks - (Server.TickCount - info.StartTick)) / 64f;
                        pi.PrintHTML = $"{player.GetTranslation("guidedbullet_flying", $"<font color='#00FF00'>{Math.Max(0, left):0.0}s</font>")}";
                    }
                }
            }
        }

        // The first living player (shooter and teammates excluded) within hitRadius of the segment travelled this tick.
        private static CCSPlayerController? FindHitPlayer(CCSPlayerController shooter, Vector from, Vector to, float radius)
        {
            Vector seg = to - from;
            float segLen2 = seg.LengthSqr();

            foreach (var other in PlayerManager.GetTickPlayers())
            {
                if (!Instance.IsPlayerValid(other) || other.Index == shooter.Index || other.Team == shooter.Team || !other.PawnIsAlive) continue;
                var pawn = other.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) continue;

                Vector center = new(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + 36f);
                float t = 0f;
                if (segLen2 > 0.0001f)
                {
                    Vector rel = center - from;
                    t = Math.Clamp((rel.X * seg.X + rel.Y * seg.Y + rel.Z * seg.Z) / segLen2, 0f, 1f);
                }
                Vector closest = from + seg * t;
                if ((center - closest).Length() <= radius)
                    return other;
            }

            return null;
        }

        private static void EndFlight(PlayerSkillInfo info, bool startCooldown)
        {
            var player = Utilities.GetPlayerFromIndex((int)info.PlayerIndex);

            if (info.Camera != null)
            {
                var camera = Utilities.GetEntityFromIndex<CDynamicProp>((int)info.Camera);
                if (camera != null && camera.IsValid)
                    EntityManager.DestroyEntity(camera.Index);
                info.Camera = null;
            }

            if (player != null && player.IsValid)
            {
                var pawn = player.PlayerPawn.Value;
                if (pawn != null && pawn.IsValid)
                {
                    if (pawn.CameraServices != null && info.OriginalView != 0)
                    {
                        pawn.CameraServices.ViewEntity.Raw = info.OriginalView;
                        Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
                    }
                    Freeze(pawn, false);
                }
                BlockWeapon(player, false);
                SkillUtils.ResetPrintHTML(player);
            }

            info.OriginalView = 0;
            if (startCooldown)
                info.NextShotTick = Server.TickCount + (int)(SkillsInfo.GetValue<float>(skillName, "cooldown") * 64);
        }

        private static void Freeze(CCSPlayerPawn pawn, bool freeze)
        {
            pawn.MoveType = freeze ? MoveType_t.MOVETYPE_NONE : MoveType_t.MOVETYPE_WALK;
            Schema.SetSchemaValue(pawn.Handle, "CBaseEntity", "m_nActualMoveType", freeze ? 0 : 2);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
            if (freeze)
                pawn.Teleport(null, null, new Vector(0, 0, 0));
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

        public class PlayerSkillInfo
        {
            public uint PlayerIndex { get; set; }
            public bool Armed { get; set; }
            public uint? Camera { get; set; }
            public uint OriginalView { get; set; }
            public Vector Position { get; set; } = new(0, 0, 0);
            public Vector Direction { get; set; } = new(1, 0, 0);
            public int StartTick { get; set; }
            public int NextShotTick { get; set; }
            public int SuppressUntilTick { get; set; } = -1;
        }

        public class SkillConfig(Skills skill = skillName, bool active = false, string color = "#ff4fd8", CsTeam onlyTeam = CsTeam.None, bool disableOnFreezeTime = true, bool needsTeammates = false, string requiredPermission = "", float? hudDuration = null, float? descriptionHudDuration = null, int maxPerServer = -1, Rarity rarity = Rarity.Legendary, float speed = 900f, float turnRate = .25f, float hitRadius = 40f, float maxFlightTime = 6f, int damage = 150, float cooldown = 4f) : SkillsInfo.DefaultSkillInfo(skill, active, color, onlyTeam, disableOnFreezeTime, needsTeammates, requiredPermission, hudDuration, descriptionHudDuration, maxPerServer, rarity)
        {
            // Bullet speed in units per second, how fast it bends towards the crosshair (0-1 per tick),
            // how close it has to pass to a player to count as a hit, and how long it can fly.
            public float Speed { get; set; } = speed;
            public float TurnRate { get; set; } = turnRate;
            public float HitRadius { get; set; } = hitRadius;
            public float MaxFlightTime { get; set; } = maxFlightTime;
            public int Damage { get; set; } = damage;
            public float Cooldown { get; set; } = cooldown;
        }
    }
}
