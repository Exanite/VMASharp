using System;
using System.Diagnostics;
using System.Numerics;
using Silk.NET.Vulkan;

namespace VulkanCube;

[DebuggerDisplay("Position: {Position}, Color: {Color}")]
public unsafe struct PositionColorVertex
{
    public static VertexInputBindingDescription BindingDescription = new()
    {
        Binding = 0,
        Stride = (uint)sizeof(PositionColorVertex),
        InputRate = VertexInputRate.Vertex,
    };

    public static VertexInputAttributeDescription[] AttributeDescriptions =
    {
        new(0, 0, Format.R32G32B32Sfloat, 0),
        new(1, 0, Format.R32G32B32Sfloat, (uint)sizeof(Vector3)),
    };

    public Vector3 Position;
    public Vector3 Color;

    public PositionColorVertex(Vector3 position, Vector3 color)
    {
        Position = position;
        Color = color;
    }

    public override bool Equals(object obj)
    {
        return obj is PositionColorVertex vert && this == vert;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Position, Color);
    }

    public static bool operator ==(PositionColorVertex left, PositionColorVertex right)
    {
        return left.Position == right.Position && left.Color == right.Color;
    }

    public static bool operator !=(PositionColorVertex left, PositionColorVertex right)
    {
        return !(left == right);
    }
}

public struct InstanceData
{
    public Vector3 InstancePosition;

    public InstanceData(Vector3 position)
    {
        InstancePosition = position;
    }
}

public static class VertexData
{
    public static readonly PositionColorVertex[] TriangleData =
    {
        new()
        {
            Position = new Vector3(0, -0.5f, 0),
            Color = new Vector3(1, 0, 0),
        },
        new()
        {
            Position = new Vector3(0.5f, 0.5f, 0),
            Color = new Vector3(0, 1, 0),
        },
        new()
        {
            Position = new Vector3(-0.5f, 0.5f, 0),
            Color = new Vector3(0, 0, 1),
        },
    };

    public static readonly PositionColorVertex[] CubeData =
    {
        //Face 1
        new(new Vector3(-1, -1, -1), new Vector3(0, 0, 0)),
        new(new Vector3(1, -1, -1), new Vector3(1, 0, 0)),
        new(new Vector3(-1, 1, -1), new Vector3(0, 1, 0)),

        new(new Vector3(-1, 1, -1), new Vector3(0, 1, 0)),
        new(new Vector3(1, -1, -1), new Vector3(1, 0, 0)),
        new(new Vector3(1, 1, -1), new Vector3(1, 1, 0)),

        //Face 2
        new(new Vector3(-1, -1, 1), new Vector3(0, 0, 1)),
        new(new Vector3(-1, 1, 1), new Vector3(0, 1, 1)),
        new(new Vector3(1, -1, 1), new Vector3(1, 0, 1)),

        new(new Vector3(1, -1, 1), new Vector3(1, 0, 1)),
        new(new Vector3(-1, 1, 1), new Vector3(0, 1, 1)),
        new(new Vector3(1, 1, 1), new Vector3(1, 1, 1)),

        //Face 3
        new(new Vector3(1, 1, 1), new Vector3(1, 1, 1)),
        new(new Vector3(1, 1, -1), new Vector3(1, 1, 0)),
        new(new Vector3(1, -1, 1), new Vector3(1, 0, 1)),

        new(new Vector3(1, -1, 1), new Vector3(1, 0, 1)),
        new(new Vector3(1, 1, -1), new Vector3(1, 1, 0)),
        new(new Vector3(1, -1, -1), new Vector3(1, 0, 0)),

        //Face 4
        new(new Vector3(-1, 1, 1), new Vector3(0, 1, 1)),
        new(new Vector3(-1, -1, 1), new Vector3(0, 0, 1)),
        new(new Vector3(-1, 1, -1), new Vector3(0, 1, 0)),

        new(new Vector3(-1, 1, -1), new Vector3(0, 1, 0)),
        new(new Vector3(-1, -1, 1), new Vector3(0, 0, 1)),
        new(new Vector3(-1, -1, -1), new Vector3(0, 0, 0)),

        //Face 5
        new(new Vector3(1, 1, 1), new Vector3(1, 1, 1)),
        new(new Vector3(-1, 1, 1), new Vector3(0, 1, 1)),
        new(new Vector3(1, 1, -1), new Vector3(1, 1, 0)),

        new(new Vector3(1, 1, -1), new Vector3(1, 1, 0)),
        new(new Vector3(-1, 1, 1), new Vector3(0, 1, 1)),
        new(new Vector3(-1, 1, -1), new Vector3(0, 1, 0)),

        //Face 6
        new(new Vector3(1, -1, 1), new Vector3(1, 0, 1)),
        new(new Vector3(1, -1, -1), new Vector3(1, 0, 0)),
        new(new Vector3(-1, -1, 1), new Vector3(0, 0, 1)),

        new(new Vector3(-1, -1, 1), new Vector3(0, 0, 1)),
        new(new Vector3(1, -1, -1), new Vector3(1, 0, 0)),
        new(new Vector3(-1, -1, -1), new Vector3(0, 0, 0)),
    };

    public static readonly PositionColorVertex[] IndexedCubeData;
    //= new Vertex[]
    //{
    //    new Vertex(new Vector3(-1, -1, -1), new Vector3(0, 0, 0)),
    //    new Vertex(new Vector3(1, -1, -1), new Vector3(1, 0, 0)),
    //    new Vertex(new Vector3(-1, 1, -1), new Vector3(0, 1, 0)),
    //    new Vertex(new Vector3(1, 1, -1), new Vector3(1, 1, 0)),
    //    new Vertex(new Vector3(-1, -1, 1), new Vector3(0, 0, 1)),
    //    new Vertex(new Vector3(-1, 1, 1), new Vector3(0, 1, 1)),
    //    new Vertex(new Vector3(1, -1, 1), new Vector3(1, 0, 1)),
    //    new Vertex(new Vector3(1, 1, 1), new Vector3(1, 1, 1))
    //};

    public static readonly ushort[] CubeIndexData;
    //= new ushort[]
    //{
    //    0, 1, 2,
    //    2, 1, 3,
    //    4, 5, 6,
    //    6, 5, 7,
    //    7, 3, 6,
    //    6, 3, 1,
    //    5, 4, 2,
    //    2, 4, 0,
    //    7, 5, 3,
    //    3, 5, 2,
    //    6, 1, 4,
    //    4, 1, 0
    //};

    static VertexData()
    {
        (IndexedCubeData, CubeIndexData) = ConvertToIndexedData(CubeData);
    }

    private delegate bool VertexCompareEqual(ref PositionColorVertex v0, ref PositionColorVertex v1);

    private static readonly VertexCompareEqual DefaultCompareEqual = (ref PositionColorVertex v0, ref PositionColorVertex v1) => v0.Position == v1.Position;

    private static (PositionColorVertex[], ushort[]) ConvertToIndexedData(PositionColorVertex[] data, VertexCompareEqual? compare = null)
    {
        compare ??= DefaultCompareEqual;

        var indexedData = new PositionColorVertex[data.Length];
        var indexData = new ushort[data.Length];

        indexedData[0] = data[0];

        var vertexDataLength = 1;
        bool found;

        for (var i = 1; i < data.Length; ++i)
        {
            ref var vtx = ref data[i];
            found = false;

            for (var j = 0; j < vertexDataLength; ++j)
            {
                if (compare(ref vtx, ref indexedData[j]))
                {
                    indexData[i] = (ushort)j;
                    found = true;

                    break;
                }
            }

            if (!found)
            {
                indexData[i] = (ushort)vertexDataLength;
                indexedData[vertexDataLength++] = vtx;
            }
        }

        return (indexedData.AsSpan(0, vertexDataLength).ToArray(), indexData);
    }
}
