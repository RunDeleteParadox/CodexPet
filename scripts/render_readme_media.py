# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Compose the README gallery from frames rendered by the actual firmware engine."""

import argparse
import json
import pathlib
import re

from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXPRESSIONS = ["idle", "thinking", "working", "waiting", "success", "error", "stop", "sleepy", "wake"]
POSE_MS = [1000, 1500, 1000, 1900, 1900, 600, 500, 1100, 1500]
WIDTH, HEIGHT = 936, 1056
MARGIN, GAP, CARD_WIDTH, CARD_HEIGHT, DISPLAY_SIZE = 24, 16, 285, 302, 236
FRAME_MS = 100
TRANSLATIONS = {
    "en": {
        "title": "CodexPet in motion",
        "subtitle": "Nine expressions for your M5Stack StopWatch",
        "footer": "Firmware {version} · Software preview · Reactions repeat for this gallery",
        "labels": [
            ("Idle", "Ready for the next prompt"),
            ("Thinking", "A turn is in progress"),
            ("Working", "Tools are running"),
            ("Waiting for you", "Your input is needed"),
            ("Response complete", "The response has finished"),
            ("Error", "A tool reported an error"),
            ("Stopped", "The turn was interrupted"),
            ("Sleep", "Settling into sleep"),
            ("Wake", "Back to the current activity"),
        ],
    },
    "fr": {
        "title": "CodexPet en mouvement",
        "subtitle": "Neuf animations pour ton M5Stack StopWatch",
        "footer": "Firmware {version} · Aperçu logiciel · Réactions répétées pour cette planche",
        "labels": [
            ("Au repos", "Prêt pour la suite"),
            ("Réfléchit", "Un tour est en cours"),
            ("Travaille", "Des outils sont en cours"),
            ("Attend ton intervention", "Une réponse est attendue"),
            ("Réponse terminée", "La réponse est terminée"),
            ("Erreur", "Un outil a signalé une erreur"),
            ("Interrompu", "Le tour a été interrompu"),
            ("Veille", "Transition vers la veille"),
            ("Réveil", "Retour à l'activité"),
        ],
    },
}


def font(size, bold=False):
    filename = "segoeuib.ttf" if bold else "segoeui.ttf"
    candidates = [pathlib.Path("C:/Windows/Fonts") / filename,
                  pathlib.Path("/usr/share/fonts/truetype/dejavu") /
                  ("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf")]
    for path in candidates:
        if path.is_file():
            return ImageFont.truetype(str(path), size)
    raise RuntimeError("Install Segoe UI or DejaVu Sans to render the gallery labels.")


def position(index):
    return MARGIN + index % 3 * (CARD_WIDTH + GAP), 88 + index // 3 * (CARD_HEIGHT + GAP)


def template(language, version):
    text = TRANSLATIONS[language]
    canvas = Image.new("RGB", (WIDTH, HEIGHT), "#0b1018")
    draw = ImageDraw.Draw(canvas)
    draw.text((MARGIN, 17), text["title"], font=font(30, True), fill="#f1f5f9")
    draw.text((MARGIN, 56), text["subtitle"], font=font(15), fill="#9caec5")
    for index, (label, description) in enumerate(text["labels"]):
        x, y = position(index)
        draw.rounded_rectangle((x, y, x + CARD_WIDTH, y + CARD_HEIGHT), radius=14,
                               fill="#131c28", outline="#28364a", width=1)
        label_font = font(20, True)
        assert draw.textlength(label, font=label_font) <= CARD_WIDTH - 32, label
        draw.text((x + 16, y + 9), label, font=label_font, fill="#f1f5f9")
        draw.text((x + 16, y + 36), description, font=font(13), fill="#9caec5")
    draw.text((MARGIN, HEIGHT - 27), text["footer"].format(version=version), font=font(13), fill="#9caec5")
    return canvas


def load_sequences(directory):
    sequences = []
    for expression in EXPRESSIONS:
        files = sorted(directory.glob(expression + "-[0-9]*.ppm"),
                       key=lambda path: int(path.stem.rsplit("-", 1)[1]))
        if not files:
            raise RuntimeError(f"No {expression} frames; run CodexPetFirmware/tests/Run-Native.cmd --frames first.")
        frames = []
        for index, path in enumerate(files):
            if int(path.stem.rsplit("-", 1)[1]) != index * FRAME_MS:
                raise RuntimeError(f"Missing or out-of-order frame: {path.name}")
            with Image.open(path) as image:
                if image.size != (466, 466):
                    raise RuntimeError(f"Unexpected firmware display size: {path.name}")
                frames.append(image.convert("RGB").resize((DISPLAY_SIZE, DISPLAY_SIZE), Image.Resampling.LANCZOS))
        sequences.append(frames)
    return sequences


def compose(base, sequences, frame_index=0):
    result = base.copy()
    for index, frames in enumerate(sequences):
        x, y = position(index)
        offset = POSE_MS[index] // FRAME_MS
        result.paste(frames[(frame_index + offset) % len(frames)],
                     (x + (CARD_WIDTH - DISPLAY_SIZE) // 2, y + 60))
    return result


def render(directory, output):
    output.mkdir(parents=True, exist_ok=True)
    protocol = (ROOT / "CodexPetFirmware/src/codexpet_protocol.h").read_text(encoding="utf-8")
    version_match = re.search(r'kFirmwareVersion\s*=\s*"([^"]+)"', protocol)
    if version_match is None:
        raise RuntimeError("Firmware version was not found in codexpet_protocol.h.")
    version = version_match.group(1)
    sequences = load_sequences(directory)
    frame_count = max(map(len, sequences))
    report = {"firmware": version, "previewFps": 1000 // FRAME_MS,
              "frames": frame_count, "expressions": dict(zip(EXPRESSIONS, map(len, sequences))), "files": []}
    for language in TRANSLATIONS:
        base = template(language, version)
        poster = compose(base, sequences)
        poster_path = output / f"codexpet-animations-{language}.png"
        poster.save(poster_path, optimize=True)
        # One palette for every frame keeps the labels and gray eye edges stable.
        # Sampling several frames also captures all firmware confetti colors.
        sample = Image.new("RGB", (WIDTH * 3, HEIGHT))
        for column, sample_index in enumerate((0, 12, 23)):
            sample.paste(compose(base, sequences, sample_index), (column * WIDTH, 0))
        palette = sample.quantize(colors=128, method=Image.Quantize.MEDIANCUT)
        frames = [compose(base, sequences, index).quantize(palette=palette, dither=Image.Dither.NONE)
                  for index in range(frame_count)]
        animation_path = output / f"codexpet-animations-{language}.gif"
        frames[0].save(animation_path, save_all=True, append_images=frames[1:],
                       duration=FRAME_MS, loop=0, disposal=1, optimize=True)
        for path in (poster_path, animation_path):
            report["files"].append({"name": path.name, "bytes": path.stat().st_size})
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--frames", type=pathlib.Path, default=ROOT / "CodexPetFirmware/artifacts/native")
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "docs/assets")
    args = parser.parse_args()
    render(args.frames, args.output)
