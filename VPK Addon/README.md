# jRandomSkills Panorama HUD (Workshop)

An optional skill HUD drawn with Panorama (`custom_hud_layout`) instead of the center HTML message. It shows the skill name in its color, the description, the skill's info line and a red cooldown bar. Curses on you are listed under the radar, and the WASD menus (skill target menus, `!skills`, `!lang`) are drawn in the same style. It has no animations.

The layout must reach the players through a Workshop addon, so you upload these files to your own Workshop item.

## Files

```
panorama/layout/jrandomskills/simple_skills.xml   layout
panorama/styles/jrandomskills/simple_skills.css   styles (generated)
tools/build_css.py                           regenerates the CSS
```

## Setup

1. Copy the `panorama` folder into your addon's content folder: `Counter-Strike Global Offensive/content/csgo_addons/<your_addon>/panorama`.
2. Open the CS2 Workshop Tools, build the addon and upload it to the Workshop.
3. Make the server mount the addon so that clients download it, for example with MultiAddonManager.
4. Enable the HUD in `configs/config.json`:

```json
"PanoramaHud": {
  "Enabled": true,
  "Layout": "panorama/layout/custom_game/jrandomskills/simple_skills.xml",
  "Font": "stratum",
  "FontSize": 100
}
```

| Key | Default | Description |
|---|---|---|
| `Enabled` | `false` | Use the Panorama HUD instead of the center HTML HUD. |
| `Layout` | `panorama/layout/jrandomskills/simple_skills.xml` | Layout path inside your addon. Change it if you rename the files. |
| `Font` | `stratum` | `stratum`, `light`, `cond`, `boldcond`, `mono`, `noto`, `serif` or `notomono`. |
| `FontSize` | `100` | Text size in percent, from `80` to `130` in steps of `5`. |

## Skill colors

Panorama cannot take a color from the server, so every skill color needs its own CSS class. The shipped CSS covers the default `skillsInfo.json`. If you change skill colors, regenerate the CSS and upload the addon again:

```
python tools/build_css.py panorama/styles/jrandomskills/simple_skills.css <path to skillsInfo.json> <path to the plugin source folder (optional)>
```
