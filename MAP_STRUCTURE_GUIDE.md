# Dark Souls Remastered map structure

Your `output.txt` is the retail DS:R map folder. It is not the old packed
`mapbhd/mapbdt` layout the addon was originally expecting.

## The folders that matter

```text
DARK SOULS REMASTERED/
  map/
    MapStudioNew/
      m10_00_00_00.msb      <- map layout: where every piece is placed
    MapStudio/
      m10_00_00_00.msb      <- older/alternate copy of the same kind of data
    m10_00_00_00/
      m2080B0A10.flver.dcx  <- visual mesh pieces for this map
      h10_00_00_00.hkxbhd   <- hi-res collision archive header
      h10_00_00_00.hkxbdt   <- hi-res collision archive data
      l10_00_00_00.hkxbhd   <- low-res collision archive header
      l10_00_00_00.hkxbdt   <- low-res collision archive data
      m10_00_00_00.mcg      <- map graph data, not needed first
      m10_00_00_00.mcp      <- map graph data, not needed first
      m10_00_00_00.nvmbnd.dcx / .nvmdump <- navmesh, not needed first
    m10/
      m10_0000.tpfbhd       <- texture archive header
      m10_0000.tpfbdt       <- texture archive data
      GI_EnvM_m10.tpfbhd    <- more textures
      m10_9999.tpf.dcx      <- standalone texture pack
```

## What each file type means

`*.msb`
: The map layout. This is the most important file for full map import. It says:
which FLVER model is used, what each instance is named, and where it goes in
world space. Use files from `map/MapStudioNew/` first.

`*.flver.dcx`
: A compressed model/mesh file. These are the visible chunks of the level.
They do not know where they belong by themselves; the MSB places them.

MSB objects
: Doors, elevators, chests, levers, bonfires, fog gates, breakables, and other
props are separate MSB object parts. The addon currently imports these as small
yellow markers under `Objects_Placeholders`. Their names include the MSB
instance name and object model ID. Real object meshes from `obj/oXXXX.objbnd.dcx`
are a later step.

`*.tpfbhd` + `*.tpfbdt`
: A split texture archive. Pick the `.tpfbhd`; the `.tpfbdt` must sit beside it.

`*.tpf.dcx`
: A compressed standalone texture archive.

`h*.hkxbhd` + `h*.hkxbdt`
: Hi-res Havok collision archives. Useful later for physics.

`l*.hkxbhd` + `l*.hkxbdt`
: Low-res Havok collision archives. Usually not the first thing to import.

`*.mcg`, `*.mcp`, `*.nvmbnd.dcx`, `*.nvmdump`
: Navigation and map graph data. Ignore these until visuals and collision work.

`autotest/` and `breakobj/`
: Test points and breakable-object metadata. Ignore these at the beginning.

`m99_*`
: Test/debug maps. Useful for experiments, not where you should start. Some of
these have odd MSB records that SoulsFormats' DS1 parser may reject. The addon
falls back to importing their loose FLVER models at the origin when placement
data cannot be read.

## Recommended import flow

1. In Godot, open or create a scene.
2. Enable the `DSR Importer` plugin.
3. Click `Import Map Layout (MSB)`.
4. Choose a normal level file such as:
   `...\DARK SOULS REMASTERED\map\MapStudioNew\m10_00_00_00.msb`
5. The addon will:
   - Resolve the matching mesh folder: `map\m10_00_00_00\`
   - Load loose `*.flver.dcx` map pieces from that folder
   - Auto-load area textures from `map\m10\`
   - Read MSB placements and instance the meshes under a `Visual` node
   - Add MSB object markers under an `Objects_Placeholders` node

## Important mental model

A Dark Souls map is not one model file. It is a recipe:

```text
MSB layout + FLVER mesh pieces + TPF textures = visible map
```

If you import only FLVER files, you get raw chunks. If you import only the MSB,
you get placement data but no geometry. The importer now uses the MSB as the
entry point because that is the file that ties the map together.
