using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using src.utils;
using System.Numerics;

namespace src.modules
{
    // Instadefuse by B3none (https://github.com/B3none/cs2-instadefuse, GPLv3), bundled into jRandomSkills.
    // When the last terrorist is dead and no grenade threatens the defuser, the bomb is defused instantly
    // if there would have been enough time, otherwise it blows up straight away.
    public class InstadefuseModule
    {
        private readonly BasePlugin _host;

        private float _bombPlantedTime = float.NaN;
        private bool _bombTicking;
        private int _molotovThreat;
        private int _heThreat;
        private readonly List<int> _infernoThreat = [];

        public InstadefuseModule(BasePlugin host)
        {
            _host = host;
        }

        private string Prefix => _host.Localizer["instadefuse.prefix"];

        public void Load()
        {
            _host.RegisterEventHandler<EventGrenadeThrown>(OnGrenadeThrown);
            _host.RegisterEventHandler<EventInfernoStartburn>(OnInfernoStartBurn);
            _host.RegisterEventHandler<EventInfernoExtinguish>(OnInfernoExtinguish);
            _host.RegisterEventHandler<EventInfernoExpire>(OnInfernoExpire);
            _host.RegisterEventHandler<EventHegrenadeDetonate>(OnHeGrenadeDetonate);
            _host.RegisterEventHandler<EventMolotovDetonate>(OnMolotovDetonate);
            _host.RegisterEventHandler<EventRoundStart>(OnRoundStart);
            _host.RegisterEventHandler<EventBombPlanted>(OnBombPlanted);
            _host.RegisterEventHandler<EventBombBegindefuse>(OnBombBeginDefuse);
        }

        private HookResult OnGrenadeThrown(EventGrenadeThrown @event, GameEventInfo info)
        {
            if (@event.Weapon == "hegrenade")
                _heThreat++;
            else if (@event.Weapon is "incgrenade" or "molotov")
                _molotovThreat++;

            return HookResult.Continue;
        }

        private HookResult OnInfernoStartBurn(EventInfernoStartburn @event, GameEventInfo info)
        {
            var bombOrigin = FindPlantedBomb()?.AbsOrigin;
            if (bombOrigin == null) return HookResult.Continue;

            var distance = Vector3.Distance(new Vector3(@event.X, @event.Y, @event.Z), new Vector3(bombOrigin.X, bombOrigin.Y, bombOrigin.Z));
            if (distance <= Config.LoadedConfig.Modules.Instadefuse.InfernoThreatRadius)
                _infernoThreat.Add(@event.Entityid);

            return HookResult.Continue;
        }

        private HookResult OnInfernoExtinguish(EventInfernoExtinguish @event, GameEventInfo info)
        {
            _infernoThreat.Remove(@event.Entityid);
            return HookResult.Continue;
        }

        private HookResult OnInfernoExpire(EventInfernoExpire @event, GameEventInfo info)
        {
            _infernoThreat.Remove(@event.Entityid);
            return HookResult.Continue;
        }

        private HookResult OnHeGrenadeDetonate(EventHegrenadeDetonate @event, GameEventInfo info)
        {
            if (_heThreat > 0) _heThreat--;
            return HookResult.Continue;
        }

        private HookResult OnMolotovDetonate(EventMolotovDetonate @event, GameEventInfo info)
        {
            if (_molotovThreat > 0) _molotovThreat--;
            return HookResult.Continue;
        }

        private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            _bombPlantedTime = float.NaN;
            _bombTicking = false;
            _heThreat = 0;
            _molotovThreat = 0;
            _infernoThreat.Clear();
            return HookResult.Continue;
        }

        private HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
        {
            _bombPlantedTime = Server.CurrentTime;
            _bombTicking = true;
            return HookResult.Continue;
        }

        private HookResult OnBombBeginDefuse(EventBombBegindefuse @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player != null && player.IsValid && player.PawnIsAlive)
                AttemptInstadefuse(player);

            return HookResult.Continue;
        }

        private void AttemptInstadefuse(CCSPlayerController defuser)
        {
            if (!_bombTicking) return;

            var plantedBomb = FindPlantedBomb();
            if (plantedBomb == null || plantedBomb.CannotBeDefused) return;
            if (TeamHasAlivePlayers(CsTeam.Terrorist)) return;

            if (_heThreat > 0 || _molotovThreat > 0 || _infernoThreat.Count > 0)
            {
                Server.PrintToChatAll($" {Prefix}{_host.Localizer["instadefuse.not_possible"]}");
                return;
            }

            var bombTimeUntilDetonation = plantedBomb.TimerLength - (Server.CurrentTime - _bombPlantedTime);

            var defuseLength = plantedBomb.DefuseLength;
            if (defuseLength != 5 && defuseLength != 10)
                defuseLength = defuser.PawnHasDefuser ? 5.0f : 10.0f;

            var timeLeftAfterDefuse = bombTimeUntilDetonation - defuseLength;

            if (timeLeftAfterDefuse < 0.0f)
            {
                Server.PrintToChatAll($" {Prefix}{_host.Localizer["instadefuse.unsuccessful", defuser.PlayerName, $"{Math.Abs(timeLeftAfterDefuse):n3}"]}");

                Server.NextFrame(() =>
                {
                    var bomb = FindPlantedBomb();
                    if (bomb != null) bomb.C4Blow = 1.0f;
                });
                return;
            }

            Server.NextFrame(() =>
            {
                // Look the bomb up again; holding on to it across frames was crashing in the original plugin.
                var bomb = FindPlantedBomb();
                if (bomb == null) return;

                bomb.DefuseCountDown = 0;
                Server.PrintToChatAll($" {Prefix}{_host.Localizer["instadefuse.successful", defuser.PlayerName, $"{Math.Abs(bombTimeUntilDetonation):n3}"]}");
            });
        }

        private static bool TeamHasAlivePlayers(CsTeam team)
        {
            return Utilities.GetPlayers().Any(p => p.IsValid && p.Team == team && p.PawnIsAlive);
        }

        private static CPlantedC4? FindPlantedBomb()
        {
            return Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault(b => b != null && b.IsValid);
        }
    }
}
