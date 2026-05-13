#if TOOLS
using Godot;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;

namespace DSRImporter
{
    /// <summary>
    /// Reads DS:R's MSB (MapStudio Binary) format.
    ///
    /// MSB answers: WHERE does each map piece go in the world?
    /// Without MSB, every imported FLVER sits at origin — you get a pile of geometry.
    ///
    /// DS:R uses MSB1 format. The MSB contains:
    ///   Parts.MapPieces  — visual geometry instances (each references a FLVER by ModelName)
    ///   Parts.Collisions — collision mesh instances (each references an HKX by ModelName)
    ///   Parts.Objects    — interactive objects
    ///   Parts.Enemies    — NPC/enemy placements
    ///   Models           — lists of model names (FLVER filenames without extension)
    ///   Events, Regions  — triggers, sound zones, etc.
    ///
    /// MSB1 file location: map/m??_??_??_??/m??_??_??_??.msb.dcx
    /// (or .msb without compression in some versions)
    /// </summary>
    public static class MSBLoader
    {
        /// <summary>
        /// Result for a single map piece instance placement.
        /// </summary>
        public struct MapPiecePlacement
        {
            public string InstanceName;  // e.g. "m1000B0A00_0000"
            public string ModelName;     // e.g. "m1000B0A00" — keys into your FLVER dictionary
            public Transform3D Transform;
        }

        /// <summary>
        /// Result for a collision mesh instance placement.
        /// </summary>
        public struct CollisionPlacement
        {
            public string InstanceName;
            public string ModelName;     // e.g. "h0000B0A00" — hi-res, or "l0000B0A00" — lo-res
            public Transform3D Transform;
            public bool IsHiRes;         // true = h-prefixed, false = l-prefixed
        }

        /// <summary>
        /// Read an MSB file and return all map piece placements (visual geometry).
        /// </summary>
        public static List<MapPiecePlacement> ReadMapPieces(string msbPath)
        {
            var msb = MSB1.Read(msbPath);
            var result = new List<MapPiecePlacement>();

            foreach (var part in msb.Parts.MapPieces)
            {
                result.Add(new MapPiecePlacement
                {
                    InstanceName = part.Name,
                    ModelName    = part.ModelName,
                    Transform    = MakePieceTransform(part.Position, part.Rotation, part.Scale)
                });
            }

            return result;
        }

        /// <summary>
        /// Read an MSB file and return all collision instance placements.
        /// </summary>
        public static List<CollisionPlacement> ReadCollisions(string msbPath)
        {
            var msb = MSB1.Read(msbPath);
            var result = new List<CollisionPlacement>();

            foreach (var col in msb.Parts.Collisions)
            {
                string modelName = col.ModelName ?? "";
                result.Add(new CollisionPlacement
                {
                    InstanceName = col.Name,
                    ModelName    = modelName,
                    Transform    = MakePieceTransform(col.Position, col.Rotation, col.Scale),
                    IsHiRes      = modelName.StartsWith("h", StringComparison.OrdinalIgnoreCase)
                });
            }

            return result;
        }

        // ── Coordinate conversion ────────────────────────────────────────────────

        /// <summary>
        /// Convert MSB position/rotation/scale to a Godot Transform3D.
        ///
        /// MSB coordinate system: same as FLVER — DirectX left-handed.
        /// Position: X right, Y up, Z into screen → negate Z for Godot.
        /// Rotation: Euler angles in RADIANS, applied as Y then X then Z (intrinsic).
        ///           DS:R stores them as (pitch, yaw, roll) = (X, Y, Z) in radians.
        ///           Negating Z AND Y handles the handedness flip for rotation.
        ///
        /// IMPORTANT: If your map looks wrong (pieces rotated incorrectly), try:
        ///   - Swap the sign on individual rotation components
        ///   - Try EulerOrder.Yxz instead of Xyz
        ///   - The exact Euler order used by From is not officially documented
        /// </summary>
        private static Transform3D MakePieceTransform(
            System.Numerics.Vector3 pos,
            System.Numerics.Vector3 rot,
            System.Numerics.Vector3 scale)
        {
            // Position: negate Z for right-handed Godot
            var gPos = new Godot.Vector3(pos.X, pos.Y, -pos.Z);

            // Rotation: DS:R Euler angles are in radians.
            // Negate Y and Z (and keep X) for the handedness flip.
            // YXZ order matches what DSMapStudio and other tools use for DS1.
            var gRot = new Godot.Vector3(rot.X, -rot.Y, -rot.Z);
            var basis = Basis.FromEuler(gRot, EulerOrder.Yxz);

            // Scale: uniform in DS:R map pieces, but MSB stores it as Vector3
            var gScale = new Godot.Vector3(scale.X, scale.Y, scale.Z);
            basis = basis.Scaled(gScale);

            return new Transform3D(basis, gPos);
        }
    }
}
#endif
