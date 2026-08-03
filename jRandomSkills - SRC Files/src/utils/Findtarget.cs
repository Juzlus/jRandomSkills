using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using static src.jRandomSkills;

namespace src.utils
{
    public class FindTarget
    {
        public static (List<CCSPlayerController> players, string targetname) Find
            (
                CommandInfo command,
                int minArgCount,
                bool singletarget,
                bool ignoreMessage = false
            )
        {
            if (command.ArgCount < minArgCount)
            {
                return (new List<CCSPlayerController>(), string.Empty);
            }

            TargetResult targetresult = command.GetArgTargetResult(1);

            if (targetresult.Players.Count == 0)
            {
                if (!ignoreMessage && command.CallingPlayer != null)
                    command.ReplyToCommand(command.CallingPlayer.GetTranslationWithoutIlliterate("no_player"));

                return (new List<CCSPlayerController>(), string.Empty);
            }
            else if (singletarget && targetresult.Players.Count > 1)
            {
                if (command.CallingPlayer != null)
                    command.ReplyToCommand(command.CallingPlayer.GetTranslationWithoutIlliterate("duplicate_player"));
                return (new List<CCSPlayerController>(), string.Empty);
            }

            string targetname;

            if (targetresult.Players.Count == 1)
            {
                targetname = targetresult.Players.Single().PlayerName;
            }
            else
            {
                Target.TargetTypeMap.TryGetValue(command.GetArg(1), out TargetType type);

                targetname = type switch
                {
                    TargetType.GroupAll => Instance.Localizer["all"],
                    TargetType.GroupBots => Instance.Localizer["bots"],
                    TargetType.GroupHumans => Instance.Localizer["humans"],
                    TargetType.GroupAlive => Instance.Localizer["alive"],
                    TargetType.GroupDead => Instance.Localizer["dead"],
                    TargetType.GroupNotMe => Instance.Localizer["notme"],
                    TargetType.PlayerMe => targetresult.Players.First().PlayerName,
                    TargetType.TeamCt => Instance.Localizer["ct"],
                    TargetType.TeamT => Instance.Localizer["t"],
                    TargetType.TeamSpec => Instance.Localizer["spec"],
                    _ => targetresult.Players.First().PlayerName
                };
            }

            return (targetresult.Players, targetname);
        }
    }
}