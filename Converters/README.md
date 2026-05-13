# DS:R to Godot 4 Importer

Godot 4 C# `EditorPlugin` that imports Dark Souls Remastered map assets into a
Godot scene. It uses the local `SoulsFormats` project for FromSoftware formats.

## Current support

- FLVER2 mesh import for static meshes
- TPF diffuse/albedo texture loading
- Split texture archives: `*.tpfbhd` + `*.tpfbdt`
- Standalone compressed texture packs: `*.tpf.dcx`
- Retail DS:R loose map folders: `MapStudioNew/*.msb` + `m??_??_??_??/*.flver.dcx`
- Older split map archives: `*.mapbhd` + `*.mapbdt`, when present
- MSB map-piece placement
- Collision placement scaffolding
- No skeletons, bone weights, or animations yet
- HKX collision extraction is currently disabled with this SoulsFormats checkout

## Retail DS:R map layout

Your `output.txt` shows this layout:

```text
DARK SOULS REMASTERED/
  map/
    MapStudioNew/
      m10_00_00_00.msb      <- map layout and placements
    m10_00_00_00/
      m2080B0A10.flver.dcx  <- loose visual mesh pieces
      h10_00_00_00.hkxbhd   <- hi-res collision header
      h10_00_00_00.hkxbdt   <- hi-res collision data
    m10/
      m10_0000.tpfbhd       <- area texture archive header
      m10_0000.tpfbdt       <- area texture archive data
```

The important mental model:

```text
MSB layout + FLVER mesh pieces + TPF textures = visible map
```

The MSB is the best entry point because it tells the importer where every map
piece belongs.

## Usage

### Importing a map

1. Open or create a scene in Godot.
2. Enable the `DSR Importer` plugin.
3. Click `Import Map Layout (MSB)`.
4. Pick an MSB from `map/MapStudioNew/`, for example:
   `...\DARK SOULS REMASTERED\map\MapStudioNew\m10_00_00_00.msb`
5. The addon will find:
   - Mesh pieces in `map\m10_00_00_00\`
   - Textures in the area texture folder, such as `map\m10\`
6. The placed map pieces appear under a `Visual` node.

See `addons/DSRImporter/MAP_STRUCTURE_GUIDE.md` for the folder breakdown.

### Importing one FLVER

1. Optional: click `Load TPF / tpfbhd+tpfbdt` and load texture packs first.
2. Click `Import FLVER file`.
3. Pick a `*.flver.dcx` file.

Single FLVER import is useful for debugging, but full maps should use the MSB
button so pieces are placed correctly.

## Coordinate system

DS:R uses a DirectX-style left-handed coordinate system. Godot uses a
right-handed coordinate system. The importer negates Z and reverses triangle
winding for mesh surfaces.

