using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace PristonTale.Geometry;

// Reads only what collision needs from a stage SMD: the file header, the stage header counts, each material record's
// MeshState and the stage vertices and faces. Positions are Priston world units
// (fixed point / 256) in the file's own axes, which are the server's coordinates.
public static class StageCollisionReader
{
    // Check-face flag: faces of materials with this MeshState bit block and support characters.
    public const uint CheckFace = 0x00000001;

    private const int FileHeaderSize = 556;
    private const int Stage3DSize = 262260;
    private const int StageAreaCells = 256 * 256;
    private const int StageCountsOffset = 4 + StageAreaCells * 4 + 12 * 4;
    private const int MaterialGroupSize = 88;
    private const int MaterialSize = 320;
    // Material record: 104 bytes of texture slots and states, eleven scalars and the diffuse colour, then UseState at 164.
    private const int MaterialMeshStateOffset = 168;
    private const int VertexSize = 28;
    private const int FaceSize = 28;
    private const float FixedScale = 256f;

    public static CollisionMesh Read(byte[] data)
    {
        if (data.Length < FileHeaderSize + Stage3DSize)
        {
            throw new InvalidDataException("Stage SMD is shorter than its headers.");
        }

        var signature = Encoding.ASCII.GetString(data, 0, 24).TrimEnd('\0');
        if (signature is not ("SMD Stage data Ver 0.72" or "SMD Stage data Ver 0.73"))
        {
            throw new InvalidDataException($"Not a stage SMD: '{signature}'.");
        }

        var materialCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(28));
        var counts = data.AsSpan(FileHeaderSize + StageCountsOffset);
        var vertexCount = BinaryPrimitives.ReadInt32LittleEndian(counts);
        var faceCount = BinaryPrimitives.ReadInt32LittleEndian(counts[4..]);
        if (vertexCount < 0 || faceCount < 0 || materialCount < 0 || materialCount > 10_000)
        {
            throw new InvalidDataException("Stage SMD has invalid counts.");
        }

        var offset = FileHeaderSize + Stage3DSize;
        var checkFace = new bool[materialCount];
        if (materialCount > 0)
        {
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(Slice(data, offset + 8, 4));
            if (declared != materialCount)
            {
                throw new InvalidDataException($"Stage SMD declares {materialCount} materials but its group holds {declared}.");
            }

            offset += MaterialGroupSize;
            for (var index = 0; index < materialCount; index++)
            {
                var material = Slice(data, offset, MaterialSize);
                var inUse = BinaryPrimitives.ReadUInt32LittleEndian(material) != 0;
                checkFace[index] = (BinaryPrimitives.ReadUInt32LittleEndian(material[MaterialMeshStateOffset..]) & CheckFace) != 0;
                offset += MaterialSize;
                if (inUse)
                {
                    // In-use materials are followed by a length-prefixed table of texture names.
                    var tableLength = BinaryPrimitives.ReadInt32LittleEndian(Slice(data, offset, 4));
                    if (tableLength < 0)
                    {
                        throw new InvalidDataException("Stage SMD material string table has a negative length.");
                    }

                    offset += 4 + tableLength;
                }
            }
        }

        var vertices = new Vector3[vertexCount];
        for (var index = 0; index < vertexCount; index++)
        {
            var vertex = Slice(data, offset + index * VertexSize, VertexSize);
            vertices[index] = new Vector3(
                BinaryPrimitives.ReadInt32LittleEndian(vertex[8..]) / FixedScale,
                BinaryPrimitives.ReadInt32LittleEndian(vertex[12..]) / FixedScale,
                BinaryPrimitives.ReadInt32LittleEndian(vertex[16..]) / FixedScale);
        }

        offset += vertexCount * VertexSize;
        var triangles = new List<Triangle>(faceCount);
        for (var index = 0; index < faceCount; index++)
        {
            var face = Slice(data, offset + index * FaceSize, FaceSize);
            int a = BinaryPrimitives.ReadUInt16LittleEndian(face[8..]);
            int b = BinaryPrimitives.ReadUInt16LittleEndian(face[10..]);
            int c = BinaryPrimitives.ReadUInt16LittleEndian(face[12..]);
            int material = BinaryPrimitives.ReadUInt16LittleEndian(face[14..]);
            if (a < vertexCount && b < vertexCount && c < vertexCount && material < materialCount && checkFace[material])
            {
                triangles.Add(new Triangle(vertices[a], vertices[b], vertices[c]));
            }
        }

        return new CollisionMesh(triangles);
    }

    private static ReadOnlySpan<byte> Slice(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException("Stage SMD ends inside a record.");
        }

        return data.AsSpan(offset, length);
    }
}
