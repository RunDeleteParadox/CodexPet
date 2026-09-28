# README media

The English and French animation galleries are rendered from the current
[firmware engine](../../CodexPetFirmware/src/avatar_engine.cpp), using the
[native software framebuffer](../../CodexPetFirmware/tests/native/M5Unified.h).
They are software previews. Physical AMOLED rendering, frame rate and audio
still need observation on the device.

- `codexpet-animations-en.gif` and `codexpet-animations-fr.gif`: nine main
  expressions at 10 frames per second. Each expression follows its own
  timeline; short reactions repeat to make them easy to inspect.
- Matching PNG files: representative stills, linked beside the GIFs for readers
  who prefer a static image.
- `hub-settings-en.png` and `hub-settings-fr.png`: the actual Hub settings window
  in each language, rendered with demo preferences and serial access disabled.
  No personal runtime settings or device identifiers are read for these images.

## Regenerate

On Windows, install the .NET 10 SDK, Python with Pillow 12.0.0, and Visual Studio
C++ Build Tools (the same native compiler used by the firmware test harness).
Then run from the repository root:

```powershell
python -m pip install Pillow==12.0.0
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Render-ReadmeMedia.ps1
```

The script runs the native firmware checks and exports fresh frames, composes
the galleries, builds the Hub and renders its settings with an isolated data
directory and Named Pipe. It does not access the physical Pet or change the
running Hub. Raw frames and runtime data stay in ignored artifact directories.

If the native frames have already been regenerated for the current firmware,
`-SkipNativeFrames` skips that step. Review both languages before committing
the six image files. Keep this regeneration code with the published assets.

## Attribution

The avatar engine derives from **KK / M5Stack StopWatch Avatar by trentct and
contributors**. CodexPet animation changes, the Hub and this gallery are
maintained by RunDeleteParadox and contributors. The project uses
AGPL-3.0-or-later; see [credits](../../CREDITS.md) and [license](../../LICENSE).
