#if TOOLS
using Godot;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;

namespace DSRImporter
{
    public static class ObjectLoader
    {
        public static Dictionary<string, List<ArrayMesh>> LoadObjectMeshes(
            string gameRoot,
            IEnumerable<string> modelNames,
            Dictionary<string, Texture2D> textureCache)
        {
            var result = new Dictionary<string, List<ArrayMesh>>(StringComparer.OrdinalIgnoreCase);
            string objDir = Path.Combine(gameRoot, "obj");

            foreach (string modelName in modelNames)
            {
                if (string.IsNullOrWhiteSpace(modelName) || result.ContainsKey(modelName))
                    continue;

                string objPath = Path.Combine(objDir, $"{modelName}.objbnd.dcx");
                if (!File.Exists(objPath))
                {
                    result[modelName] = new List<ArrayMesh>();
                    continue;
                }

                try
                {
                    result[modelName] = LoadSingleObjectBundle(objPath, modelName, textureCache);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[DSRImporter] Object '{modelName}' failed: {ex.Message}");
                    result[modelName] = new List<ArrayMesh>();
                }
            }

            return result;
        }

        private static List<ArrayMesh> LoadSingleObjectBundle(
            string objPath,
            string modelName,
            Dictionary<string, Texture2D> textureCache)
        {
            List<BinderFile> files = ReadBinderFiles(objPath);

            foreach (var file in files)
            {
                string name = file.Name ?? "";
                if (name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".tpf.dcx", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        TPFConverter.LoadBytesIntoCache(file.Bytes, textureCache);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[DSRImporter] Object texture '{Path.GetFileName(name)}' failed: {ex.Message}");
                    }
                }
            }

            var meshes = new List<ArrayMesh>();
            foreach (var file in files)
            {
                string name = file.Name ?? "";
                if (!name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(".flver.dcx", StringComparison.OrdinalIgnoreCase))
                    continue;

                string meshName = NormalizeModelName(name);
                var node = FLVERConverter.ToMeshInstance(file.Bytes, meshName, textureCache);
                if (node.Mesh is ArrayMesh arrayMesh)
                    meshes.Add(arrayMesh);
                node.QueueFree();
            }

            GD.Print($"[DSRImporter] Object '{modelName}': {meshes.Count} mesh(es).");
            return meshes;
        }

        private static List<BinderFile> ReadBinderFiles(string path)
        {
            try
            {
                return BND4.Read(path).Files;
            }
            catch
            {
                return BND3.Read(path).Files;
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
    }
}
#endif
