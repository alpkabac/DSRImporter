#if TOOLS
using Godot;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;

namespace DSRImporter
{
    /// <summary>
    /// Full map loader. Combines:
    ///   1. FLVER meshes from mapbhd+mapbdt
    ///   2. MSB placements (WHERE each mesh goes in the world)
    ///   3. HKX collision from hkxbhd+hkxbdt (optional)
    ///
    /// The MSB is the critical missing piece from v1 — without it every mesh sits at origin.
    /// </summary>
    public static class MapLoader
    {
        public struct MapLoadOptions
        {
            public bool LoadCollision;       // Load HKX collision meshes
            public bool ShowCollisionDebug;  // Also show visible debug collision mesh (green)
            public bool HiResCollisionOnly;  // Skip lo-res (l-prefix) collision
            public bool AutoLoadTextures;    // Attempt to auto-find and load .tpfbhd alongside
        }

        // ── Full map load (recommended) ──────────────────────────────────────────

        /// <summary>
        /// Load a complete map from its MSB file.
        /// Automatically finds the mapbhd, tpfbhd, and hkxbhd siblings.
        ///
        /// Directory layout it expects:
        ///   dir/m??_??_??_??.msb.dcx     ← pass this path
        ///   dir/m??_??_??_??.mapbhd      ← found automatically
        ///   dir/m??_??_??_??.mapbdt
        ///   dir/m??_??_??_??.tpfbhd      ← found automatically (if AutoLoadTextures)
        ///   dir/m??_??_??_??.tpfbdt
        ///   dir/h??_??_??_??.hkxbhd      ← found automatically (if LoadCollision)
        ///   dir/h??_??_??_??.hkxbdt
        /// </summary>
        public static Node3D LoadFromMSB(
            string msbPath,
            Dictionary<string, Texture2D> textureCache,
            MapLoadOptions options = default)
        {
            string dir   = Path.GetDirectoryName(msbPath) ?? ".";
            string mapId = ExtractMapId(msbPath);   // e.g. "m10_00_00_00"
            string mapDir = ResolveMapDirectory(msbPath, mapId);
            var root     = new Node3D { Name = mapId };

            // ── Step 1: Auto-load textures ────────────────────────────────────
            if (options.AutoLoadTextures)
            {
                int loaded = AutoLoadAreaTextures(mapDir, mapId, textureCache);
                GD.Print($"[DSRImporter] Auto-loaded {loaded} textures for {mapId}");
            }

            // ── Step 2: Load all FLVERs into a reusable mesh dictionary ──────
            string mapBhd = Path.Combine(mapDir, $"{mapId}.mapbhd");
            string mapBdt = Path.Combine(mapDir, $"{mapId}.mapbdt");
            var flverDict = File.Exists(mapBhd)
                ? LoadFLVERDictionary(mapBhd, mapBdt, textureCache)
                : LoadLooseFLVERDictionary(mapDir, textureCache);

            // ── Step 3: Read MSB and instance each mesh at its world transform
            bool placedFromMsb = TryPlaceMapPiecesFromMSB(msbPath, flverDict, root);
            if (!placedFromMsb)
                AddRawFLVERInstances(flverDict, root);

            // ── Step 4: Collision ─────────────────────────────────────────────
            if (options.LoadCollision)
            {
                var colRoot = LoadCollisionNode(mapDir, mapId, msbPath,
                    options.HiResCollisionOnly, options.ShowCollisionDebug);
                if (colRoot != null)
                    root.AddChild(colRoot);
            }

            return root;
        }

        private static bool TryPlaceMapPiecesFromMSB(
            string msbPath, Dictionary<string, ArrayMesh> flverDict, Node3D root)
        {
            List<MSBLoader.MapPiecePlacement> placements;
            try
            {
                placements = MSBLoader.ReadMapPieces(msbPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[DSRImporter] MSB placement read failed; importing loose FLVERs at origin instead: {ex.Message}");
                return false;
            }

            var visualRoot = new Node3D { Name = "Visual" };
            root.AddChild(visualRoot);
            int placed = 0, missing = 0;

            var missingSamples = new List<string>();
            foreach (var placement in placements)
            {
                if (!TryGetMapPieceMesh(flverDict, placement.ModelName, out var mesh))
                {
                    missing++;
                    if (missingSamples.Count < 10)
                        missingSamples.Add(placement.ModelName);
                    continue;
                }

                visualRoot.AddChild(new MeshInstance3D
                {
                    Name = placement.InstanceName,
                    Mesh = mesh,
                    Transform = placement.Transform
                });
                placed++;
            }

            GD.Print($"[DSRImporter] Visual: {placed} placed, {missing} missing models.");
            if (missingSamples.Count > 0)
                GD.Print($"[DSRImporter] Missing model samples: {string.Join(", ", missingSamples)}");
            return true;
        }

        private static bool TryGetMapPieceMesh(
            Dictionary<string, ArrayMesh> flverDict, string modelName, out ArrayMesh mesh)
        {
            if (flverDict.TryGetValue(modelName, out mesh))
                return true;

            string lower = modelName.ToLowerInvariant();
            if (flverDict.TryGetValue(lower, out mesh))
                return true;

            foreach (string candidate in BuildRemasteredModelNameCandidates(modelName))
            {
                if (flverDict.TryGetValue(candidate, out mesh))
                    return true;
            }

            return false;
        }

        private static IEnumerable<string> BuildRemasteredModelNameCandidates(string modelName)
        {
            // DS1 MSB model names are often short, e.g. m1330B0.
            // DS:R loose FLVER files often add area suffixes, e.g. m1330B0A12.flver.dcx.
            if (!modelName.StartsWith("m", StringComparison.OrdinalIgnoreCase))
                yield break;

            if (modelName.IndexOf('A') >= 0 || modelName.Length < 2)
                yield break;

            for (int area = 10; area <= 18; area++)
            {
                yield return $"{modelName}A{area}";
                yield return $"{modelName.ToLowerInvariant()}a{area}";
            }
        }

        private static void AddRawFLVERInstances(Dictionary<string, ArrayMesh> flverDict, Node3D root)
        {
            var visualRoot = new Node3D { Name = "Visual_Raw" };
            root.AddChild(visualRoot);

            foreach (var kv in flverDict)
            {
                visualRoot.AddChild(new MeshInstance3D
                {
                    Name = kv.Key,
                    Mesh = kv.Value
                });
            }

            GD.Print($"[DSRImporter] Visual_Raw: {flverDict.Count} unplaced models.");
        }

        // ── Collision ────────────────────────────────────────────────────────────

        private static Node3D LoadCollisionNode(
            string dir, string mapId, string msbPath,
            bool hiResOnly, bool showDebug)
        {
            var colRoot      = new Node3D { Name = "Collision" };
            var colPlacements = MSBLoader.ReadCollisions(msbPath);

            // DS:R HKX naming: h{mapId without 'm'} → hi-res, l{...} → lo-res
            string suffix    = mapId.Substring(1); // "10_00_00_00"
            var colMeshes    = new Dictionary<string, CollisionLoader.CollisionMesh>(StringComparer.OrdinalIgnoreCase);

            string hiResBhd = Path.Combine(dir, $"h{suffix}.hkxbhd");
            string hiResBdt = Path.Combine(dir, $"h{suffix}.hkxbdt");
            if (File.Exists(hiResBhd))
            {
                foreach (var kv in CollisionLoader.LoadFromBXF(hiResBhd, hiResBdt))
                    colMeshes[kv.Key] = kv.Value;
                GD.Print($"[DSRImporter] Hi-res collision: {colMeshes.Count} meshes loaded");
            }
            else GD.PrintErr($"[DSRImporter] No hi-res collision HKX at {hiResBhd}");

            if (!hiResOnly)
            {
                string loResBhd = Path.Combine(dir, $"l{suffix}.hkxbhd");
                string loResBdt = Path.Combine(dir, $"l{suffix}.hkxbdt");
                if (File.Exists(loResBhd))
                {
                    int before = colMeshes.Count;
                    foreach (var kv in CollisionLoader.LoadFromBXF(loResBhd, loResBdt))
                        colMeshes[kv.Key] = kv.Value;
                    GD.Print($"[DSRImporter] Lo-res collision: {colMeshes.Count - before} additional meshes");
                }
            }

            int colPlaced = 0;
            foreach (var cp in colPlacements)
            {
                if (!colMeshes.TryGetValue(cp.ModelName, out var colMesh) &&
                    !colMeshes.TryGetValue(cp.ModelName.ToLowerInvariant(), out colMesh))
                    continue;

                var body = CollisionLoader.ToStaticBody(colMesh);
                body.Name      = cp.InstanceName;
                body.Transform = cp.Transform;
                colRoot.AddChild(body);

                if (showDebug)
                    body.AddChild(CollisionLoader.ToDebugMesh(colMesh));

                colPlaced++;
            }

            GD.Print($"[DSRImporter] Collision placed: {colPlaced}");
            return colRoot;
        }

        // ── FLVER dictionary ─────────────────────────────────────────────────────

        private static Dictionary<string, ArrayMesh> LoadFLVERDictionary(
            string bhd, string bdt, Dictionary<string, Texture2D> textureCache)
        {
            var dict = new Dictionary<string, ArrayMesh>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(bhd)) { GD.PrintErr($"[DSRImporter] mapbhd not found: {bhd}"); return dict; }

            var bxf = BXF4.Read(bhd, bdt);

            foreach (var file in bxf.Files)
            {
                string fn = file.Name ?? "";
                if (!fn.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) &&
                    !fn.EndsWith(".flver.dcx", StringComparison.OrdinalIgnoreCase))
                    continue;

                string modelName = Path.GetFileNameWithoutExtension(
                    fn.EndsWith(".dcx", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetFileNameWithoutExtension(fn) : fn);
                try
                {
                    var node = FLVERConverter.ToMeshInstance(file.Bytes, modelName, textureCache);
                    dict[modelName] = node.Mesh as ArrayMesh;
                    node.QueueFree();
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[DSRImporter] FLVER '{modelName}' failed: {ex.Message}");
                }
            }

            GD.Print($"[DSRImporter] Loaded {dict.Count} FLVER models.");
            return dict;
        }

        private static Dictionary<string, ArrayMesh> LoadLooseFLVERDictionary(
            string mapDir, Dictionary<string, Texture2D> textureCache)
        {
            var dict = new Dictionary<string, ArrayMesh>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(mapDir))
            {
                GD.PrintErr($"[DSRImporter] Map directory not found: {mapDir}");
                return dict;
            }

            foreach (string path in Directory.EnumerateFiles(mapDir, "*.flver*", SearchOption.TopDirectoryOnly))
            {
                string modelName = NormalizeModelName(Path.GetFileName(path));
                try
                {
                    var node = FLVERConverter.ToMeshInstance(path, textureCache);
                    dict[modelName] = node.Mesh as ArrayMesh;
                    node.QueueFree();
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[DSRImporter] FLVER '{modelName}' failed: {ex.Message}");
                }
            }

            GD.Print($"[DSRImporter] Loaded {dict.Count} loose FLVER models from {mapDir}.");
            return dict;
        }

        // ── Legacy entry point ───────────────────────────────────────────────────

        /// <summary>Old no-MSB loader kept for compatibility. Use LoadFromMSB.</summary>
        public static Node3D LoadMapBXF(string bhd, string bdt, Dictionary<string, Texture2D> textureCache)
        {
            string mapName = Path.GetFileNameWithoutExtension(bhd).Replace(".mapbhd", "");
            var root = new Node3D { Name = mapName + "_raw" };
            var bxf  = BXF4.Read(bhd, bdt);
            int ok = 0, fail = 0;

            foreach (var file in bxf.Files)
            {
                string fn = file.Name ?? "";
                if (!fn.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) &&
                    !fn.EndsWith(".flver.dcx", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileNameWithoutExtension(
                    fn.EndsWith(".dcx", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetFileNameWithoutExtension(fn) : fn);
                try { root.AddChild(FLVERConverter.ToMeshInstance(file.Bytes, name, textureCache)); ok++; }
                catch (Exception ex) { GD.PrintErr($"[DSRImporter] '{name}': {ex.Message}"); fail++; }
            }

            GD.Print($"[DSRImporter] Raw '{mapName}': {ok} ok, {fail} failed.");
            return root;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static string ExtractMapId(string msbPath)
            => Path.GetFileName(msbPath).Replace(".msb.dcx", "").Replace(".msb", "");

        private static string ResolveMapDirectory(string msbPath, string mapId)
        {
            string msbDir = Path.GetDirectoryName(msbPath) ?? ".";

            // Retail DS:R layout: map/MapStudioNew/m10_00_00_00.msb
            // Visual pieces live next to that folder as map/m10_00_00_00/*.flver.dcx.
            string parent = Path.GetDirectoryName(msbDir) ?? msbDir;
            string siblingMapDir = Path.Combine(parent, mapId);
            if (Directory.Exists(siblingMapDir))
                return siblingMapDir;

            // Fallback for extracted/older layouts where the MSB sits beside the files.
            return msbDir;
        }

        private static int AutoLoadAreaTextures(
            string mapDir, string mapId, Dictionary<string, Texture2D> textureCache)
        {
            int loaded = 0;

            // First try same-folder split texture archives.
            string localTpfBhd = Path.Combine(mapDir, $"{mapId}.tpfbhd");
            if (File.Exists(localTpfBhd))
                loaded += TryLoadTextureArchive(localTpfBhd, textureCache);

            // Retail DS:R keeps map textures in area folders: map/m10, map/m11, etc.
            string mapRoot = Path.GetDirectoryName(mapDir) ?? mapDir;
            string areaId = mapId.Length >= 3 ? mapId.Substring(0, 3) : mapId;
            string areaDir = Path.Combine(mapRoot, areaId);
            if (!Directory.Exists(areaDir))
                return loaded;

            foreach (string tpfBhd in Directory.EnumerateFiles(areaDir, "*.tpfbhd", SearchOption.TopDirectoryOnly))
                loaded += TryLoadTextureArchive(tpfBhd, textureCache);

            foreach (string tpf in Directory.EnumerateFiles(areaDir, "*.tpf.dcx", SearchOption.TopDirectoryOnly))
                loaded += TryLoadTextureArchive(tpf, textureCache);

            return loaded;
        }

        private static int TryLoadTextureArchive(string path, Dictionary<string, Texture2D> textureCache)
        {
            try
            {
                return TPFConverter.LoadIntoCache(path, textureCache);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[DSRImporter] Skipped texture archive '{Path.GetFileName(path)}': {ex.Message}");
                return 0;
            }
        }

        private static string NormalizeModelName(string filename)
        {
            string name = Path.GetFileName(filename);
            if (name.EndsWith(".dcx", StringComparison.OrdinalIgnoreCase))
                name = Path.GetFileNameWithoutExtension(name);
            if (name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase))
                name = Path.GetFileNameWithoutExtension(name);
            return name;
        }

        public static int AutoLoadMapTextures(string mapBhd, Dictionary<string, Texture2D> textureCache)
        {
            string dir    = Path.GetDirectoryName(mapBhd) ?? ".";
            string mapId  = Path.GetFileName(mapBhd).Replace(".mapbhd", "");
            string tpfBhd = Path.Combine(dir, $"{mapId}.tpfbhd");
            return File.Exists(tpfBhd) ? TPFConverter.LoadIntoCache(tpfBhd, textureCache) : 0;
        }
    }
}
#endif
