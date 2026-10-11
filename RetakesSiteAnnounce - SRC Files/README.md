# RetakesSiteAnnounce

Standalone [CounterStrikeSharp](https://docs.cssharp.dev/) plugin for CS2 retakes servers: when a round starts it
shows **SITE A** / **SITE B** as text in front of each CT's eyes for a few seconds, right as they spawn.

It is **not a HUD message**. The text is a `point_worldtext` entity kept in front of the player's view (the
[CS2-GameHUD](https://github.com/darkerz7/CS2-GameHUD) technique), one entity per player for the whole map, sent only
to its owner. Nothing is re-sent to the clients every tick, so it adds no HUD traffic and it never touches the centre
message slot other plugins use. The text follows the view, so it stays on screen no matter where the player looks.

## How it knows the site

1. **Retakes event API** (preferred): the plugin hooks the `retakes_plugin:event_sender` capability from
   `RetakesPluginShared` and reads the `AnnounceBombsiteEvent` that [cs2-retakes](https://github.com/B3none/cs2-retakes)
   (or the retakes module bundled in jRandomSkills) fires at round start. If the retakes plugin loads later, the hook is
   retried every round.
2. **Fallback** (`FallbackDetection`): without that event, the planted bomb tells the site (`planted_c4` present at
   round start, or the `bomb_planted` event).

The call is shown once per round to everyone who should get it; a player whose pawn spawns a moment later gets it on
spawn for what is left of the window.

## Install

Needs the `RetakesPluginShared` API in `addons/counterstrikesharp/shared/RetakesPluginShared/` (shipped in this
repository's server files and with cs2-retakes).

1. Copy `plugins/RetakesSiteAnnounce/` into `addons/counterstrikesharp/plugins/`.
2. Optionally copy `configs/plugins/RetakesSiteAnnounce/RetakesSiteAnnounce.json`; CounterStrikeSharp writes it with
   the defaults on first load otherwise.
3. Nothing else: the jRandomSkills retakes module sees this plugin (it registers the `retakes_site_announce_version`
   convar) and keeps its own site banner off while it is loaded, so the call is never shown twice. Its chat line and
   agent voice line stay on.

## Config (`configs/plugins/RetakesSiteAnnounce/RetakesSiteAnnounce.json`)

| Key | Default | Meaning |
| --- | --- | --- |
| `Seconds` | `5` | How long the text stays after the player spawns. |
| `CtOnly` | `true` | Only CTs get the call (the Ts spawn on the site). |
| `IncludeSpectators` | `false` | Also show it to spectators. |
| `TextA` / `TextB` | `SITE A` / `SITE B` | Text per site; `\n` starts a new line. |
| `ColorA` / `ColorB` | `#FF5050` / `#50A0FF` | Text colour per site. |
| `Method` | `Pawn` | `Pawn`: re-aimed from the view angles every tick while visible (GameHUD default). `Orient`: rides a `point_orient` that turns with the eyes on the client, nothing per tick. |
| `PositionX` / `PositionY` / `PositionZ` | `0` / `40` / `80` | Placement in front of the eyes: right, up, distance (GameHUD / InfoTop convention). |
| `FontSize` / `UnitsPerPixel` / `Font` | `40` / `0.25` / `Arial Bold` | Text size and font. |
| `BackgroundBorder` | `0.5` | Dark box behind the text; `0` for none. |
| `AnnounceOnPlant` | `false` | Show the call again when the bomb is planted. |
| `FallbackDetection` | `true` | Read the site from the planted bomb when no retakes event arrives. |
| `TestCommandPermission` | `@css/root` | Who may use `!sitetest`. |

## Commands

- `!sitetest [A|B]` (`css_sitetest`): shows the banner to you for tuning the placement.

## Building

```
dotnet build "RetakesSiteAnnounce - SRC Files/RetakesSiteAnnounce.csproj" -c Release
```

The build copies `RetakesSiteAnnounce.dll` to `jRandomSkills - Server Files/plugins/RetakesSiteAnnounce/`.
`RetakesPluginShared` is referenced with `Private=false`: it is loaded from the shared folder at runtime.
