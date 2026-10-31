<p align="center">
  <img src="src/assets/PickSnap.png" width="72" height="72" alt="PickSnap icon">
</p>

<h1 align="center">PickSnap</h1>

<p align="center">Hang your screenshots and copied text on a clothesline along the top edge of the screen.<br>Slide it down when you need something: take it, move it, or pin it on the desktop.</p>

<p align="center">
  <a href="https://github.com/lurrrrrr/PickSnap/releases/latest"><strong>Download the latest version</strong></a>
  ·
  <a href="README.md">简体中文</a> ·
  <a href="README.en.md">English</a> ·
  <a href="README.zh-TW.md">繁體中文</a> ·
  <a href="README.ja.md">日本語</a>
</p>

<p align="center">
  <a href="https://github.com/lurrrrrr/PickSnap/releases/latest"><img src="https://img.shields.io/github/v/release/lurrrrrr/PickSnap?style=flat-square&label=release&color=2563eb" alt="Latest release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078d4?style=flat-square" alt="Windows 10 and 11">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-475569?style=flat-square" alt="MIT license"></a>
</p>

![Screenshots hanging along the top edge of the screen, with the image basket and text envelope on the right](docs/assets/shelf-light.png)

---

## One little clip, three ways to use it

Every screenshot on the line hangs from a small aluminium clip. It is not decoration — it is the only handle that image has. Copy it, pin it, move it: it all happens on that clip.

![Hold the clip above a screenshot and drag left or right to move it freely along the line](docs/assets/clip-drag.gif)

| Gesture | Result |
| --- | --- |
| Click the image | Copy to clipboard |
| Drag the image to another window | Drop it as an image file; the original stays on the line |
| Click the clip above the image | Detach from the line and drop to the desktop as a resizable, movable pinned image |
| **Hold the clip and drag left or right** | **Move the image freely along the line** |

The new order is remembered and restored on the next launch.

The line can hold far more than one screen: scroll horizontally with the wheel, drag an image to either end to keep the line scrolling, and it settles into alignment when you stop, so no image is left hanging half off the edge.

![Scroll horizontally across the whole line](docs/assets/rope-scroll.gif)

---

## Features

**Collecting**

- Watches the Windows screenshots folder and hangs new screenshots automatically
- Clipboard images and text can be collected too (separate switches for images and text)
- "Add image" lets you hang any picture already on your machine

**On the line**

- Click to copy to the clipboard; drag to another window to drop it as an image file
- Press and hold to annotate: box, arrow, freehand, text, crop, rotate — all processed locally
- Scroll horizontally with the wheel, or use the arrows at both ends; dragging an image to either end keeps the line scrolling
- Capacity is derived from screen width (24 images minimum); when it is full, the oldest image goes back to the basket
- Right-click menu: open, edit, save a copy, open location, remove to basket

**Pinned to the desktop**

Click the clip above an image (press and release) and it detaches from the line and drops into an always-on-top window that you can resize and move. Move the mouse to the top center of the pinned image and a thumbtack button appears; click it to hang the image back where it was.

**Image basket**

Keep the screenshots you do not need right now here. Select by clicking or rubber-band, then hang a whole selection back on the line. To take an image off the line, drag it onto the basket icon at the right end, or straight into the basket window if it is open.

![Drag an image onto the basket on the right to take it off the line](docs/assets/drop-basket.gif)

"Auto-clean images older than 7 days" at the top of the basket window is off by default; when enabled, screenshots older than 7 days are moved to the Recycle Bin (see the FAQ below).

**Text envelope**

Text you copy is collected automatically, with full-text search and one-click copy that preserves line breaks and indentation. After deleting, you have 5 seconds to click "Undo" and put it back. Only the most recent 100 entries are kept.

<p align="center"><img src="docs/assets/text-window.png" width="330" alt="Text envelope: search box, collected text, copy and delete, with undo after delete"></p>

**Region capture**

`Ctrl+Alt+S` starts a region selection: draw rectangles, ellipses, arrows, freehand strokes, text and mosaic right there. `Enter` confirms, `Esc` cancels.

**Appearance**

Five ropes (classic silver-grey, linen with wooden clips, porcelain, pastel, graphite). Colors can follow the system or stay light or dark. With multiple monitors, pin it to one screen or let it follow the mouse.

<details>
<summary>Dark mode</summary>

![The line in dark mode](docs/assets/shelf-dark.png)

</details>

---

## Installation

### 1. Download the installer package (recommended)

1. Open [Releases](https://github.com/lurrrrrr/PickSnap/releases/latest) and download `PickSnap-<version>-windows-x64.zip`
2. Extract it anywhere and double-click `Install.cmd`
3. The installer puts the files in `%LOCALAPPDATA%\Programs\PickSnap`, creates a desktop shortcut and starts the app

No administrator rights and no system services. To uninstall, remove it from Windows "Settings → Apps"; uninstalling also clears collected screenshots and settings.

You can also run it without `Install.cmd`: double-click `app\PickSnap.exe`. The first launch creates a desktop shortcut, and you can rebuild it any time from the tray under "More → Create desktop shortcut".

**Requirements**: Windows 10 / 11 64-bit; .NET Framework 4.8 (built into Windows 10 1809 and later); the built-in editor needs [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (built into Windows 11; some Windows 10 machines need a separate install — annotation in region capture still works without it).

### 2. Build from source

```powershell
git clone https://github.com/lurrrrrr/PickSnap.git
cd PickSnap
./scripts/build.ps1
```

Output goes to `build\`; run `build\PickSnap.exe`. Use `./scripts/build.ps1 -Package` to produce the release zip.

---

## Three steps to start

1. **Capture**: `Win+Shift+S`, `PrtScn`, or WeChat `Alt+A` — no need to save the file
2. **Slide it down**: move the mouse to the very top edge and the line slides down with your screenshots
3. **Take it**: click to copy, or drag straight into a chat window, document or folder

<details>
<summary>Shortcuts and common actions</summary>

| Action | How |
| --- | --- |
| Show / hide the line | `Ctrl+Alt+T`, or click the tray icon |
| Region capture | `Ctrl+Alt+S`, or click "Region capture" at the top of the tray panel |
| Change the capture shortcut | Tray → "More" → "Capture shortcut" |
| Copy an image | Click it |
| Open the original | Double-click it |
| Move it along the line | Hold the clip above the image and drag left or right |
| Pin to desktop / hang back | Click the clip / click the thumbtack on the pinned image |
| Annotate | Press and hold the image, or right-click → "Edit" |
| Remove to basket | Right-click → "Remove to basket", or drag onto the basket icon |
| Open the image basket | Click the basket at the right end |
| Open the text envelope | Click the envelope at the right end |
| Choose a screen | Right-click the tray icon → "Display screen" |
| Appearance, run at startup, check for updates | Right-click the tray icon |

</details>

---

## Data and privacy

Everything stays on your machine: images live in `data\Captures` under the program folder, settings and text in `data\state.json`. Nothing is uploaded; the only network request is GitHub Releases version information when you manually choose "Check for updates".

Images do not expire on their own: the line has limited room, and images that do not fit go back to the basket while the files stay on disk — you decide when to clean up (the optional auto-clean only moves expired copies to the Recycle Bin). The text envelope keeps the most recent 100 entries; older text is pushed out by new copies.

---

## FAQ

<details>
<summary>Screenshots do not appear automatically?</summary>

By default only the Windows screenshots folder is watched. `Win+Shift+S`, `PrtScn` and WeChat capture put images on the clipboard instead, so turn on "Collect images" in the tray panel under "Clipboard collection" first.

If "New images hang automatically" is off, new screenshots go to the image basket; select them and click "Hang on line".

</details>

<details>
<summary>What is the difference between "remove" and "delete"?</summary>

"Remove to basket" only takes the image off the line — the file and its record stay, and you can hang it back any time. Delete moves the collected copy to the Recycle Bin; for original screenshots in the screenshots folder it only drops PickSnap's record and leaves the file untouched.

</details>

<details>
<summary>The line keeps scrolling when I drag to an end?</summary>

Dragging the cursor to the left or right edge scrolls the line continuously; release once the image is back in view and scrolling stops immediately, settling into alignment with an image edge.

</details>

<details>
<summary>Are images cleaned up automatically?</summary>

**No images are deleted automatically by default.**

If you want it to tidy up: open the image basket and use "Auto-clean images older than 7 days" at the top right (off by default). Turning it on runs a check immediately, then checks at every launch and every 6 hours; images older than 7 days go to the Recycle Bin, so they can still be restored. Manually added originals outside the collection folder only lose their record, never the file. Age is judged from the file's current modified time, so annotated images are not cleaned by mistake. Flip the same switch to turn it off.

</details>

<details>
<summary>Which image formats are supported?</summary>

PNG, JPG, BMP, GIF, TIF. GIF and TIF use only the first frame for editing; crop, rotate and mosaic apply to the whole image.

</details>

<details>
<summary>How much can it hold, and what happens when full?</summary>

Line capacity is derived from screen width, 24 images minimum. Images that do not fit wait in the basket; nothing is discarded.

The text envelope keeps the most recent 100 entries; older ones are pushed out automatically — clipboard text is throwaway by nature, so it just rolls off.

Once the image basket passes **100 images**, you get one reminder per new screenshot. PickSnap **never deletes images on its own** — you choose what to delete and when: multi-select in the basket and delete; deleted copies go to the Recycle Bin.

</details>

<details>
<summary>How do I update?</summary>

Right-click the tray icon → "More" → "Check for updates", then restart once the download finishes. Images, settings and the run-at-startup entry are preserved.

</details>

---

## Development

C# + WPF (.NET Framework 4.8), compiled directly with `csc`, no MSBuild dependency.

```powershell
./scripts/build.ps1          # build
./scripts/build.ps1 -Package # build and create the release zip
```

Source in `src\`, third-party components in `vendor\`, build and packaging scripts in `scripts\`.

---

## Credits and license

This project is derived from the following open-source projects — thanks to their authors:

- [Tendedero](https://github.com/alejandrobujan/tendedero): the original macOS project by [Alejandro Buján](https://github.com/alejandrobujan)
- [PicRope](https://github.com/Renly1994/PicRope): the Windows port by [Renly1994](https://github.com/Renly1994)

Original copyright belongs to Alejandro Buján and is released under the MIT license; the modifications in this repository use MIT as well. Under the upstream "name and icon" clause, this project uses its own name "PickSnap / 拾帧" and its own icon. The full provenance chain and third-party notices are in [NOTICE](NOTICE); license terms are in [LICENSE](LICENSE).
