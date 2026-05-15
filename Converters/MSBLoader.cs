#if TOOLS
using Godot;
using SoulsFormats;
using System;
using System.Collections.Generic;

namespace DSRImporter
{
    /// <summary>
    /// Reads DS:R MSB map layout data.
    /// </summary>
    public static class MSBLoader
    {
        public struct MapPiecePlacement
        {
            public string InstanceName;
            public string ModelName;
            public Transform3D Transform;
        }

        public struct CollisionPlacement
        {
            public string InstanceName;
            public string ModelName;
            public Transform3D Transform;
            public bool IsHiRes;
        }

        public struct ObjectPlacement
        {
            public string InstanceName;
            public string ModelName;
            public string CollisionName;
            public int EntityID;
            public short InitAnimID;
            public Transform3D Transform;
        }

        public struct EnemyPlacement
        {
            public string InstanceName;
            public string ModelName;
            public string CollisionName;
            public int EntityID;
            public int ThinkParamID;
            public int NPCParamID;
            public int TalkID;
            public int CharaInitID;
            public int InitAnimID;
            public Transform3D Transform;
        }

        public static List<MapPiecePlacement> ReadMapPieces(string msbPath)
        {
            var msb = MSB1.Read(msbPath);
            var result = new List<MapPiecePlacement>();

            foreach (var part in msb.Parts.MapPieces)
            {
                result.Add(new MapPiecePlacement
                {
                    InstanceName = part.Name,
                    ModelName = part.ModelName,
                    Transform = MakePartTransform(part.Position, part.Rotation, part.Scale)
                });
            }

            return result;
        }

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
                    ModelName = modelName,
                    Transform = MakePartTransform(col.Position, col.Rotation, col.Scale),
                    IsHiRes = modelName.StartsWith("h", StringComparison.OrdinalIgnoreCase)
                });
            }

            return result;
        }

        public static List<ObjectPlacement> ReadObjects(string msbPath)
        {
            var msb = MSB1.Read(msbPath);
            var result = new List<ObjectPlacement>();

            foreach (var obj in msb.Parts.Objects)
            {
                result.Add(new ObjectPlacement
                {
                    InstanceName = obj.Name,
                    ModelName = obj.ModelName ?? "",
                    CollisionName = obj.CollisionName ?? "",
                    EntityID = obj.EntityID,
                    InitAnimID = obj.InitAnimID,
                    Transform = MakePartTransform(obj.Position, obj.Rotation, obj.Scale)
                });
            }

            return result;
        }

        public static List<EnemyPlacement> ReadEnemies(string msbPath)
        {
            var msb = MSB1.Read(msbPath);
            var result = new List<EnemyPlacement>();

            foreach (var enemy in msb.Parts.Enemies)
            {
                result.Add(new EnemyPlacement
                {
                    InstanceName = enemy.Name,
                    ModelName = enemy.ModelName ?? "",
                    CollisionName = enemy.CollisionName ?? "",
                    EntityID = enemy.EntityID,
                    ThinkParamID = enemy.ThinkParamID,
                    NPCParamID = enemy.NPCParamID,
                    TalkID = enemy.TalkID,
                    CharaInitID = enemy.CharaInitID,
                    InitAnimID = enemy.InitAnimID,
                    Transform = MakePartTransform(enemy.Position, enemy.Rotation, enemy.Scale)
                });
            }

            return result;
        }

        private static Transform3D MakePartTransform(
            System.Numerics.Vector3 pos,
            System.Numerics.Vector3 rot,
            System.Numerics.Vector3 scale)
        {
            var godotPosition = new Godot.Vector3(pos.X, pos.Y, -pos.Z);
            var godotRotation = new Godot.Vector3(rot.X, -rot.Y, -rot.Z);
            var basis = Basis.FromEuler(godotRotation, EulerOrder.Yxz);
            basis = basis.Scaled(new Godot.Vector3(scale.X, scale.Y, scale.Z));
            return new Transform3D(basis, godotPosition);
        }
    }
}
#endif
