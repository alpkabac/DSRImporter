#if TOOLS
using Godot;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

// Alias to avoid clashing with Godot.Vector3 / Godot.Vector2
using SNum = System.Numerics;

namespace DSRImporter
{
    public static class FLVERConverter
    {
        /// <summary>
        /// Reads a FLVER file (raw or DCX-compressed) and returns a MeshInstance3D.
        /// Each FLVER mesh becomes a surface on the ArrayMesh.
        /// </summary>
        public static MeshInstance3D ToMeshInstance(string path, Dictionary<string, Texture2D> textureCache)
        {
            var flver = FLVER2.Read(path);
            return BuildMeshInstance(flver, System.IO.Path.GetFileNameWithoutExtension(path), textureCache);
        }

        /// <summary>
        /// Same but from a byte array — used when extracted from a BND/BXF archive.
        /// </summary>
        public static MeshInstance3D ToMeshInstance(byte[] bytes, string name, Dictionary<string, Texture2D> textureCache)
        {
            var flver = FLVER2.Read(bytes);
            return BuildMeshInstance(flver, name, textureCache);
        }

        private static MeshInstance3D BuildMeshInstance(FLVER2 flver, string name, Dictionary<string, Texture2D> textureCache)
        {
            var arrayMesh = new ArrayMesh();
            var materials = BuildMaterials(flver, textureCache);

            for (int mi = 0; mi < flver.Meshes.Count; mi++)
            {
                var mesh = flver.Meshes[mi];

                // Pick the LOD0 faceset (lowest LOD flag value = highest detail)
                var faceSet = GetLOD0FaceSet(mesh);
                if (faceSet == null) continue;

                List<int> faceIndices = faceSet.Triangulate(mesh.Vertices.Count < ushort.MaxValue);
                if (faceIndices == null || faceIndices.Count == 0) continue;

                var verts    = new List<Godot.Vector3>();
                var normals  = new List<Godot.Vector3>();
                var uvs      = new List<Godot.Vector2>();
                var indices  = new List<int>();

                // Remap: we only emit vertices that are actually referenced
                var vertRemap = new Dictionary<int, int>();

                for (int ti = 0; ti + 2 < faceIndices.Count; ti += 3)
                {
                    // Skip degenerate triangles
                    int tri0 = faceIndices[ti];
                    int tri1 = faceIndices[ti + 1];
                    int tri2 = faceIndices[ti + 2];
                    if (tri0 == tri1 || tri1 == tri2 || tri0 == tri2) continue;

                    for (int vi = 0; vi < 3; vi++)
                    {
                        int srcIdx = vi == 0 ? tri0 : vi == 1 ? tri1 : tri2;
                        if (!vertRemap.TryGetValue(srcIdx, out int dstIdx))
                        {
                            dstIdx = verts.Count;
                            vertRemap[srcIdx] = dstIdx;

                            var v = mesh.Vertices[srcIdx];

                            // DS:R → Godot coordinate conversion:
                            // DS:R is left-handed DirectX (X right, Y up, Z forward into screen)
                            // Godot is right-handed (X right, Y up, -Z forward)
                            // So negate Z.
                            verts.Add(new Godot.Vector3(v.Position.X, v.Position.Y, -v.Position.Z));

                            // Normal — comes as Vector4, W is tangent sign, ignore it here
                            var n = v.Normal;
                            normals.Add(new Godot.Vector3(n.X, n.Y, -n.Z).Normalized());

                            // UVs — DS:R uses DirectX UV (0,0 = top-left) same as Godot,
                            // but many tools flip Y. Test both and keep what looks right.
                            // v.UVs[0] is Vector3, XY = UV, Z = 0
                            if (v.UVs != null && v.UVs.Count > 0)
                                uvs.Add(new Godot.Vector2(v.UVs[0].X, v.UVs[0].Y));
                            else
                                uvs.Add(Godot.Vector2.Zero);
                        }
                        indices.Add(dstIdx);
                    }
                }

                if (verts.Count == 0) continue;

                // Reverse winding order because we flipped Z
                ReverseWindingOrder(indices);

                var surfArrays = new Godot.Collections.Array();
                surfArrays.Resize((int)Mesh.ArrayType.Max);
                surfArrays[(int)Mesh.ArrayType.Vertex]  = verts.ToArray();
                surfArrays[(int)Mesh.ArrayType.Normal]  = normals.ToArray();
                surfArrays[(int)Mesh.ArrayType.TexUV]   = uvs.ToArray();
                surfArrays[(int)Mesh.ArrayType.Index]   = indices.ToArray();

                arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfArrays);

                // Apply material if we have one for this mesh
                int matIdx = mesh.MaterialIndex;
                if (matIdx >= 0 && matIdx < materials.Count && materials[matIdx] != null)
                    arrayMesh.SurfaceSetMaterial(arrayMesh.GetSurfaceCount() - 1, materials[matIdx]);
            }

            var instance = new MeshInstance3D
            {
                Name = name,
                Mesh = arrayMesh
            };
            return instance;
        }

        // ── Material builder ─────────────────────────────────────────────────────

        private static List<StandardMaterial3D> BuildMaterials(FLVER2 flver, Dictionary<string, Texture2D> textureCache)
        {
            var result = new List<StandardMaterial3D>();

            foreach (var mat in flver.Materials)
            {
                var godotMat = new StandardMaterial3D();
                godotMat.ResourceName = mat.Name;

                // DS:R materials have named texture slots. Common ones:
                //   g_Diffuse  → albedo
                //   g_Specular → specular/roughness
                //   g_Bumpmap  → normal map
                foreach (var tex in mat.Textures)
                {
                    if (string.IsNullOrEmpty(tex.Path)) continue;

                    // tex.Path is usually something like "m10_Altar_D" — strip path & extension
                    string texName = System.IO.Path.GetFileNameWithoutExtension(tex.Path);

                    if (!textureCache.TryGetValue(texName, out var texture2D))
                    {
                        // Also try lowercase
                        textureCache.TryGetValue(texName.ToLowerInvariant(), out texture2D);
                    }

                    if (texture2D == null) continue;

                    string typeUpper = tex.ParamName?.ToUpperInvariant() ?? "";
                    if (typeUpper.Contains("DIFFUSE") || typeUpper.Contains("ALBEDO") || typeUpper == "G_DIFFUSE")
                    {
                        godotMat.AlbedoTexture = texture2D;
                    }
                    else if (typeUpper.Contains("BUMP") || typeUpper.Contains("NORMAL") || typeUpper == "G_BUMPMAP")
                    {
                        godotMat.NormalEnabled = true;
                        godotMat.NormalTexture = texture2D;
                    }
                    else if (typeUpper.Contains("SPECULAR") || typeUpper == "G_SPECULAR")
                    {
                        godotMat.MetallicTexture = texture2D;
                    }
                }

                result.Add(godotMat);
            }

            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Gets the LOD0 (highest detail) FaceSet from a mesh.
        /// LOD0 is identified by having no LodLevel flags set.
        /// Falls back to the first faceset if none is clean LOD0.
        /// </summary>
        private static FLVER2.FaceSet GetLOD0FaceSet(FLVER2.Mesh mesh)
        {
            if (mesh.FaceSets.Count == 0) return null;

            // LOD0 has no LodLevel1 or LodLevel2 flags
            foreach (var fs in mesh.FaceSets)
            {
                var flags = fs.Flags;
                bool isLodHigher = flags.HasFlag(FLVER2.FaceSet.FSFlags.LodLevel1)
                                || flags.HasFlag(FLVER2.FaceSet.FSFlags.LodLevel2);
                if (!isLodHigher)
                    return fs;
            }

            // Fallback: just take the first one
            return mesh.FaceSets[0];
        }

        private static void ReverseWindingOrder(List<int> indices)
        {
            for (int i = 0; i < indices.Count; i += 3)
            {
                // Swap index 0 and 2 in each triangle
                (indices[i], indices[i + 2]) = (indices[i + 2], indices[i]);
            }
        }
    }
}
#endif
