#if TOOLS
using Godot;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using Pfim;  // NuGet: Pfim — DDS/TGA decoder

namespace DSRImporter
{
    /// <summary>
    /// Converts TPF texture archives to Godot Texture2D resources.
    /// Supports both standalone .tpf/.tpf.dcx and split .tpfbhd+.tpfbdt archives.
    ///
    /// DS:R textures are DDS format, commonly:
    ///   BC1 (DXT1)  — diffuse without alpha
    ///   BC3 (DXT5)  — diffuse with alpha
    ///   BC5 (ATI2)  — normal maps (RG channels)
    ///   BC7         — high quality diffuse (newer)
    /// Pfim handles all of these.
    /// </summary>
    public static class TPFConverter
    {
        /// <summary>
        /// Load textures from a TPF file or split BXF archive into the cache.
        /// Returns the count of newly added textures.
        /// </summary>
        public static int LoadIntoCache(string path, Dictionary<string, Texture2D> cache)
        {
            int added = 0;
            string ext = path.ToLowerInvariant();

            if (ext.EndsWith(".tpfbhd"))
            {
                // Split archive: header + data
                string bdtPath = Path.ChangeExtension(path, ".tpfbdt");
                added += LoadSplitTPFArchive(path, bdtPath, cache);
            }
            else
            {
                // Standalone .tpf or .tpf.dcx (DCX is handled automatically by SoulsFormats)
                var tpf = TPF.Read(path);
                added += LoadTPFTextures(tpf, cache);
            }

            return added;
        }

        public static int LoadBytesIntoCache(byte[] bytes, Dictionary<string, Texture2D> cache)
        {
            var tpf = TPF.Read(bytes);
            return LoadTPFTextures(tpf, cache);
        }

        private static int LoadSplitTPFArchive(string bhdPath, string bdtPath, Dictionary<string, Texture2D> cache)
        {
            if (BXF4.IsHeader(bhdPath))
                return LoadBinderFiles(BXF4.Read(bhdPath, bdtPath).Files, cache);

            if (BXF3.IsHeader(bhdPath))
                return LoadBinderFiles(BXF3.Read(bhdPath, bdtPath).Files, cache);

            throw new InvalidDataException($"Unsupported split TPF header: {bhdPath}");
        }

        private static int LoadBinderFiles(IEnumerable<BinderFile> files, Dictionary<string, Texture2D> cache)
        {
            int added = 0;

            foreach (var file in files)
            {
                if (!file.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase) &&
                    !file.Name.EndsWith(".tpf.dcx", StringComparison.OrdinalIgnoreCase))
                    continue;

                var tpf = TPF.Read(file.Bytes);
                added += LoadTPFTextures(tpf, cache);
            }

            return added;
        }

        private static int LoadTPFTextures(TPF tpf, Dictionary<string, Texture2D> cache)
        {
            int added = 0;
            foreach (var tex in tpf.Textures)
            {
                if (cache.ContainsKey(tex.Name)) continue;

                try
                {
                    var texture2D = DDSBytesToTexture2D(tex.Bytes, tex.Name);
                    if (texture2D != null)
                    {
                        cache[tex.Name] = texture2D;
                        // Also register lowercase key for case-insensitive lookup
                        cache[tex.Name.ToLowerInvariant()] = texture2D;
                        added++;
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[DSRImporter] Failed to convert texture '{tex.Name}': {ex.Message}");
                }
            }
            return added;
        }

        /// <summary>
        /// Decode DDS bytes to a Godot Image and wrap in Texture2D.
        /// Uses Pfim for decoding — handles BC1/BC3/BC5/BC7 and uncompressed formats.
        /// </summary>
        public static Texture2D DDSBytesToTexture2D(byte[] ddsBytes, string debugName = "")
        {
            using var pfimImage = Pfimage.FromStream(new MemoryStream(ddsBytes), new PfimConfig());

            if (pfimImage == null || pfimImage.Data == null)
                throw new Exception($"Pfim failed to decode '{debugName}'");

            // Pfim decodes to BGRA or BGR by default on Windows; we need RGBA for Godot
            byte[] rgba = ConvertToRGBA(pfimImage);
            int w = pfimImage.Width;
            int h = pfimImage.Height;

            // BC5 / ATI2 normal maps only store RG — reconstruct B for Godot normal maps
            // (optional, looks flat without it but won't crash)

            var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);

            // Generate mipmaps for in-engine use
            img.GenerateMipmaps();

            var texture = ImageTexture.CreateFromImage(img);
            texture.ResourceName = debugName;
            return texture;
        }

        private static byte[] ConvertToRGBA(IImage img)
        {
            int w = img.Width;
            int h = img.Height;
            byte[] src = img.Data;
            byte[] dst = new byte[w * h * 4];

            switch (img.Format)
            {
                case ImageFormat.Rgba32:
                    // BGRA → RGBA
                    for (int i = 0; i < w * h; i++)
                    {
                        dst[i * 4 + 0] = src[i * 4 + 2]; // R
                        dst[i * 4 + 1] = src[i * 4 + 1]; // G
                        dst[i * 4 + 2] = src[i * 4 + 0]; // B
                        dst[i * 4 + 3] = src[i * 4 + 3]; // A
                    }
                    break;

                case ImageFormat.Rgb24:
                    // BGR → RGBA (fill alpha=255)
                    for (int i = 0; i < w * h; i++)
                    {
                        dst[i * 4 + 0] = src[i * 3 + 2]; // R
                        dst[i * 4 + 1] = src[i * 3 + 1]; // G
                        dst[i * 4 + 2] = src[i * 3 + 0]; // B
                        dst[i * 4 + 3] = 255;
                    }
                    break;

                default:
                    // Pfim should have already decoded compressed (BC1-BC7) to one of the above.
                    // If we hit this, fall back to raw copy and hope for the best.
                    GD.PrintErr($"[DSRImporter] Unexpected Pfim format: {img.Format} — raw copy, colours may be wrong");
                    Buffer.BlockCopy(src, 0, dst, 0, Math.Min(src.Length, dst.Length));
                    break;
            }

            return dst;
        }
    }
}
#endif
