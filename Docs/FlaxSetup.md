# Flax Setup — this machine

## Engine install

- **Flax 1.13:** `C:\Program Files (x86)\Flax\Flax_1.13`
  - Editor binaries: `C:\Program Files (x86)\Flax\Flax_1.13\Binaries\Editor\Win64`
  - Build tools: `C:\Program Files (x86)\Flax\Flax_1.13\Binaries\Tools\Flax.Build.exe`
  - Engine source: `C:\Program Files (x86)\Flax\Flax_1.13\Source\Engine`
- **Project:** `C:\Users\plame\Documents\akira\New\EventManager\EventManager\EventManager.flaxproj`

## API documentation

- **Online:** https://docs.flaxengine.com/
- **Offline (recommended):** clone https://github.com/FlaxEngine/FlaxDocs and build/view locally.

## Notes

The `Game` module is C#-only (`BuildNativeCode = false`). It builds against `FlaxEngine.CSharp.dll` supplied by the Flax editor build, so the editor must be installed on this machine to compile the module or run the editor-side play checks.
