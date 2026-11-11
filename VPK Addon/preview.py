from __future__ import annotations

import argparse
import html
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import urllib.parse
import xml.etree.ElementTree as ET
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple


DIV_TAGS = {
    "Panel", "Frame", "Movie", "ProgressBar", "ToggleButton",
    "RadioButton", "DropDown", "Carousel", "ContextMenu",
}
IMAGE_SUFFIXES = (".png", ".jpg", ".jpeg", ".tga", ".bmp", ".webp", ".tif", ".tiff")
FALLBACK_TEXTURE = (
    "data:image/svg+xml,"
    + urllib.parse.quote(
        '<svg xmlns="http://www.w3.org/2000/svg" width="256" height="144">'
        '<rect width="100%" height="100%" fill="#292f38"/>'
        '<text x="50%" y="50%" fill="#aeb7c4" text-anchor="middle" '
        'font-family="sans-serif" font-size="16">VTEX unavailable</text></svg>',
        safe="",
    )
)


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")


def image_kind(data: bytes) -> Optional[str]:
    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        return "png"
    if data.startswith(b"\xff\xd8\xff"):
        return "jpg"
    if data.startswith((b"GIF87a", b"GIF89a")):
        return "gif"
    if data.startswith(b"BM"):
        return "bmp"
    if data.startswith((b"II*\x00", b"MM\x00*")):
        return "tiff"
    if len(data) >= 12 and data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "webp"
    return None


def read_magic(path: Path) -> bytes:
    try:
        with path.open("rb") as stream:
            return stream.read(32)
    except OSError:
        return b""


def find_source_image(texture: Path, panorama_dir: Path) -> Optional[Path]:
    candidates: List[Path] = []
    try:
        with texture.open("rb") as stream:
            sample = stream.read(1024 * 1024)
        descriptor = "" if b"\x00" in sample[:4096] else sample.decode("utf-8-sig", errors="ignore")
        for match in re.finditer(
            r"""(?i)([\w./\\ -]+\.(?:png|jpe?g|tga|bmp|webp|tiff?))""",
            descriptor,
        ):
            raw = match.group(1).strip().replace("\\", "/")
            candidate = (texture.parent / raw).resolve()
            try:
                candidate.relative_to(panorama_dir.resolve())
            except ValueError:
                continue
            if candidate not in candidates:
                candidates.append(candidate)
    except (OSError, UnicodeError):
        pass

    for ext in IMAGE_SUFFIXES:
        candidate = texture.with_suffix(ext).resolve()
        try:
            candidate.relative_to(panorama_dir.resolve())
        except ValueError:
            continue
        candidates.append(candidate)
    for candidate in candidates:
        if candidate.is_file() and candidate != texture:
            return candidate
    return None


def run_vtex_converter(
    converter: Sequence[str], source: Path, destination: Path
) -> Optional[Path]:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(destination.stem + ".convert.png")
    command = [
        part.format(input=str(source), output=str(temporary), output_stem=str(temporary.with_suffix("")))
        for part in converter
    ]
    try:
        result = subprocess.run(
            command,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=120,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        print(f"VTEX: could not start converter for {source}: {exc}", file=sys.stderr)
        return None

    if result.returncode != 0:
        detail = (result.stdout or "").strip()
        print(
            f"VTEX: converter exited with code {result.returncode} for {source}"
            + (f": {detail[-500:]}" if detail else ""),
            file=sys.stderr,
        )
        return None

    produced = temporary if temporary.is_file() else None
    if produced is None:
        for suffix in (".png", ".jpg", ".jpeg"):
            candidate = temporary.with_suffix(suffix)
            if candidate.is_file():
                produced = candidate
                break
    if produced is None:
        print(
            f"VTEX: converter did not create an output file for {source}. "
            "Use {input} and {output} in the command.",
            file=sys.stderr,
        )
        return None

    kind = image_kind(read_magic(produced))
    if kind not in ("png", "jpg"):
        print(
            f"VTEX: converter created {kind or 'an unknown format'} "
            f"for {source}; supported output formats are PNG and JPG.",
            file=sys.stderr,
        )
        return None
    destination = destination.with_suffix("." + kind)
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(produced, destination)
    for suffix in (".convert.png", ".convert.jpg", ".convert.jpeg"):
        temporary.with_suffix(suffix).unlink(missing_ok=True)
    return destination


def convert_image_file(source: Path, destination_dir: Path, stem: str) -> Optional[Path]:
    data = read_magic(source)
    if not data:
        print(f"ASSET: cannot read {source}", file=sys.stderr)
        return None

    kind = image_kind(data)
    if kind in ("png", "jpg"):
        destination = destination_dir / (stem + "." + kind)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        return destination

    try:
        from PIL import Image
    except ImportError:
        print(
            f"ASSET: {source} uses {kind or source.suffix or 'an unknown format'}. "
            "Install Pillow to convert this format; PNG and JPG need no extra package.",
            file=sys.stderr,
        )
        return None

    try:
        with Image.open(source) as image:
            image.convert("RGBA").save(destination_dir / (stem + ".png"), format="PNG")
        return destination_dir / (stem + ".png")
    except Exception as exc:
        print(f"ASSET: could not convert {source}: {exc}", file=sys.stderr)
        return None


def convert_vtex_files(
    panorama_dir: Path,
    output_dir: Path,
    converter: Sequence[str],
) -> Dict[str, str]:
    lookup: Dict[str, str] = {}
    basename_lookup: Dict[str, List[str]] = {}
    if not panorama_dir.is_dir():
        print(f"ASSET: Panorama directory not found: {panorama_dir}")
        return lookup

    texture_files = sorted(
        path
        for path in panorama_dir.rglob("*")
        if path.is_file() and path.name.lower().endswith((".vtex", ".vtex_c"))
    )
    asset_root = output_dir / "assets" / "panorama"

    for texture in texture_files:
        relative = texture.relative_to(panorama_dir)
        relative_stem = relative.with_suffix("")
        if relative.name.lower().endswith(".vtex_c"):
            relative_stem = Path(str(relative)[:-7])
        destination_dir = asset_root / relative_stem.parent
        destination_stem = relative_stem.name

        converted: Optional[Path] = None
        source_image_url: Optional[str] = None
        direct_kind = image_kind(read_magic(texture))

        if direct_kind in ("png", "jpg"):
            converted = convert_image_file(texture, destination_dir, destination_stem)
        else:
            source_image = find_source_image(texture, panorama_dir)
            if source_image is not None:
                source_image_url = (
                    "content:" + source_image.relative_to(panorama_dir).as_posix()
                )
            elif converter:
                expected = destination_dir / (destination_stem + ".png")
                converted = run_vtex_converter(converter, texture, expected)

        if source_image_url is not None:
            web_path = source_image_url
        elif converted is None:
            print(
                f"VTEX: skipped {texture}; it is not a directly readable image. "
                "For compiled VTEX, provide --vtex-converter with {input} and {output}.",
                file=sys.stderr,
            )
            web_path = FALLBACK_TEXTURE
        else:
            web_path = (
                Path("assets") / "panorama" / relative_stem.parent
                / (destination_stem + converted.suffix.lower())
            ).as_posix()

        aliases = {
            relative.as_posix(),
            relative_stem.with_suffix(".vtex").as_posix(),
            relative_stem.with_suffix(".vtex_c").as_posix(),
        }
        for alias in aliases:
            lookup[alias.casefold()] = web_path
            basename_lookup.setdefault(Path(alias).name.casefold(), []).append(web_path)

    image_files = sorted(
        path for path in panorama_dir.rglob("*")
        if path.is_file() and path.suffix.casefold() in (
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".svg",
            ".vsvg", ".vsvg_c",
        )
    )
    raster_count = sum(
        1 for path in image_files
        if path.suffix.casefold() in (".png", ".jpg", ".jpeg")
    )
    vector_count = sum(
        1 for path in image_files
        if path.suffix.casefold() in (".svg", ".vsvg", ".vsvg_c")
    )
    print(
        f"ASSET: recursively found {raster_count} PNG/JPG and "
        f"{vector_count} SVG files under {panorama_dir}"
    )
    for source in image_files:
        relative = source.relative_to(panorama_dir)
        suffix = source.suffix.casefold()
        web_path: Optional[str] = None
        if suffix in (".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".svg"):
            web_path = "content:" + relative.as_posix()
        if suffix in (".vsvg", ".vsvg_c"):
            try:
                sample = source.read_bytes()[:4096]
                if sample.startswith((b"\xff\xfe", b"\xfe\xff")):
                    text_sample = sample.decode("utf-16", errors="ignore")
                else:
                    text_sample = sample.decode("utf-8-sig", errors="ignore")
                if not re.search(r"<svg\b", text_sample, re.I):
                    web_path = FALLBACK_TEXTURE
                    print(
                        f"SVG: no source SVG found in {source}; using a placeholder.",
                        file=sys.stderr,
                    )
                    output_suffix = ".svg"
                else:
                    output_suffix = ".svg"
            except OSError:
                web_path = FALLBACK_TEXTURE
                output_suffix = ".svg"
        else:
            output_suffix = suffix

        if web_path is None:
            destination = asset_root / relative.with_suffix(output_suffix)
            destination.parent.mkdir(parents=True, exist_ok=True)
            try:
                shutil.copyfile(source, destination)
                web_path = (
                    Path("assets") / "panorama" / relative.with_suffix(output_suffix)
                ).as_posix()
            except OSError as exc:
                web_path = FALLBACK_TEXTURE
                print(f"ASSET: could not copy {source}: {exc}", file=sys.stderr)

        aliases = {relative.as_posix()}
        if suffix in (".png", ".jpg", ".jpeg"):
            aliases.add(relative.with_suffix(".vtex").as_posix())
            aliases.add(relative.with_suffix(".vtex_c").as_posix())
        if suffix == ".svg":
            aliases.add(relative.with_suffix(".vsvg").as_posix())
            aliases.add(relative.with_suffix(".vsvg_c").as_posix())
        if suffix in (".vsvg", ".vsvg_c"):
            aliases.add(relative.with_suffix(".svg").as_posix())
            aliases.add(relative.with_suffix(".vsvg").as_posix())
            aliases.add(relative.with_suffix(".vsvg_c").as_posix())
        for alias in aliases:
            lookup[alias.casefold()] = web_path
            basename_lookup.setdefault(Path(alias).name.casefold(), []).append(web_path)

    for basename, paths in basename_lookup.items():
        if len(set(paths)) == 1:
            lookup[basename] = paths[0]
    print(
        f"VTEX: found {len(texture_files)} .vtex/.vtex_c files; "
        f"indexed {len(image_files)} raster/vector files"
    )
    return lookup


def texture_url(raw_path: str, lookup: Dict[str, str]) -> Optional[str]:
    source = urllib.parse.unquote(raw_path).replace("\\", "/")
    normalized = source.casefold()
    normalized = normalized.split("?", 1)[0].split("#", 1)[0]
    is_panorama_asset = (
        "s2r://panorama/" in normalized
        or "content/panorama/" in normalized
    )
    normalized = normalized.replace("s2r://panorama/", "")

    for marker in ("content/panorama/", "panorama/"):
        if marker in normalized:
            normalized = normalized.rsplit(marker, 1)[1]
            break
    normalized = normalized.lstrip("./")
    if normalized in lookup:
        return lookup[normalized]
    basename = normalized.rsplit("/", 1)[-1]
    found = lookup.get(basename)
    if found:
        return found
    if is_panorama_asset and re.search(
        r"\.(?:vtex(?:_c)?|vsvg(?:_c)?|png|jpe?g|svg|webp|gif|bmp)$",
        normalized,
        re.I,
    ):
        return FALLBACK_TEXTURE
    return None


def rewrite_texture_refs(
    text: str,
    lookup: Dict[str, str],
    css: bool = False,
    source_asset_prefix: str = "../Content/panorama/",
) -> str:
    def replace(match: re.Match[str]) -> str:
        original = match.group(1)
        replacement = texture_url(original, lookup)
        if replacement is None:
            return match.group(0)
        if replacement.startswith("content:"):
            relative_path = replacement[len("content:"):]
            replacement = source_asset_prefix + urllib.parse.quote(
                relative_path,
                safe="/-_.",
            )
        if css and replacement.startswith("assets/"):
            replacement = "../" + replacement
        return replacement

    return re.sub(
        r"""(?i)([^"'()\s;]+\.(?:vtex_c|vtex|vsvg_c|vsvg|png|jpe?g|svg|webp|gif|bmp))""",
        replace,
        text,
    )


def split_top_level(text: str) -> List[str]:
    parts: List[str] = []
    start = depth = 0
    quote: Optional[str] = None
    escaped = False
    for index, char in enumerate(text):
        if quote:
            if char == quote and not escaped:
                quote = None
            escaped = char == "\\" and not escaped
            if char != "\\":
                escaped = False
            continue
        if char in ("'", '"'):
            quote = char
        elif char == "(":
            depth += 1
        elif char == ")":
            depth = max(0, depth - 1)
        elif char == "," and depth == 0:
            parts.append(text[start:index].strip())
            start = index + 1
    parts.append(text[start:].strip())
    return parts


def matching_paren(text: str, open_index: int) -> int:
    depth = 0
    quote: Optional[str] = None
    escaped = False
    for index in range(open_index, len(text)):
        char = text[index]
        if quote:
            if char == quote and not escaped:
                quote = None
            escaped = char == "\\" and not escaped
            if char != "\\":
                escaped = False
            continue
        if char in ("'", '"'):
            quote = char
        elif char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                return index
    return -1


def convert_one_gradient(inner: str) -> Optional[str]:
    parts = split_top_level(inner)
    if len(parts) < 2:
        return None
    kind = parts[0].strip().lower()
    if kind not in ("linear", "radial"):
        return None

    stops: List[str] = []
    first_stop = -1
    for index, part in enumerate(parts[1:], 1):
        match = re.match(r"(?i)^(from|to)\s*\(\s*([^)]+?)\s*\)$", part)
        if match:
            first_stop = index if first_stop < 0 else first_stop
            stops.append(match.group(2).strip() + (" 0%" if match.group(1).lower() == "from" else " 100%"))
            continue
        match = re.match(
            r"(?i)^color-stop\s*\(\s*([\d.]+%?)\s*,\s*([^)]+?)\s*\)$",
            part,
        )
        if match:
            first_stop = index if first_stop < 0 else first_stop
            position = match.group(1)
            if not position.endswith("%"):
                position = f"{float(position) * 100:g}%"
            stops.append(match.group(2).strip() + " " + position)
    if not stops:
        return None

    if kind == "radial":
        position = "50% 50%"
        if first_stop > 1:
            header = " ".join(parts[1:first_stop])
            point = re.search(r"(-?[\d.]+%?)\s+(-?[\d.]+%?)", header)
            if point:
                position = " ".join(
                    value if value.endswith("%") else value + "%"
                    for value in point.groups()
                )
        return f"radial-gradient(ellipse at {position}, {', '.join(stops)})"

    angle = 180
    if first_stop > 2:
        point_a = re.search(r"(-?[\d.]+%?)\s+(-?[\d.]+%?)", parts[1])
        point_b = re.search(r"(-?[\d.]+%?)\s+(-?[\d.]+%?)", parts[2])
        if point_a and point_b:
            numbers = [
                float(value.rstrip("%"))
                for value in (*point_a.groups(), *point_b.groups())
            ]
            dx, dy = numbers[2] - numbers[0], numbers[3] - numbers[1]
            import math

            angle = round(math.degrees(math.atan2(dx, -dy)))
    return f"linear-gradient({angle}deg, {', '.join(stops)})"


def convert_gradients(text: str) -> str:
    matches = list(re.finditer(r"(?i)(?<![-\w])gradient\s*\(", text))
    if not matches:
        return text
    output: List[str] = []
    cursor = 0
    for match in matches:
        open_index = match.end() - 1
        close_index = matching_paren(text, open_index)
        if close_index < 0:
            break
        output.append(text[cursor:match.start()])
        converted = convert_one_gradient(text[open_index + 1:close_index])
        output.append(
            converted
            if converted is not None
            else text[match.start():close_index + 1]
        )
        cursor = close_index + 1
    output.append(text[cursor:])
    return "".join(output)


def convert_blur(text: str) -> str:
    def replace(match: re.Match[str]) -> str:
        amount = re.search(r"-?\d+(?:\.\d+)?\s*(?:px|rem|%)?", match.group(2))
        if not amount:
            return match.group(0)
        value = amount.group(0).strip()
        if not re.search(r"[a-z%]$", value, re.I):
            value += "px"
        return match.group(1) + "filter: blur(" + value + ");"

    return re.sub(r"(?im)(^|[;{]\s*)blur\s*:\s*([^;]+);", replace, text)


def translate_css(
    text: str,
    texture_lookup: Dict[str, str],
    source_asset_prefix: str = "../../Content/panorama/",
) -> str:
    text = re.sub(
        r"(?i)s2r://panorama/",
        lambda _: source_asset_prefix,
        text,
    )
    text = re.sub(r"(?i)\.vsvg(?:_c)?", ".svg", text)
    text = re.sub(r"(?i)flow-children:\s*down\s*;", "display:flex;flex-direction:column;", text)
    text = re.sub(r"(?i)flow-children:\s*right\s*;", "display:flex;flex-direction:row;", text)
    text = re.sub(r"(?i)flow-children:\s*none\s*;", "", text)
    text = re.sub(
        r"(?i)horizontal-align:\s*right\s*;",
        "justify-self:end;align-self:flex-end;",
        text,
    )
    text = re.sub(
        r"(?i)horizontal-align:\s*center\s*;",
        "justify-self:center;align-self:center;",
        text,
    )
    text = re.sub(
        r"(?i)horizontal-align:\s*(left|top)\s*;",
        "justify-self:start;align-self:flex-start;",
        text,
    )
    text = re.sub(r"(?i)vertical-align:\s*bottom\s*;", "align-self:flex-end;", text)
    text = re.sub(r"(?i)vertical-align:\s*center\s*;", "align-self:center;", text)
    text = re.sub(
        r"(?i)vertical-align:\s*(top|middle)\s*;",
        "align-self:flex-start;",
        text,
    )

    def convert_align(match: re.Match[str]) -> str:
        values = {"left": "start", "right": "end", "center": "center", "top": "start", "bottom": "end"}
        return "justify-self:{};align-self:{};".format(
            values[match.group(1).lower()], values[match.group(2).lower()]
        )

    text = re.sub(
        r"(?im)(?<![-\w])align\s*:\s*(left|center|right|top|bottom)\s+"
        r"(left|center|right|top|bottom)\s*;",
        convert_align,
        text,
    )
    text = re.sub(r"(?i)(background-size\s*:\s*)contains\b", r"\1contain", text)
    text = re.sub(r"(?i)visibility\s*:\s*collapse\s*;", "display:none;", text)
    text = re.sub(r"(?i)fit-children", "fit-content", text)
    text = re.sub(r"(?i)-s2-mix-blend-mode", "mix-blend-mode", text)
    text = re.sub(r"(?i)pre-transform-scale2d\s*:", "scale:", text)
    text = re.sub(r"(?i)@keyframes\s+'([^']+)'", r"@keyframes \1", text)
    text = convert_blur(text)
    text = convert_gradients(text)
    text = rewrite_texture_refs(
        text,
        texture_lookup,
        css=True,
        source_asset_prefix=source_asset_prefix,
    )
    return text


def find_language_file(root: Path) -> Tuple[Optional[dict], Optional[Path]]:
    candidates = [
        root / "jRandomSkills - Server Files" / "plugins" / "jRandomSkills" / "languages" / "en.json",
        root.parent / "jRandomSkills - Server Files" / "plugins" / "jRandomSkills" / "languages" / "en.json",
        root.parent.parent / "jRandomSkills - Server Files" / "plugins" / "jRandomSkills" / "languages" / "en.json",
        root / "Content" / "panorama" / "localization" / "en.json",
        root / "Content" / "panorama" / "localization" / "languages" / "en.json",
        root / "Content" / "panorama" / "languages" / "en.json",
        root / "Content" / "panorama" / "lang" / "en.json",
        root / "languages" / "en.json",
    ]
    seen = set()

    def load(candidate: Path) -> Optional[dict]:
        try:
            resolved = candidate.resolve()
        except OSError:
            return None
        if resolved in seen:
            return None
        seen.add(resolved)
        if candidate.is_file():
            try:
                data = json.loads(read_text(candidate))
                if isinstance(data, dict):
                    return data
                print(f"LANG: {candidate} does not contain a JSON object.", file=sys.stderr)
            except (OSError, json.JSONDecodeError) as exc:
                print(f"LANG: could not read {candidate}: {exc}", file=sys.stderr)
        return None

    for candidate in candidates:
        data = load(candidate)
        if data is not None:
            return data, candidate

    search_roots = [
        root / "Content" / "panorama" / "localization",
        root / "Content" / "panorama" / "languages",
        root / "Content" / "panorama" / "lang",
        root / "jRandomSkills - Server Files" / "plugins" / "jRandomSkills",
        root.parent / "jRandomSkills - Server Files" / "plugins" / "jRandomSkills",
    ]
    for search_root in search_roots:
        if not search_root.is_dir():
            continue
        for candidate in search_root.rglob("*"):
            if candidate.is_file() and candidate.name.casefold() == "en.json":
                data = load(candidate)
                if data is not None:
                    return data, candidate
    return None, None


def expand_localization(text: str, language: Optional[dict]) -> str:
    if not language:
        return text
    flattened: Dict[str, str] = {}

    def collect(node, prefix: str = "") -> None:
        if isinstance(node, dict):
            for field in ("text", "value", "translation", "en", "english"):
                value = node.get(field)
                if prefix and isinstance(value, str):
                    flattened.setdefault(prefix, value)
            for key, value in node.items():
                path = f"{prefix}.{key}" if prefix else str(key)
                if isinstance(value, str):
                    flattened[path] = value
                    flattened.setdefault(str(key), value)
                elif isinstance(value, dict):
                    collect(value, path)

    collect(language)
    casefolded = {key.casefold(): value for key, value in flattened.items()}

    def replace(match: re.Match[str]) -> str:
        key = match.group(1)
        value = flattened.get(key, casefolded.get(key.casefold()))
        if value is None:
            return match.group(0)
        return html.escape(value, quote=True)

    return re.sub(r"\{s:([A-Za-z0-9_.-]+)\}", replace, text)


def find_stylesheet(style_dir: Path, include_src: str) -> Optional[Path]:
    name = Path(include_src.replace("\\", "/")).name
    stem = re.sub(r"(?i)\.(vcss_c|vcss|css)$", "", name)
    for suffix in (".css", ".vcss", ".vcss_c"):
        candidate = style_dir / (stem + suffix)
        if candidate.is_file():
            return candidate
    if style_dir.is_dir():
        for candidate in style_dir.rglob("*"):
            if candidate.is_file() and candidate.stem.casefold() == stem.casefold():
                return candidate
    return None


def render_node(
    node: ET.Element,
    texture_lookup: Dict[str, str],
    source_asset_prefix: str = "../Content/panorama/",
) -> str:
    name = node.tag.split("}")[-1]
    if name.lower() in ("styles", "scripts", "include", "root"):
        return "".join(
            render_node(child, texture_lookup, source_asset_prefix) for child in node
        )

    if name == "Panel" or name in DIV_TAGS:
        tag = "div"
    elif name == "Button":
        tag = "button"
    elif name == "Label":
        tag = "span"
    elif name == "Image":
        tag = "img"
    else:
        return "".join(
            render_node(child, texture_lookup, source_asset_prefix) for child in node
        )

    attrs: List[Tuple[str, str]] = []
    for key, value in node.attrib.items():
        key = key.split("}")[-1]
        if key.lower().startswith("hittest"):
            continue
        if key == "text":
            continue
        value = rewrite_texture_refs(
            value,
            texture_lookup,
            source_asset_prefix=source_asset_prefix,
        )
        value = re.sub(
            r"(?i)s2r://panorama/",
            lambda _: source_asset_prefix,
            value,
        )
        value = re.sub(r"(?i)\.vsvg(?:_c)?", ".svg", value)
        attrs.append((key, value))

    if tag == "button" and not any(key.lower() == "type" for key, _ in attrs):
        attrs.append(("type", "button"))
    ident = node.attrib.get("id")
    if ident and not any(key == "data-id" for key, _ in attrs):
        attrs.append(("data-id", ident))

    rendered_attrs = "".join(
        f' {key}="{html.escape(value, quote=True)}"' for key, value in attrs
    )
    if tag == "img":
        return f"<img{rendered_attrs}>"

    body_parts: List[str] = []
    text_value = node.attrib.get("text", "")
    if text_value:
        body_parts.append(
            html.escape(
                rewrite_texture_refs(
                    text_value,
                    texture_lookup,
                    source_asset_prefix=source_asset_prefix,
                )
            )
        )
    elif node.text and node.text.strip():
        body_parts.append(
            html.escape(
                rewrite_texture_refs(
                    node.text.strip(),
                    texture_lookup,
                    source_asset_prefix=source_asset_prefix,
                )
            )
        )
    for child in node:
        body_parts.append(render_node(child, texture_lookup, source_asset_prefix))
        if child.tail and child.tail.strip():
            body_parts.append(html.escape(child.tail.strip()))
    return f"<{tag}{rendered_attrs}>{''.join(body_parts)}</{tag}>"


def map_options(output_dir: Path) -> str:
    maps_dir = output_dir / "maps"
    maps_dir.mkdir(parents=True, exist_ok=True)
    options = ['<option value="">Default background</option>']
    maps = sorted(
        path for path in maps_dir.iterdir()
        if path.is_file() and path.suffix.lower() in (".png", ".jpg", ".jpeg", ".webp")
    )
    for path in maps:
        value = "maps/" + urllib.parse.quote(path.name)
        options.append(
            f'<option value="{html.escape(value, quote=True)}">'
            f"{html.escape(path.stem)}</option>"
        )
    print(f"MAPS: found {len(maps)} images in {maps_dir}")
    return "\n      ".join(options)


def html_layout_options(
    outputs: Sequence[Tuple[Path, Path]],
    selected_output: Path,
) -> str:
    if len(outputs) < 2:
        return ""
    options = []
    for layout, output in outputs:
        selected = " selected" if output == selected_output else ""
        value = urllib.parse.quote(output.name)
        label = html.escape(layout.stem)
        options.append(
            f'<option value="{value}"{selected}>{label}</option>'
        )
    return (
        '<div class="pv_row"><label for="pv_html" style="align-content:center;">HTML:</label>'
        '<select id="pv_html" style="width:65%;margin-left:auto;">'
        + "".join(options)
        + "</select></div>"
    )


def build_html(
    layout: Path,
    root: Path,
    output_dir: Path,
    texture_lookup: Dict[str, str],
    language: Optional[dict],
    layout_options: str = "",
) -> str:
    xml_text = expand_localization(read_text(layout), language)
    document = ET.fromstring(xml_text)
    panorama_dir = root / "Content" / "panorama"
    styles_root = panorama_dir / "styles"
    preferred_style_dir = styles_root / layout.parent.name
    style_dir = preferred_style_dir if preferred_style_dir.is_dir() else styles_root
    html_source_prefix = os.path.relpath(panorama_dir, output_dir).replace("\\", "/") + "/"
    css_source_prefix = os.path.relpath(
        panorama_dir, output_dir / "styles"
    ).replace("\\", "/") + "/"

    links: List[str] = []
    for element in document.iter():
        if element.tag.split("}")[-1] != "include":
            continue
        source = element.get("src", "")
        stylesheet = find_stylesheet(style_dir, source)
        name = Path(source.replace("\\", "/")).name
        stem = re.sub(r"(?i)\.(vcss_c|vcss|css)$", "", name)
        if stylesheet:
            css_name = stem + ".css"
            css_output = output_dir / "styles" / css_name
            write_text(
                css_output,
                translate_css(
                    read_text(stylesheet),
                    texture_lookup,
                    source_asset_prefix=css_source_prefix,
                ),
            )
            link = f'  <link rel="stylesheet" href="styles/{urllib.parse.quote(css_name)}">'
            if link not in links:
                links.append(link)

    body = "".join(
        render_node(child, texture_lookup, html_source_prefix)
        for child in document
    )
    link_tags = "\n".join(links)
    filename = html.escape(layout.name, quote=True)
    title = html.escape(layout.stem, quote=True)
    return f"""<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>{title}</title>
{link_tags}
  <style>
    html, body {{ margin:0; height:100%; overflow:hidden; background:#000; }}
    button {{ background:none; border:0; color:inherit; font:inherit; cursor:pointer; text-align:inherit; }}
    #stage {{ position:absolute; left:0; top:0; width:1920px; height:1080px;
      background:linear-gradient(135deg,#6b7f6b,#2b3a4a 60%,#1c2530);
      transform-origin:0 0; overflow:hidden; }}
    :where(#stage div) {{ display:grid; grid-template-columns:minmax(0,1fr); grid-template-rows:minmax(0,1fr);
      box-sizing:border-box; position:relative; }}
    :where(#stage div) > :where(*) {{ grid-area:1/1; justify-self:start; align-self:start; }}
    :where(#stage span) {{ display:block; white-space:nowrap; }}
    :where(#stage) > :where(div) {{ width:100%; height:100%; }}
    :where(#stage button) {{ padding:0; }}
    #pv_panel {{ position:fixed; right:12px; bottom:12px; z-index:99999; display:flex; flex-direction:column;
      gap:6px; padding:8px 10px; min-width:170px; background:rgba(0,0,0,.68); color:#fff;
      font:12px/1.3 Consolas,monospace; border:1px solid rgba(255,255,255,.25); border-radius:6px;
      opacity:.35; transition:opacity .2s; }}
    #pv_panel:hover {{ opacity:1; }}
    #pv_name {{ font-weight:bold; }}
    #pv_panel .pv_row {{ display:flex; gap:6px; }}
    #pv_panel button, #pv_panel select {{ font:inherit; color:#fff; background:rgba(255,255,255,.12);
      border:1px solid rgba(255,255,255,.3); border-radius:4px; padding:3px 8px; cursor:pointer; }}
    #pv_panel select option {{ color:#000; }}
    #pv_panel button.pv_on {{ background:#2e7d32; border-color:#66bb6a; }}
    #pv_panel button:disabled {{ opacity:.5; cursor:default; }}
    #pv_grid_overlay {{ position:absolute; left:0; top:0; width:1920px; height:1080px; pointer-events:none;
      z-index:9999; display:none;
      background-image:linear-gradient(to right,rgba(255,255,255,.35) 1px,transparent 1px),
        linear-gradient(to bottom,rgba(255,255,255,.35) 1px,transparent 1px),
        linear-gradient(to right,rgba(255,255,255,.12) 1px,transparent 1px),
        linear-gradient(to bottom,rgba(255,255,255,.12) 1px,transparent 1px),
        linear-gradient(to right,transparent 959px,rgba(255,80,80,.7) 959px,rgba(255,80,80,.7) 961px,transparent 961px),
        linear-gradient(to bottom,transparent 539px,rgba(255,80,80,.7) 539px,rgba(255,80,80,.7) 541px,transparent 541px);
      background-size:100px 100px,100px 100px,20px 20px,20px 20px,100% 100%,100% 100%;
      background-repeat:repeat,repeat,repeat,repeat,no-repeat,no-repeat; }}
    #pv_grid_overlay.on {{ display:block; }}
  </style>
</head>
<body>
<div id="stage">
{body}
<div id="pv_grid_overlay"></div>
</div>
<div id="pv_panel">
  <div id="pv_name">{filename}</div>
  {layout_options}
  <div class="pv_row">
    <button id="pv_show" type="button">show</button>
    <button id="pv_grid" type="button">Grid: OFF</button>
  </div>
  <div id="pv_pos_row" class="pv_row" style="display:none;">
    <label for="pv_pos" style="align-content:center;">Position:</label>
    <select id="pv_pos" style="width:65%;margin-left:auto;">
      <option value="">Unassigned</option>
      <option value="pos_left_top">LeftTop</option>
      <option value="pos_left_center">LeftCenter</option>
      <option value="pos_left_bottom">LeftBottom</option>
      <option value="pos_center_top">CenterTop</option>
      <option value="pos_center_center">CenterCenter</option>
      <option value="pos_center_bottom">CenterBottom</option>
      <option value="pos_right_top">RightTop</option>
      <option value="pos_right_center">RightCenter</option>
      <option value="pos_right_bottom">RightBottom</option>
    </select>
  </div>
  <div class="pv_row">
    <label for="pv_bg" style="align-content:center;">Background:</label>
    <select id="pv_bg" style="width:65%;margin-left:auto;">{map_options(output_dir)}</select>
  </div>
</div>
<script>
  function fit() {{
    var scale = Math.min(window.innerWidth / 1920, window.innerHeight / 1080);
    document.getElementById('stage').style.transform = 'scale(' + scale + ')';
  }}
  window.addEventListener('resize', fit);
  fit();
  (function() {{
    var stage = document.getElementById('stage');
    var root = document.getElementById('jrs_root') ||
      document.querySelector('.jrs_root') ||
      Array.from(document.querySelectorAll('[id^="jrs_"]')).find(function(el) {{
        return el.id.indexOf('root') !== -1;
      }}) || stage.firstElementChild;
    var show = document.getElementById('pv_show');
    var gridButton = document.getElementById('pv_grid');
    var grid = document.getElementById('pv_grid_overlay');
    var bg = document.getElementById('pv_bg');
    var htmlSelect = document.getElementById('pv_html');
    var posRow = document.getElementById('pv_pos_row');
    var pos = document.getElementById('pv_pos');
    var positionClasses = [
      'pos_left_top','pos_left_center','pos_left_bottom',
      'pos_center_top','pos_center_center','pos_center_bottom',
      'pos_right_top','pos_right_center','pos_right_bottom'
    ];
    if (root) root.classList.add('show');
    function refreshShow() {{
      if (!root) {{ show.disabled = true; show.textContent = 'No module root'; return; }}
      var on = root.classList.contains('show');
      show.textContent = '#' + root.id + '.show: ' + (on ? 'ON' : 'OFF');
      show.className = on ? 'pv_on' : '';
    }}
    function refreshPosition() {{
      var current = root && positionClasses.find(function(name) {{ return root.classList.contains(name); }});
      posRow.style.display = 'flex';
      pos.value = current || '';
    }}
    show.onclick = function() {{ if (root) {{ root.classList.toggle('show'); refreshShow(); }} }};
    pos.onchange = function() {{
      if (!root || !pos.value) return;
      positionClasses.forEach(function(name) {{ root.classList.remove(name); }});
      root.classList.add(pos.value);
      refreshPosition();
    }};
    gridButton.onclick = function() {{
      var on = grid.classList.toggle('on');
      gridButton.textContent = 'Grid: ' + (on ? 'ON' : 'OFF');
      gridButton.className = on ? 'pv_on' : '';
    }};
    function applyBg(value) {{
      stage.style.background = value ? 'url("' + value + '") center / cover no-repeat' : '';
    }}
    bg.onchange = function() {{
      applyBg(bg.value);
      try {{ localStorage.setItem('pv_bg', bg.value); }} catch (e) {{}}
    }};
    if (htmlSelect) {{
      htmlSelect.onchange = function() {{
        if (htmlSelect.value) window.location.href = htmlSelect.value;
      }};
    }}
    try {{
      var saved = localStorage.getItem('pv_bg');
      if (saved) {{ bg.value = saved; if (bg.value === saved) applyBg(saved); }}
    }} catch (e) {{}}

    document.getElementById('stage').addEventListener('click', function(event) {{
      var button = event.target.closest('button');
      if (!button || !button.parentElement) return;
      var siblings = Array.from(button.parentElement.children).filter(function(el) {{
        return el.tagName === 'BUTTON';
      }});
      var classes = Array.from(button.classList).filter(function(name) {{
        return name !== 'active' && name !== 'disabled';
      }});
      if (!classes.length) return;
      var preferred = classes.find(function(name) {{
        return /tab|group|toggle|segment|switch/i.test(name) &&
          siblings.filter(function(el) {{ return el.classList.contains(name); }}).length > 1;
      }});
      var candidates = classes.map(function(name) {{
        return {{
          name: name,
          members: siblings.filter(function(el) {{ return el.classList.contains(name); }})
        }};
      }}).filter(function(item) {{ return item.members.length > 1; }});
      candidates.sort(function(a, b) {{
        return b.members.length - a.members.length;
      }});
      var groupClass = preferred || (candidates[0] && candidates[0].name);
      var group = groupClass
        ? siblings.filter(function(el) {{ return el.classList.contains(groupClass); }})
        : [button];
      group.forEach(function(el) {{
        var selected = el === button;
        el.classList.toggle('active', selected);
        el.setAttribute('aria-pressed', selected ? 'true' : 'false');
      }});
    }});
    refreshShow();
    refreshPosition();
  }})();
</script>
</body>
</html>
"""


def select_layouts(layout_dir: Path, requested: Optional[str]) -> List[Path]:
    if requested:
        candidate = Path(requested)
        if not candidate.is_absolute():
            candidate = layout_dir / candidate
        candidate = candidate.resolve()
        try:
            candidate.relative_to(layout_dir.resolve())
        except ValueError as exc:
            raise ValueError("--layout must point to an XML file inside the layout directory.") from exc
        if not candidate.is_file() or candidate.suffix.lower() != ".xml":
            raise FileNotFoundError(f"XML file not found: {candidate}")
        return [candidate]
    if not layout_dir.is_dir():
        raise FileNotFoundError(f"Layout directory not found: {layout_dir}")
    layouts = sorted(path for path in layout_dir.glob("*.xml") if path.is_file())
    if not layouts:
        raise FileNotFoundError(f"No .xml files found in: {layout_dir}")
    return layouts


def build_previews(args: argparse.Namespace) -> List[Tuple[Path, Path]]:
    root = args.root.resolve()
    layout_dir = (args.layout_dir or (
        root / "Content" / "panorama" / "layout" / "jRandomSkills"
    )).resolve()
    output_dir = (args.output_dir or (root / "Preview")).resolve()
    layouts = select_layouts(layout_dir, args.layout)

    converter = shlex.split(args.vtex_converter) if args.vtex_converter else []
    if converter and not any(
        "{input}" in part for part in converter
    ):
        raise ValueError("--vtex-converter must include the {input} placeholder.")
    if converter and not any("{output}" in part for part in converter):
        raise ValueError("--vtex-converter must include the {output} placeholder.")

    language, language_path = find_language_file(root)
    if language_path:
        print(f"LANG: {language_path}")
    else:
        print("LANG: en.json not found; {s:key} tokens will remain unchanged")

    texture_lookup = convert_vtex_files(
        root / "Content" / "panorama", output_dir, converter
    )
    output_dir.mkdir(parents=True, exist_ok=True)
    results: List[Tuple[Path, Path]] = []
    for index, layout in enumerate(layouts):
        destination_name = (
            args.html_name
            if index == 0 and args.html_name
            else layout.stem + ".html"
        )
        destination = output_dir / destination_name
        results.append((layout, destination))

    for layout, destination in results:
        write_text(
            destination,
            build_html(
                layout,
                root,
                output_dir,
                texture_lookup,
                language,
                html_layout_options(results, destination),
            ),
        )
        print(f"HTML: {layout.name} -> {destination} (rebuilt)")
    return results


def make_handler(args: argparse.Namespace, root: Path, output_dir: Path):
    class PreviewHandler(SimpleHTTPRequestHandler):
        def __init__(self, *handler_args, **handler_kwargs):
            super().__init__(*handler_args, directory=str(root), **handler_kwargs)

        def do_GET(self):
            parsed = urllib.parse.urlsplit(self.path)
            request_path = urllib.parse.unquote(parsed.path)
            is_preview_request = request_path == "/" or request_path.lower().endswith(".html")
            if not is_preview_request:
                return super().do_GET()

            try:
                results = build_previews(args)
            except Exception as exc:
                message = f"Preview build failed: {exc}\n"
                self.send_response(500)
                self.send_header("Content-Type", "text/plain; charset=utf-8")
                self.end_headers()
                self.wfile.write(message.encode("utf-8"))
                print(message.strip(), file=sys.stderr)
                return

            query = urllib.parse.parse_qs(parsed.query)
            requested_layout = query.get("layout", [None])[0]
            selected: Optional[Path] = None
            if requested_layout:
                requested_name = Path(requested_layout).name.casefold()
                for layout, output in results:
                    if layout.name.casefold() == requested_name or layout.stem.casefold() == requested_name:
                        selected = output
                        break
                if selected is None:
                    self.send_error(404, "Requested layout XML was not found.")
                    return
            elif request_path != "/":
                request_name = Path(request_path).name.casefold()
                for _, output in results:
                    if output.name.casefold() == request_name:
                        selected = output
                        break
            if selected is None:
                selected = results[0][1]

            if request_path == "/":
                try:
                    relative_url = selected.resolve().relative_to(root).as_posix()
                except ValueError:
                    self.send_error(
                        500,
                        "The HTML output directory must be inside --root "
                        "so the server can serve CSS and images.",
                    )
                    return
                self.send_response(302)
                self.send_header("Location", "/" + urllib.parse.quote(relative_url))
                self.send_header("Cache-Control", "no-cache")
                self.end_headers()
                return

            try:
                content = selected.read_bytes()
            except OSError as exc:
                self.send_error(500, f"Could not read {selected.name}: {exc}")
                return
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(content)))
            self.send_header("Cache-Control", "no-cache")
            self.end_headers()
            self.wfile.write(content)

        def log_message(self, fmt: str, *values) -> None:
            print(f"[{self.log_date_time_string()}] {fmt % values}")

    return PreviewHandler


def parse_args() -> argparse.Namespace:
    script_root = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(
        description="Builds a Panorama XML preview and serves it in a browser."
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=script_root,
        help="project directory containing Content (defaults to the script directory)",
    )
    parser.add_argument(
        "--layout-dir",
        type=Path,
        default=None,
        help="directory containing layout XML files",
    )
    parser.add_argument(
        "--layout",
        help="build and serve one XML file by name or path relative to --layout-dir",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=None,
        help="output directory (defaults to Preview)",
    )
    parser.add_argument(
        "--html-name",
        help="output HTML filename for the first selected layout, for example preview.html",
    )
    parser.add_argument("--host", default="127.0.0.1", help="server address (defaults to 127.0.0.1)")
    parser.add_argument("--port", type=int, default=8091, help="HTTP port (defaults to 8091)")
    parser.add_argument(
        "--vtex-converter",
        help=(
            "optional command with {input} and {output} placeholders, "
            "for example 'vtex2png {input} {output}'"
        ),
    )
    parser.add_argument(
        "--build-only",
        action="store_true",
        help="generate files without starting the server",
    )
    args = parser.parse_args()
    if args.html_name:
        if Path(args.html_name).name != args.html_name or not args.html_name.lower().endswith(".html"):
            parser.error("--html-name must be a filename ending in .html.")
    if not 1 <= args.port <= 65535:
        parser.error("--port must be between 1 and 65535.")
    return args


def main() -> int:
    args = parse_args()
    try:
        results = build_previews(args)
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 1

    if args.build_only:
        return 0

    root = args.root.resolve()
    output_dir = (args.output_dir or (root / "Preview")).resolve()
    handler = make_handler(args, root, output_dir)
    try:
        server = ThreadingHTTPServer((args.host, args.port), handler)
    except OSError as exc:
        print(f"Could not start server at {args.host}:{args.port}: {exc}", file=sys.stderr)
        return 1

    first_html = results[0][1]
    print(f"Preview: http://{args.host}:{args.port}/")
    print(f"Default file: {first_html}")
    print("Reloading the page rebuilds the preview. Stop the server with Ctrl+C.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nPreview server stopped.")
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
