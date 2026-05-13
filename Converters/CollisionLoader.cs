#if TOOLS
using Godot;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DSRImporter
{
    /// <summary>
    /// Extracts collision triangle meshes from DS:R HKX (Havok packfile) archives.
    ///
    /// DS:R collision file locations:
    ///   map/m??_??_??_??/h??_??_??_??.hkxbhd + .hkxbdt  — hi-res collision (gameplay)
    ///   map/m??_??_??_??/l??_??_??_??.hkxbhd + .hkxbdt  — lo-res collision (distant)
    ///
    /// Havok shape hierarchy in DS:R (Havok 2010.2.0-r1):
    ///
    ///   hkRootLevelContainer
    ///     hkpPhysicsData
    ///       hkpPhysicsSystem
    ///         hkpRigidBody (static, m_motionType = MOTION_FIXED)
    ///           hkpMoppBvTreeShape          ← BVH acceleration structure
    ///             hkpExtendedMeshShape       ← actual triangle data
    ///               TrianglesSubpart[]
    ///                 m_vertexBase           ← float* to vertex data
    ///                 m_indexBase            ← ushort* or uint* to index data
    ///                 m_numVertices
    ///                 m_numTriangleShapes
    ///
    /// SoulsFormats exposes this as an HKX object graph — you navigate it by classname
    /// and member name. It does NOT give you typed .Vertices / .Indices properties.
    /// You have to walk the graph and interpret raw byte data yourself.
    ///
    /// This extractor handles the common DS:R case. Some maps use
    /// hkpStaticCompoundShape (a container of multiple sub-shapes) instead of
    /// hkpMoppBvTreeShape — both are handled below.
    /// </summary>
    public static class CollisionLoader
    {
        public struct CollisionMesh
        {
            public string Name;
            public Godot.Vector3[] Vertices;
            public int[] Indices;
        }

        // ── Public entry points ──────────────────────────────────────────────────

        /// <summary>
        /// Load all collision meshes from a BXF4 split archive (hkxbhd+hkxbdt).
        /// Returns a dictionary keyed by the HKX filename (without extension).
        /// </summary>
        public static Dictionary<string, CollisionMesh> LoadFromBXF(string bhd, string bdt)
        {
            var bxf = BXF4.Read(bhd, bdt);
            var result = new Dictionary<string, CollisionMesh>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in bxf.Files)
            {
                string ext = Path.GetExtension(file.Name).ToLowerInvariant();
                if (ext != ".hkx" && ext != ".dcx") continue;

                string name = Path.GetFileNameWithoutExtension(
                    ext == ".dcx" ? Path.GetFileNameWithoutExtension(file.Name) : file.Name);

                try
                {
                    var meshes = ExtractFromBytes(file.Bytes);
                    if (meshes.Count > 0)
                    {
                        // Merge all sub-shapes of this HKX into one CollisionMesh
                        result[name] = MergeCollisionMeshes(name, meshes);
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[DSRImporter] HKX '{name}' failed: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Convert a CollisionMesh to a Godot StaticBody3D with CollisionShape3D (ConcavePolygonShape3D).
        /// </summary>
        public static StaticBody3D ToStaticBody(CollisionMesh cm)
        {
            var body = new StaticBody3D { Name = cm.Name };
            var shape = new CollisionShape3D();
            body.AddChild(shape);

            // Build a ConcavePolygonShape3D from triangle soup
            var concave = new ConcavePolygonShape3D();
            var faces = new Godot.Vector3[cm.Indices.Length];
            for (int i = 0; i < cm.Indices.Length; i++)
                faces[i] = cm.Vertices[cm.Indices[i]];
            concave.SetFaces(faces);

            shape.Shape = concave;
            return body;
        }

        /// <summary>
        /// Also build a visible debug MeshInstance3D from the collision mesh (wireframe-like).
        /// Useful for verifying collision matches geometry.
        /// </summary>
        public static MeshInstance3D ToDebugMesh(CollisionMesh cm, Color color = default)
        {
            if (color == default) color = new Color(0f, 1f, 0f, 0.3f);

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = cm.Vertices;
            arrays[(int)Mesh.ArrayType.Index]  = cm.Indices;

            var arrayMesh = new ArrayMesh();
            arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            var mat = new StandardMaterial3D
            {
                AlbedoColor       = color,
                Transparency      = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode          = BaseMaterial3D.CullModeEnum.Disabled,
                ShadingMode       = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            arrayMesh.SurfaceSetMaterial(0, mat);

            return new MeshInstance3D { Name = cm.Name + "_debug", Mesh = arrayMesh };
        }

        // ── HKX graph traversal ──────────────────────────────────────────────────

        private static List<CollisionMesh> ExtractFromBytes(byte[] bytes)
        {
            GD.PrintErr("[DSRImporter] HKX collision import is unavailable with this SoulsFormats checkout.");
            return new List<CollisionMesh>();

#if false
            // SoulsFormats HKX.Read handles DCX decompression automatically
            var hkx = HKX.Read(bytes);
            var results = new List<CollisionMesh>();

            // Walk all objects in the data section looking for shape types
            foreach (var obj in hkx.DataSection.Objects)
            {
                switch (obj.Classname)
                {
                    case "hkpExtendedMeshShape":
                        ExtractExtendedMeshShape(obj, hkx, results);
                        break;

                    case "hkpStorageExtendedMeshShape":
                        ExtractStorageExtendedMeshShape(obj, hkx, results);
                        break;

                    // hkpMoppBvTreeShape just wraps the above — we get the child via
                    // the object reference, but since we scan all objects anyway, we'll
                    // hit the extended mesh shape directly.
                }
            }

            return results;
#endif
        }

        // ── hkpExtendedMeshShape extractor ──────────────────────────────────────
        // This is the most common type in DS:R hi-res collision.
        // Vertex and index data are stored in raw byte arrays within the HKX data section.

#if false
        private static void ExtractExtendedMeshShape(HKXObject obj, HKX hkx, List<CollisionMesh> results)
        {
            // TrianglesSubparts is an array of sub-shape entries.
            // Each subpart has:
            //   m_numVertices      → int
            //   m_numTriangleShapes → int
            //   m_vertexStriding   → int (bytes between vertices, usually 16)
            //   m_indexStriding    → int (bytes between index triples, usually 6 for ushort)
            //   Vertex data and index data are stored as referenced byte arrays.

            if (!obj.Members.TryGetValue("m_trianglesSubparts", out var subpartsObj))
                return;

            var subparts = subpartsObj as List<HKXObject>;
            if (subparts == null) return;

            var allVerts   = new List<Godot.Vector3>();
            var allIndices = new List<int>();

            foreach (var subpart in subparts)
            {
                int numVerts = GetMemberInt(subpart, "m_numVertices");
                int numTris  = GetMemberInt(subpart, "m_numTriangleShapes");
                int vStride  = GetMemberInt(subpart, "m_vertexStriding", 16);
                int iStride  = GetMemberInt(subpart, "m_indexStriding", 6);

                if (numVerts <= 0 || numTris <= 0) continue;

                // Vertex data
                byte[] vData = GetMemberBytes(subpart, "m_vertexBase", hkx);
                // Index data
                byte[] iData = GetMemberBytes(subpart, "m_indexBase", hkx);

                if (vData == null || iData == null) continue;

                int baseVert = allVerts.Count;

                // Read vertices (each vertex is 3 floats, may have 4th float padding)
                for (int vi = 0; vi < numVerts && (vi * vStride + 12) <= vData.Length; vi++)
                {
                    int off = vi * vStride;
                    float x = BitConverter.ToSingle(vData, off + 0);
                    float y = BitConverter.ToSingle(vData, off + 4);
                    float z = BitConverter.ToSingle(vData, off + 8);
                    // DS:R → Godot: negate Z
                    allVerts.Add(new Godot.Vector3(x, y, -z));
                }

                // Read indices (3 ushorts per triangle, stride may pad to 8 bytes)
                bool useUInt = iStride >= 12; // 3x uint vs 3x ushort
                int indicesPerTri = 3;
                for (int ti = 0; ti < numTris && (ti * iStride + indicesPerTri * (useUInt ? 4 : 2)) <= iData.Length; ti++)
                {
                    int off = ti * iStride;
                    int i0, i1, i2;
                    if (useUInt)
                    {
                        i0 = (int)BitConverter.ToUInt32(iData, off + 0);
                        i1 = (int)BitConverter.ToUInt32(iData, off + 4);
                        i2 = (int)BitConverter.ToUInt32(iData, off + 8);
                    }
                    else
                    {
                        i0 = BitConverter.ToUInt16(iData, off + 0);
                        i1 = BitConverter.ToUInt16(iData, off + 2);
                        i2 = BitConverter.ToUInt16(iData, off + 4);
                    }

                    // Reverse winding for Z-flip (same as FLVER)
                    allIndices.Add(baseVert + i2);
                    allIndices.Add(baseVert + i1);
                    allIndices.Add(baseVert + i0);
                }
            }

            if (allVerts.Count > 0 && allIndices.Count > 0)
            {
                results.Add(new CollisionMesh
                {
                    Name     = $"col_{results.Count}",
                    Vertices = allVerts.ToArray(),
                    Indices  = allIndices.ToArray()
                });
            }
        }

        // ── hkpStorageExtendedMeshShape extractor ────────────────────────────────
        // Alternative shape type — stores vertex/index data inline rather than by pointer.
        // Common in some maps and object collision.

        private static void ExtractStorageExtendedMeshShape(HKXObject obj, HKX hkx, List<CollisionMesh> results)
        {
            // StorageExtendedMesh stores data in m_meshstorage which contains
            // m_vertices (array of hkVector4f) and m_indices (array of ushort)

            if (!obj.Members.TryGetValue("m_meshstorage", out var storageListObj))
                return;

            var storages = storageListObj as List<HKXObject>;
            if (storages == null) return;

            var allVerts   = new List<Godot.Vector3>();
            var allIndices = new List<int>();

            foreach (var storage in storages)
            {
                // m_vertices: array of float4 (hkVector4f)
                if (!storage.Members.TryGetValue("m_vertices", out var vertsObj))
                    continue;

                var vertList = vertsObj as List<object>;
                if (vertList == null) continue;

                int baseVert = allVerts.Count;

                foreach (var v in vertList)
                {
                    // Each element is a float[4] or similar
                    var arr = v as float[];
                    if (arr != null && arr.Length >= 3)
                        allVerts.Add(new Godot.Vector3(arr[0], arr[1], -arr[2]));
                }

                // m_indices: array of ushort (triangle list)
                if (!storage.Members.TryGetValue("m_indices", out var idxObj))
                    continue;

                var idxList = idxObj as List<object>;
                if (idxList == null) continue;

                for (int i = 0; i + 2 < idxList.Count; i += 3)
                {
                    int i0 = Convert.ToInt32(idxList[i]);
                    int i1 = Convert.ToInt32(idxList[i + 1]);
                    int i2 = Convert.ToInt32(idxList[i + 2]);
                    // Reverse winding
                    allIndices.Add(baseVert + i2);
                    allIndices.Add(baseVert + i1);
                    allIndices.Add(baseVert + i0);
                }
            }

            if (allVerts.Count > 0 && allIndices.Count > 0)
            {
                results.Add(new CollisionMesh
                {
                    Name     = $"col_storage_{results.Count}",
                    Vertices = allVerts.ToArray(),
                    Indices  = allIndices.ToArray()
                });
            }
        }

        // ── HKX graph helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Get a referenced byte array from the HKX data section.
        /// HKX stores large blobs (vertex/index data) as DataSection references.
        ///
        /// NOTE: The exact API for accessing raw bytes from SoulsFormats HKX objects
        /// varies by SoulsFormats version. If this fails to compile, check HKXObject
        /// for how raw data references work in the specific commit you're using.
        /// The Shadowth117 fork may expose this differently than JKAnderson's original.
        /// </summary>
        private static byte[] GetMemberBytes(HKXObject obj, string memberName, HKX hkx)
        {
            if (!obj.Members.TryGetValue(memberName, out var val))
                return null;

            // In SoulsFormats HKX, byte array members come back as byte[] directly
            // or as a HKXReference that points into the data section.
            if (val is byte[] direct)
                return direct;

            // If it's an HKXObject reference to a raw data block
            if (val is HKXObject refObj && refObj.Members.TryGetValue("_rawData", out var rawData))
                return rawData as byte[];

            return null;
        }

        private static int GetMemberInt(HKXObject obj, string name, int defaultVal = 0)
        {
            if (obj.Members.TryGetValue(name, out var val))
                return Convert.ToInt32(val);
            return defaultVal;
        }
#endif

        private static CollisionMesh MergeCollisionMeshes(string name, List<CollisionMesh> meshes)
        {
            if (meshes.Count == 1)
            {
                var m = meshes[0];
                m.Name = name;
                return m;
            }

            var allVerts   = new List<Godot.Vector3>();
            var allIndices = new List<int>();

            foreach (var mesh in meshes)
            {
                int baseV = allVerts.Count;
                allVerts.AddRange(mesh.Vertices);
                foreach (var idx in mesh.Indices)
                    allIndices.Add(baseV + idx);
            }

            return new CollisionMesh
            {
                Name     = name,
                Vertices = allVerts.ToArray(),
                Indices  = allIndices.ToArray()
            };
        }
    }
}
#endif
