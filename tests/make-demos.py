"""Encode synthetic WPF documentation fixtures. Requires Pillow; no user content is read.

First: powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/run-features.ps1 -Demos
Then:  python tests/make-demos.py
"""
from pathlib import Path
from PIL import Image

project = Path(__file__).resolve().parents[1]
source = project / "build" / "demo-frames"
output = project / "docs" / "assets"
output.mkdir(exist_ok=True)

for name in ("rope-pin", "basket", "text-envelope", "free-placement", "horizontal-scroll"):
    frames = [Image.open(path).convert("RGB") for path in sorted((source / name).glob("*.png"))]
    durations = [int(value) for value in (source / name / "timing.txt").read_text().splitlines()]
    assert frames and len(frames) == len(durations), f"Incomplete frames: {name}"
    count = min(len(frames), 12)
    thumb_height = frames[0].height // 2
    sample = Image.new("RGB", (550, thumb_height * count))
    for j in range(count):
        index = round(j * (len(frames) - 1) / max(1, count - 1))
        sample.paste(frames[index].resize((550, thumb_height)), (0, j * thumb_height))
    # Shared colors avoid palette flicker; no dithering keeps UI lettering clean.
    palette = sample.quantize(colors=192, method=Image.Quantize.MEDIANCUT)
    converted = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    path = output / f"{name}.gif"
    converted[0].save(path, save_all=True, append_images=converted[1:], duration=durations,
                      loop=0, optimize=True, disposal=1)
    with Image.open(path) as gif:
        assert gif.size == frames[0].size and gif.n_frames > 1
    print(f"{path.name}: {path.stat().st_size:,} bytes")
