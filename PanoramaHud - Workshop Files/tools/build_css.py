# usage: python build_css.py <output.css> <skillsInfo.json | source dir>...
import json
import os
import re
import sys

HEX = re.compile(r'^#[0-9a-fA-F]{6}$')
QUOTED = re.compile(r'["\'](#[0-9a-fA-F]{6})["\']')
EXTRA = ['#ffffff', '#00ffff', '#008000', '#ffff00', '#ff0000', '#00ff00']

FONTS = {
    'stratum': 'Stratum2',
    'light': 'Stratum2 Light',
    'cond': 'Stratum2 Condensed',
    'boldcond': 'Stratum2 Bold Condensed',
    'mono': 'Stratum2 Mono',
    'noto': 'Noto Sans',
    'serif': 'Noto Serif',
    'notomono': 'Noto Mono',
}
SIZES = {
    '.js-info': 13,
    '.js-name': 26,
    '.js-seg': 16,
    '.js-card-name': 17,
    '.js-card-info': 14,
    '.js-menu-title': 21,
    '.js-menu-info': 13,
    '.js-row-text': 15,
    '.js-fseg': 12,
}

BASE = '''@define panel-bg: rgba(9, 9, 11, 0.9);
@define text: #fafafa;
@define muted: #9f9fa9;
@define soft: #d4d4d8;
@define cooldown: #e5383b;
@define selected: rgba(255, 255, 255, 0.07);

.js-root,
.js-scope
{
	width: 100%;
	height: 100%;
}

.js-off
{
	visibility: collapse;
}

.js-skill
{
	horizontal-align: center;
	vertical-align: bottom;
	margin-bottom: 16px;
	width: 300px;
	height: fit-children;
	flow-children: down;
	background-color: panel-bg;
	border-radius: 2px;
}

.js-scope.is-spec .js-skill,
.js-scope.is-spec .js-menu
{
	margin-bottom: 86px;
}

.js-scope.is-spec.is-other .js-skill
{
	visibility: collapse;
}

.js-body
{
	width: 100%;
	height: fit-children;
	padding: 5px 12px 8px 12px;
	flow-children: down;
}

.js-info
{
	horizontal-align: center;
	text-align: center;
	font-family: Stratum2, "Arial Unicode MS";
	font-weight: bold;
	font-size: 13px;
	letter-spacing: 1px;
	text-transform: uppercase;
	color: muted;
}

.js-name
{
	horizontal-align: center;
	text-align: center;
	width: 100%;
	font-family: Stratum2, "Arial Unicode MS";
	font-weight: bold;
	font-size: 26px;
	color: text;
	white-space: nowrap;
	text-overflow: ellipsis;
}

.js-desc,
.js-status
{
	horizontal-align: center;
	width: fit-children;
	max-width: 100%;
	height: fit-children;
	margin-top: 1px;
	flow-children: right-wrap;
}

.js-desc
{
	max-height: 66px;
}

.js-seg
{
	max-width: 276px;
	text-align: center;
	font-family: Stratum2, "Arial Unicode MS";
	font-weight: bold;
	font-size: 16px;
	color: text;
}

.js-skill.no-info .js-info,
.js-skill.no-desc .js-desc,
.js-skill.no-status .js-status
{
	visibility: collapse;
}

.js-cd
{
	horizontal-align: left;
	width: 0%;
	height: 3px;
	background-color: cooldown;
	visibility: collapse;
}

.js-skill.has-cd .js-cd
{
	visibility: visible;
}

.js-side
{
	horizontal-align: left;
	vertical-align: top;
	margin-left: 12px;
	margin-top: 452px;
	width: 360px;
	height: fit-children;
	flow-children: down;
}

.js-card
{
	width: fit-children;
	max-width: 360px;
	height: fit-children;
	margin-bottom: 6px;
	padding: 6px 10px 5px 10px;
	flow-children: right;
	background-color: panel-bg;
	border-radius: 2px;
}

.js-card-name
{
	vertical-align: middle;
	font-family: Stratum2, "Arial Unicode MS";
	font-weight: bold;
	font-size: 17px;
	color: text;
	white-space: nowrap;
	text-overflow: ellipsis;
}

.js-card-info
{
	vertical-align: middle;
	max-width: 240px;
	margin-left: 8px;
	font-family: Stratum2, "Arial Unicode MS";
	font-size: 14px;
	color: soft;
}

.js-card.no-info .js-card-info
{
	visibility: collapse;
}

.js-menu
{
	horizontal-align: center;
	vertical-align: bottom;
	margin-bottom: 16px;
	width: 300px;
	height: fit-children;
	padding-bottom: 6px;
	flow-children: down;
	background-color: panel-bg;
	border-radius: 2px;
}

.js-menu-title
{
	horizontal-align: center;
	text-align: center;
	width: 100%;
	margin-top: 6px;
	font-family: Stratum2, "Arial Unicode MS";
	font-weight: bold;
	font-size: 21px;
	color: text;
	white-space: nowrap;
	text-overflow: ellipsis;
}

.js-menu-info
{
	horizontal-align: center;
	text-align: center;
	width: 100%;
	margin-bottom: 4px;
	font-family: Stratum2, "Arial Unicode MS";
	font-size: 13px;
	color: muted;
}

.js-menu.no-info .js-menu-info
{
	visibility: collapse;
}

.js-row
{
	width: 100%;
	height: 27px;
	flow-children: right;
}

.js-row-bar
{
	width: 3px;
	height: 100%;
}

.js-row-text
{
	vertical-align: middle;
	width: fill-parent-flow( 1.0 );
	margin-left: 14px;
	margin-right: 14px;
	font-family: Stratum2, "Arial Unicode MS";
	font-size: 15px;
	color: soft;
	white-space: nowrap;
	text-overflow: ellipsis;
}

.js-row.is-sel
{
	background-color: selected;
}

.js-row.is-sel .js-row-bar
{
	background-color: cooldown;
}

.js-row.is-sel .js-row-text
{
	font-weight: bold;
	color: text;
}

.js-menu-foot
{
	horizontal-align: center;
	width: fit-children;
	height: fit-children;
	margin-top: 4px;
	flow-children: right;
}

.js-fseg
{
	margin: 0px 3px;
	font-family: Stratum2, "Arial Unicode MS";
	font-weight: bold;
	font-size: 12px;
	letter-spacing: 1px;
	text-transform: uppercase;
	color: muted;
}
'''


def colors(paths):
    found = set(EXTRA)
    for path in paths:
        if os.path.isdir(path):
            for folder, _, files in os.walk(path):
                for name in files:
                    if name.endswith(('.cs', '.json')):
                        for line in open(os.path.join(folder, name), encoding='utf-8-sig', errors='ignore'):
                            if 'class SkillConfig' not in line:
                                found.update(c.lower() for c in QUOTED.findall(line))
            continue
        for skill in json.load(open(path, encoding='utf-8-sig')):
            color = str(skill.get('Color', '')).strip()
            if HEX.match(color):
                found.add(color.lower())
    return sorted(found)


def build(found):
    out = [BASE]
    for name, family in FONTS.items():
        selectors = ',\n'.join(f'.js-scope.ff-{name} {element}' for element in SIZES)
        out.append(f'{selectors}\n{{\n\tfont-family: {family}, "Arial Unicode MS";\n}}\n')
    for size in range(80, 135, 5):
        if size == 100:
            continue
        for element, base in SIZES.items():
            out.append(f'.js-scope.fs-{size} {element}\n{{\n\tfont-size: {round(base * size / 100)}px;\n}}\n')
    for color in found:
        cls = 'sc-' + color[1:]
        out.append(f'.js-skill.{cls} .js-name,\n.js-seg.{cls},\n.js-card.{cls} .js-card-name,\n.js-menu.{cls} .js-menu-title,\n.js-row.{cls} .js-row-text\n{{\n\tcolor: {color};\n}}\n')
    for pct in range(101):
        out.append(f'.js-skill.cd-{pct} .js-cd\n{{\n\twidth: {pct}%;\n}}\n')
    return '\n'.join(out)


output, sources = sys.argv[1], sys.argv[2:]
found = colors(sources)
open(output, 'w', encoding='utf-8', newline='\n').write(build(found))
print(output, len(found), 'colors')
