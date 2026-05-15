#if TOOLS
using Godot;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;

namespace DSRImporter
{
    public static class CharacterLoader
    {
        public static Dictionary<string, List<ArrayMesh>> LoadCharacterMeshes(
            string gameRoot,
            IEnumerable<string> modelNames,
            Dictionary<string, Texture2D> textureCache)
        {
            var result = new Dictionary<string, List<ArrayMesh>>(StringComparer.OrdinalIgnoreCase);
            string chrDir = Path.Combine(gameRoot, "chr");

            foreach (string modelName in modelNames)
            {
                if (string.IsNullOrWhiteSpace(modelName) || result.ContainsKey(modelName))
                    continue;

                string chrPath = Path.Combine(chrDir, $"{modelName}.chrbnd.dcx");
                if (!File.Exists(chrPath))
                {
                    result[modelName] = new List<ArrayMesh>();
                    continue;
                }

                try
                {
                    result[modelName] = LoadSingleCharacterBundle(chrPath, modelName, textureCache);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[DSRImporter] Character '{modelName}' failed: {ex.Message}");
                    result[modelName] = new List<ArrayMesh>();
                }
            }

            return result;
        }

        private static List<ArrayMesh> LoadSingleCharacterBundle(
            string chrPath,
            string modelName,
            Dictionary<string, Texture2D> textureCache)
        {
            List<BinderFile> files = ReadBinderFiles(chrPath);

            LoadSiblingCharacterTextures(chrPath, modelName, files, textureCache);

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
                        GD.PrintErr($"[DSRImporter] Character texture '{Path.GetFileName(name)}' failed: {ex.Message}");
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

            GD.Print($"[DSRImporter] Character '{modelName}': {meshes.Count} mesh(es).");
            return meshes;
        }

        private static void LoadSiblingCharacterTextures(
            string chrPath,
            string modelName,
            List<BinderFile> chrbndFiles,
            Dictionary<string, Texture2D> textureCache)
        {
            string chrDir = Path.GetDirectoryName(chrPath) ?? ".";
            string chrtpfbdt = Path.Combine(chrDir, $"{modelName}.chrtpfbdt");
            if (!File.Exists(chrtpfbdt))
                return;

            int loaded = 0;
            foreach (var file in chrbndFiles)
            {
                byte[] bytes = file.Bytes;
                string name = file.Name ?? "";

                bool isBxf3Header = name.EndsWith(".chrtpfbhd", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".tpfbhd", StringComparison.OrdinalIgnoreCase)
                    || StartsWithAscii(bytes, "BHF3");
                bool isBxf4Header = StartsWithAscii(bytes, "BHF4");

                if (!isBxf3Header && !isBxf4Header)
                    continue;

                try
                {
                    List<BinderFile> textureFiles = isBxf4Header
                        ? BXF4.Read(bytes, chrtpfbdt).Files
                        : BXF3.Read(bytes, chrtpfbdt).Files;

                    foreach (var textureFile in textureFiles)
                    {
                        string textureName = textureFile.Name ?? "";
                        if (!textureName.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase) &&
                            !textureName.EndsWith(".tpf.dcx", StringComparison.OrdinalIgnoreCase))
                            continue;

                        loaded += TPFConverter.LoadBytesIntoCache(textureFile.Bytes, textureCache);
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[DSRImporter] Character sibling textures '{modelName}' failed: {ex.Message}");
                }
            }

            if (loaded > 0)
                GD.Print($"[DSRImporter] Character '{modelName}': {loaded} sibling textures.");
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

        private static bool StartsWithAscii(byte[] bytes, string value)
        {
            if (bytes == null || bytes.Length < value.Length)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                if (bytes[i] != (byte)value[i])
                    return false;
            }

            return true;
        }
    }
}
#endif
