using System.Runtime.InteropServices;
using Crucible.Core;
using Crucible.Core.Exceptions;
using Crucible.Tests.Utilities;
using Crucible.Core.Math;
using Buffer = Crucible.Core.Buffer;

namespace Crucible.Tests;
[Test]
public static class MeshTests
{
    public static Vector3[] vertices = 
    {
        // Face 1: Front (+Z)
        new Vector3(-0.5f, -0.5f,  0.5f), // 0: Bottom Left
        new Vector3(0.5f, -0.5f,  0.5f), // 1: Bottom Right
            new Vector3(0.5f,  0.5f,  0.5f), // 2: Top Right
                new Vector3(-0.5f,  0.5f,  0.5f), // 3: Top Left

        // Face 2: Back (-Z)
        new Vector3(-0.5f, -0.5f, -0.5f), // 4: Bottom Left
            new Vector3(0.5f, -0.5f, -0.5f), // 5: Bottom Right
                new Vector3(0.5f,  0.5f, -0.5f), // 6: Top Right
                    new Vector3(-0.5f,  0.5f, -0.5f), // 7: Top Left

        // Face 3: Left (-X)
        new Vector3(-0.5f, -0.5f, -0.5f), // 8: Bottom Back
            new Vector3(-0.5f, -0.5f,  0.5f), // 9: Bottom Front
                new Vector3(-0.5f,  0.5f,  0.5f), // 10: Top Front
                    new Vector3(-0.5f,  0.5f, -0.5f), // 11: Top Back

        // Face 4: Right (+X)
        new Vector3(0.5f, -0.5f,  0.5f), // 12: Bottom Front
            new Vector3(0.5f, -0.5f, -0.5f), // 13: Bottom Back
                new Vector3(0.5f,  0.5f, -0.5f), // 14: Top Back
                    new Vector3(0.5f,  0.5f,  0.5f), // 15: Top Front

        // Face 5: Bottom (-Y)
        new Vector3(-0.5f, -0.5f, -0.5f),  // 16: Front Left
            new Vector3(0.5f, -0.5f, -0.5f),  // 17: Front Right
                new Vector3(0.5f, -0.5f,  0.5f), // 18: Back Right
                    new Vector3(-0.5f, -0.5f,  0.5f), // 19: Back Left

        // Face 6: Top (+Y)
        new Vector3(-0.5f,  0.5f,  0.5f), // 20: Front Left
            new Vector3(0.5f,  0.5f,  0.5f), // 21: Front Right
                new Vector3(0.5f,  0.5f, -0.5f), // 22: Back Right
                    new Vector3(-0.5f,  0.5f, -0.5f)  // 23: Back Left
    };
    public static Vector3[] normals =
    {
        new Vector3(0.0f,  0.0f,  1.0f),
        new Vector3(0.0f,  0.0f,  1.0f),
        new Vector3(0.0f,  0.0f,  1.0f),
        new Vector3(0.0f,  0.0f,  1.0f),
        
        new Vector3(-1.0f,  0.0f,  0.0f),
        new Vector3(-1.0f,  0.0f,  0.0f),
        new Vector3(-1.0f,  0.0f,  0.0f),
        new Vector3(-1.0f,  0.0f,  0.0f),
        
        new Vector3(-1.0f,  0.0f,  0.0f),
        new Vector3(-1.0f,  0.0f,  0.0f),
        new Vector3(-1.0f,  0.0f,  0.0f),
        new Vector3(-1.0f,  0.0f,  0.0f),
        
        new Vector3(1.0f,  0.0f,  0.0f),
        new Vector3(1.0f,  0.0f,  0.0f),
        new Vector3(1.0f,  0.0f,  0.0f),
        new Vector3(1.0f,  0.0f,  0.0f),
        
        new Vector3(0.0f, -1.0f,  0.0f),
        new Vector3(0.0f, -1.0f,  0.0f),
        new Vector3(0.0f, -1.0f,  0.0f),
        new Vector3(0.0f, -1.0f,  0.0f),
        
        new Vector3(0.0f,  1.0f,  0.0f),
        new Vector3(0.0f,  1.0f,  0.0f),
        new Vector3(0.0f,  1.0f,  0.0f),
        new Vector3(0.0f,  1.0f,  0.0f),
        
    };

    public static Color[] colors =
    {
      new Color(255,255,255,255),
      new Color(255,255,255,255),
      new Color(255,255,255,255),
      new Color(255,255,255,255),
      
      new Color(255,0,0,255),
      new Color(255,0,0,255),
      new Color(255,0,0,255),
      new Color(255,0,0,255),
      
      new Color(0,0,0,255),
      new Color(0,0,0,255),
      new Color(0,0,0,255),
      new Color(0,0,0,255),
      
      new Color(0,255,0,255),
      new Color(0,255,0,255),
      new Color(0,255,0,255),
      new Color(0,255,0,255),
      
      new Color(0,0,255,255),
      new Color(0,0,255,255),
      new Color(0,0,255,255),
      new Color(0,0,255,255),
      
      new Color(255,255,0,255),
      new Color(255,255,0,255),
      new Color(255,255,0,255),
      new Color(255,255,0,255),
    };

    public static BoneWeights[] boneWeights =
    {
        new BoneWeights(new BoneWeight(0,1.0f)),
        new BoneWeights(new BoneWeight(0,1.0f)),
        new BoneWeights(new BoneWeight(0,1.0f)),
        new BoneWeights(new BoneWeight(0,1.0f)),
        
        new BoneWeights(new BoneWeight(1,1.0f)),
        new BoneWeights(new BoneWeight(1,1.0f)),
        new BoneWeights(new BoneWeight(1,1.0f)),
        new BoneWeights(new BoneWeight(1,1.0f)),
        
        new BoneWeights(new BoneWeight(2,1.0f)),
        new BoneWeights(new BoneWeight(2,1.0f)),
        new BoneWeights(new BoneWeight(2,1.0f)),
        new BoneWeights(new BoneWeight(2,1.0f)),
        
        new BoneWeights(new BoneWeight(3,1.0f)),
        new BoneWeights(new BoneWeight(3,1.0f)),
        new BoneWeights(new BoneWeight(3,1.0f)),
        new BoneWeights(new BoneWeight(3,1.0f)),
        
        new BoneWeights(new BoneWeight(4,1.0f)),
        new BoneWeights(new BoneWeight(4,1.0f)),
        new BoneWeights(new BoneWeight(4,1.0f)),
        new BoneWeights(new BoneWeight(4,1.0f)),
        
        new BoneWeights(new BoneWeight(5,1.0f)),
        new BoneWeights(new BoneWeight(5,1.0f)),
        new BoneWeights(new BoneWeight(5,1.0f)),
        new BoneWeights(new BoneWeight(5,1.0f)),
    };
    public static Vector2[] uvs =
    {
        new Vector2(0.0f, 0.0f),
        new Vector2(1.0f, 0.0f),
        new Vector2(1.0f, 1.0f),
        new Vector2(0.0f, 1.0f),
        
        new Vector2(0.0f, 0.0f),
        new Vector2(1.0f, 0.0f),
        new Vector2(1.0f, 1.0f),
        new Vector2(0.0f, 1.0f),
        
        new Vector2(0.0f, 0.0f),
        new Vector2(1.0f, 0.0f),
        new Vector2(1.0f, 1.0f),
        new Vector2(0.0f, 1.0f),
        
        new Vector2(0.0f, 0.0f),
        new Vector2(1.0f, 0.0f),
        new Vector2(1.0f, 1.0f),
        new Vector2(0.0f, 1.0f),
        
        new Vector2(0.0f, 0.0f),
        new Vector2(1.0f, 0.0f),
        new Vector2(1.0f, 1.0f),
        new Vector2(0.0f, 1.0f),
        
        new Vector2(0.0f, 0.0f),
        new Vector2(1.0f, 0.0f),
        new Vector2(1.0f, 1.0f),
        new Vector2(0.0f, 1.0f),
    };
    
    public static UInt16[] indices =
    {
        // Face 1: Front
        0, 1, 2,
        2, 3, 0,
        // Face 2: Back
        4, 5, 6,
        6, 7, 4,
        // Face 3: Left
        8, 9, 10,
        10, 11, 8,
        // Face 4: Right
        12, 13, 14,
        14, 15, 12,
        // Face 5: Bottom
        16, 17, 18,
        18, 19, 16,
        // Face 6: Top
        20, 21, 22,
        22, 23, 20
    };
    
    public static UInt32[] indices32 =
    {
        // Face 1: Front
        0, 1, 2,
        2, 3, 0,
        // Face 2: Back
        4, 5, 6,
        6, 7, 4,
        // Face 3: Left
        8, 9, 10,
        10, 11, 8,
        // Face 4: Right
        12, 13, 14,
        14, 15, 12,
        // Face 5: Bottom
        16, 17, 18,
        18, 19, 16,
        // Face 6: Top
        20, 21, 22,
        22, 23, 20
    };
        
    public static TestResult SerializeAndLoadMesh()
    {
        Mesh mesh = new Mesh(vertices, indices, normals: normals, uvs: uvs);
        
        var serialData = Mesh.Serialize(mesh);
        
        BinaryReader reader = new BinaryReader(new MemoryStream(serialData));
        var copy = Mesh.ReadFromStream(reader, new Mesh.MeshBufferAccessibility());

        if (copy.DefinedAttributes != (Mesh.VertexAttributes.Position | Mesh.VertexAttributes.Normal |
            Mesh.VertexAttributes.UV))
        {
            return TestResult.Fail("Deserialized Mesh has different vertex attributes than expected");
        }

        if (copy.VertexCount != vertices.Length || copy.IndexCount != indices.Length)
        {
            return TestResult.Fail("Deserialized Mesh has different vertex count than expected");
        }

        if (copy.VertexIndexSize != Mesh.IndexSize.UInt16)
        {
            return TestResult.Fail("Deserialized Mesh has different index size than expected");
        }

        var meshPositions = copy.GetPositionData();
        if (vertices.Length != meshPositions.Length)
        {
            return TestResult.Fail("Deserialized Mesh has different vertex count than expected");
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i] != meshPositions[i])
            {
                return TestResult.Fail("Deserialized Mesh has different vertex positions than expected");
            }
        }

        var meshNormals = copy.GetNormalData();
        if (normals.Length != meshNormals.Length)
        {
            return TestResult.Fail("Deserialized Mesh has different normals count than expected");
        }

        for (int i = 0; i < normals.Length; i++)
        {
            if (normals[i] != meshNormals[i])
            {
                return TestResult.Fail("Deserialized Mesh has different normals than expected");
            }
        }

        var meshUVs = copy.GetUVData();
        if (uvs.Length != meshUVs.Length)
        {
            return TestResult.Fail("Deserialized Mesh has different uv count than expected");
        }

        for (int i = 0; i < uvs.Length; i++)
        {
            if (uvs[i] != meshUVs[i])
            {
                return TestResult.Fail("Deserialized Mesh has different uvs than expected");
            }
        }

        var meshIndexes = copy.GetIndex16Data();
        if (indices.Length != meshIndexes.Length)
        {
            return TestResult.Fail("Deserialized Mesh has different index count than expected");
        }

        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] != meshIndexes[i])
            {
                return TestResult.Fail("Deserialized Mesh has different indexes than expected");
            }
        }
        return TestResult.Pass();
    }

    public static TestResult LoadMeshDeferred()
    {
        Mesh? deferredMesh = null;
        GPUBatchInitQueue initQueue = new GPUBatchInitQueue();
        using (var meshFile = File.Open("resources/cube.cmsh", FileMode.Open))
        {
            initQueue.QueueMesh(new BinaryReader(meshFile),new Mesh.MeshBufferAccessibility(),(Mesh mesh) => { deferredMesh = mesh; });
        }

        if (deferredMesh != null)
        {
            return TestResult.Fail("Mesh creation not deferred");
        }
        initQueue.Process();
        if (deferredMesh == null)
        {
            return TestResult.Fail("Mesh creation not executed");
        }

        if (deferredMesh.DefinedAttributes !=
            (Mesh.VertexAttributes.Position | Mesh.VertexAttributes.UV | Mesh.VertexAttributes.Normal))
        {
            return TestResult.Fail("Deferred Mesh has different attributes than expected");
        }
        
        

        var positionData = deferredMesh.GetPositionData();
        var normalData = deferredMesh.GetNormalData();
        var uvData = deferredMesh.GetUVData();
        var indexData = deferredMesh.GetIndex16Data();
        if (positionData.Length != vertices.Length)
        {
            return TestResult.Fail("Deferred Mesh has different vertex count than expected");
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i] != positionData[i])
            {
                return TestResult.Fail("Deferred Mesh has different vertex positions than expected");
            }
        }

        if (normalData.Length != normals.Length)
        {
            return TestResult.Fail("Deferred Mesh has different normals count than expected");
        }

        for (int i = 0; i < normals.Length; i++)
        {
            if (normals[i] != normalData[i])
            {
                return TestResult.Fail("Deferred Mesh has different normals than expected");
            }
        }

        if (uvData.Length != uvs.Length)
        {
            return TestResult.Fail("Deferred Mesh has different uv count than expected");
        }

        for (int i = 0; i < uvs.Length; i++)
        {
            if (uvs[i] != uvData[i])
            {
                return TestResult.Fail("Deferred Mesh has different uvs than expected");
            }
        }
        

        if (indexData.Length != indices.Length)
        {
            return TestResult.Fail("Deferred Mesh has different index count than expected");
        }
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] != indexData[i])
            {
                return TestResult.Fail($"Deferred Mesh has different indices than expected- index({i}) e:{indices[i]} a:{indexData[i]}");
            }
        }
        
        return TestResult.Pass();
    }

    public static TestResult MeshVertexCount()
    {
        Mesh mesh = new Mesh(vertices, indices, normals: normals, uvs: uvs);
        if (mesh.VertexCount != vertices.Length)
        {
            return TestResult.Fail($"Mesh has different vertex count than expected, actual: {mesh.VertexCount}");
        }

        Vector3[] verts =
        {
            new Vector3(0, 1, 0),
            new Vector3(0, -1, 0),
            new Vector3(-1, 0, 0),
        };
        UInt32[] idxs =
        {
            0, 1, 2
        };
        Mesh mesh2 = new Mesh(verts, idxs);
        if (mesh2.VertexCount != verts.Length)
        {
            return TestResult.Fail($"Mesh has different vertex count than expected, actual: {mesh2.VertexCount}");
        }
        return TestResult.Pass();
    }

    public static TestResult MeshIndexCount()
    {
        Mesh mesh = new Mesh(vertices, indices, normals: normals, uvs: uvs);
        if (mesh.IndexCount != indices.Length)
        {
            return TestResult.Fail($"Mesh has different index count than expected, actual: {mesh.IndexCount}");
        }
        Vector3[] verts =
        {
            new Vector3(0, 1, 0),
            new Vector3(0, -1, 0),
            new Vector3(-1, 0, 0),
        };
        UInt32[] idxs =
        {
            0, 1, 2
        };
        Mesh mesh2 = new Mesh(verts, idxs);
        if (mesh2.IndexCount != idxs.Length)
        {
            return TestResult.Fail($"Mesh has different index count than expected, actual: {mesh2.IndexCount}");
        }
        return TestResult.Pass();
    }

    public static TestResult MeshIndexSize()
    {
        Mesh mesh = new Mesh(vertices, indices, normals: normals, uvs: uvs);
        Vector3[] verts =
        {
            new Vector3(0, 1, 0),
            new Vector3(0, -1, 0),
            new Vector3(-1, 0, 0),
        };
        UInt32[] idxs =
        {
            0, 1, 2
        };
        Mesh mesh2 = new Mesh(verts, idxs);
        if (mesh.VertexIndexSize != Mesh.IndexSize.UInt16)
        {
            return TestResult.Fail("Mesh index size was not UInt16");
        }

        if (mesh2.VertexIndexSize != Mesh.IndexSize.UInt32)
        {
            return TestResult.Fail("Mesh index size was not UInt32");
        }
        return TestResult.Pass();
    }

    public static TestResult MeshCopyPosition()
    {
        Mesh mesh =  new Mesh(vertices, indices);
        var positions = mesh.GetPositionData();
        if (positions.Length != vertices.Length)
        {
            return TestResult.Fail($"Mesh positions has different count than expected: {positions.Length}");
        }

        for (int i = 0; i < positions.Length; i++)
        {
            if (positions[i] != vertices[i])
            {
                return TestResult.Fail($"Mesh positions has different vertices than expected: {positions[i]}");
            }
        }
        return TestResult.Pass();
    }

    public static TestResult MeshCopyNormal()
    {
        Mesh mesh =  new Mesh(vertices, indices, normals: normals);
        var meshNormals = mesh.GetNormalData();
        if (meshNormals.Length != normals.Length)
        {
            return TestResult.Fail($"Mesh normals has different count than expected: {meshNormals.Length}");
        }

        for (int i = 0; i < meshNormals.Length; i++)
        {
            if (meshNormals[i] != normals[i])
            {
                return TestResult.Fail($"Mesh normals has different value than expected: {meshNormals[i]}");
            }
        }
        return TestResult.Pass();
    }

    public static TestResult MeshCopyTangent()
    {
        //we use normals here because they're arbitrary vector 3 data
        Mesh mesh =  new Mesh(vertices, indices, tangents: normals);
        var meshTangents = mesh.GetTangentData();
        if (meshTangents.Length != normals.Length)
        {
            return TestResult.Fail($"Mesh tangents has different count than expected: {meshTangents.Length}");
        }

        for (int i = 0; i < meshTangents.Length; i++)
        {
            if (meshTangents[i] != normals[i])
            {
                return TestResult.Fail($"Mesh tangents has different value than expected: {meshTangents[i]}");
            }
        }
        return TestResult.Pass();
    }

    public static TestResult MeshCopyColor()
    {
        Mesh mesh =  new Mesh(vertices, indices, colors: colors);
        var meshColors = mesh.GetColorData();
        if (meshColors.Length != colors.Length)
        {
            return TestResult.Fail($"Mesh colors has different count than expected: {meshColors.Length}");
        }

        for (int i = 0; i < meshColors.Length; i++)
        {
            if (meshColors[i] != colors[i])
            {
                return TestResult.Fail($"Mesh colors has different value than expected: {meshColors[i]}");
            }
        }
        return TestResult.Pass();
    }

    public static TestResult MeshCopyBoneWeights()
    {
        Mesh mesh =  new Mesh(vertices, indices, boneWeights: boneWeights);
        var meshBoneWeights = mesh.GetBoneWeightsData();
        if (meshBoneWeights.Length != boneWeights.Length)
        {
            return TestResult.Fail($"Mesh bone weights has different count than expected: {meshBoneWeights.Length}");
        }

        for (int i = 0; i < meshBoneWeights.Length; i++)
        {
            if (meshBoneWeights[i] != boneWeights[i])
            {
                return TestResult.Fail($"Mesh bone weights has different value than expected: {meshBoneWeights[i]}");
            }
        }
        return TestResult.Pass();
    }

    public static TestResult MeshCopyUV()
    {
        Mesh mesh =  new Mesh(vertices, indices, uvs: uvs);
        var meshuvs = mesh.GetUVData();
        if (meshuvs.Length != uvs.Length)
        {
            return TestResult.Fail($"Mesh uvs has different count than expected: {meshuvs.Length}");
        }

        for (int i = 0; i < meshuvs.Length; i++)
        {
            if (meshuvs[i] != uvs[i])
            {
                return TestResult.Fail($"Mesh uvs has different value than expected: {meshuvs[i]}");
            }
        }
        return TestResult.Pass(); 
    }

    public static TestResult MeshCopyUV2()
    {
        Mesh mesh =  new Mesh(vertices, indices, uvs2: uvs);
        var meshuvs2 = mesh.GetUV2Data();
        if (meshuvs2.Length != uvs.Length)
        {
            return TestResult.Fail($"Mesh uvs2 has different count than expected: {meshuvs2.Length}");
        }

        for (int i = 0; i < meshuvs2.Length; i++)
        {
            if (meshuvs2[i] != uvs[i])
            {
                return TestResult.Fail($"Mesh uv2 has different value than expected: {meshuvs2[i]}");
            }
        }
        return TestResult.Pass(); 
    }
    
    public static TestResult MeshCopyUV3()
    {
        Mesh mesh =  new Mesh(vertices, indices, uvs3: uvs);
        var meshuvs3 = mesh.GetUV3Data();
        if (meshuvs3.Length != uvs.Length)
        {
            return TestResult.Fail($"Mesh uvs3 has different count than expected: {meshuvs3.Length}");
        }

        for (int i = 0; i < meshuvs3.Length; i++)
        {
            if (meshuvs3[i] != uvs[i])
            {
                return TestResult.Fail($"Mesh uv3 has different value than expected: {meshuvs3[i]}");
            }
        }
        return TestResult.Pass(); 
    }

    public static TestResult MeshCopyUV4()
    {
        Mesh mesh =  new Mesh(vertices, indices, uvs4: uvs);
        var meshuvs4 = mesh.GetUV4Data();
        if (meshuvs4.Length != uvs.Length)
        {
            return TestResult.Fail($"Mesh uvs4 has different count than expected: {meshuvs4.Length}");
        }

        for (int i = 0; i < meshuvs4.Length; i++)
        {
            if (meshuvs4[i] != uvs[i])
            {
                return TestResult.Fail($"Mesh uv4 has different value than expected: {meshuvs4[i]}");
            }
        }
        return TestResult.Pass(); 
    }

    public static TestResult EditPositions()
    {
        Mesh.MeshBufferAccessibility accessibility = new Mesh.MeshBufferAccessibility();
        accessibility.Position = Buffer.Accessibility.CpuAndGpu;
        Mesh mesh = new Mesh(vertices, indices,bufferAccessibility:accessibility);
        var positionData = mesh.PositionBuffer();
        if (positionData.Count != (UInt64)vertices.Length)
        {
            return TestResult.Fail("Mesh local position data has different count than expected");
        }
        positionData[0] = new Vector3(10, 10, 10);
        positionData[1] = new Vector3(-10, -10, 10);
        var position2 = positionData[1];
        if (position2 != new Vector3(-10, -10, 10))
        {
            return TestResult.Fail("Assignment of position data did not take");
        }
        var downloaded = mesh.GetPositionData();
        if (downloaded[0] != new Vector3(10, 10, 10) && downloaded[1] != new Vector3(-10, -10, 10))
        {
            return TestResult.Fail("Assignment of position data did not take");
        }
        return TestResult.Pass();
    }
    
    public static TestResult EditNormals()
    {
        Mesh.MeshBufferAccessibility accessibility = new Mesh.MeshBufferAccessibility();
        accessibility.Normal = Buffer.Accessibility.CpuAndGpu;
        Mesh mesh = new Mesh(vertices, indices,normals:normals,bufferAccessibility:accessibility);
        var normalData = mesh.NormalBuffer();
        if (normalData.Count != (UInt64)normals.Length)
        {
            return TestResult.Fail("Mesh local normal data has different count than expected");
        }
        normalData[0] = new Vector3(10, 10, 10);
        normalData[1] = new Vector3(-10, -10, 10);
        var normal2 = normalData[1];
        if (normal2 != new Vector3(-10, -10, 10))
        {
            return TestResult.Fail("Assignment of normal data did not take");
        }
        var downloaded = mesh.GetNormalData();
        if (downloaded[0] != new Vector3(10, 10, 10) && downloaded[1] != new Vector3(-10, -10, 10))
        {
            return TestResult.Fail("Assignment of normal data did not take");
        }
        return TestResult.Pass();
    }
    
    public static TestResult EditTangents()
    {
        Mesh.MeshBufferAccessibility accessibility = new Mesh.MeshBufferAccessibility();
        accessibility.Tangent = Buffer.Accessibility.CpuAndGpu;
        Mesh mesh = new Mesh(vertices, indices,tangents:normals,bufferAccessibility:accessibility);
        var tangentData = mesh.TangentBuffer();
        if (tangentData.Count != (UInt64)normals.Length)
        {
            return TestResult.Fail("Mesh local tangent data has different count than expected");
        }
        tangentData[0] = new Vector3(10, 10, 10);
        tangentData[1] = new Vector3(-10, -10, 10);
        var tangent2 = tangentData[1];
        if (tangent2 != new Vector3(-10, -10, 10))
        {
            return TestResult.Fail("Assignment of tangent data did not take");
        }
        var downloaded = mesh.GetTangentData();
        if (downloaded[0] != new Vector3(10, 10, 10) && downloaded[1] != new Vector3(-10, -10, 10))
        {
            return TestResult.Fail("Assignment of tangent data did not take");
        }
        return TestResult.Pass();
    }
    
    public static TestResult EditColors()
    {
        Mesh.MeshBufferAccessibility accessibility = new Mesh.MeshBufferAccessibility();
        accessibility.Color = Buffer.Accessibility.CpuAndGpu;
        Mesh mesh = new Mesh(vertices, indices,colors:colors,bufferAccessibility:accessibility);
        var colorData = mesh.ColorBuffer();
        if (colorData.Count != (UInt64)colors.Length)
        {
            return TestResult.Fail("Mesh local color data has different count than expected");
        }
        colorData[0] = new Color(10,20,30,255);
        colorData[1] = new Color(0, 255, 3,255);
        var color2 = colorData[1];
        if (color2 != new Color(0, 255, 3,255))
        {
            return TestResult.Fail("Assignment of color data did not take");
        }
        var downloaded = mesh.GetColorData();
        if (downloaded[0] != new Color(10,20,30,255) && downloaded[1] != new Color(0, 255, 3,255))
        {
            return TestResult.Fail("Assignment of color data did not take");
        }
        return TestResult.Pass();
    }
    
    public static TestResult EditBoneWeights()
    {
        Mesh.MeshBufferAccessibility accessibility = new Mesh.MeshBufferAccessibility();
        accessibility.BoneWeight = Buffer.Accessibility.CpuAndGpu;
        Mesh mesh = new Mesh(vertices, indices,boneWeights:boneWeights,bufferAccessibility:accessibility);
        var boneWeightsData = mesh.BoneWeightsBuffer();
        if (boneWeightsData.Count != (UInt64)boneWeights.Length)
        {
            return TestResult.Fail("Mesh local bone weights data has different count than expected");
        }
        boneWeightsData[0] = new BoneWeights(new BoneWeight(5,1));
        boneWeightsData[1] = new BoneWeights(new BoneWeight(4,.5f), new BoneWeight(5,.5f));
        var boneWeights2 = boneWeightsData[1];
        if (boneWeights2 != new BoneWeights(new BoneWeight(4,.5f), new BoneWeight(5,.5f)))
        {
            return TestResult.Fail("Assignment of bone weights data did not take");
        }
        var downloaded = mesh.GetBoneWeightsData();
        if (downloaded[0] != new BoneWeights(new BoneWeight(5,1)) && downloaded[1] != new BoneWeights(new BoneWeight(4,.5f), new BoneWeight(5,.5f)))
        {
            return TestResult.Fail("Assignment of bone weights data did not take");
        }
        return TestResult.Pass();
    }
    
    public static TestResult EditUVs()
    {
        Mesh.MeshBufferAccessibility accessibility = new Mesh.MeshBufferAccessibility();
        accessibility.Uv = Buffer.Accessibility.CpuAndGpu;
        Mesh mesh = new Mesh(vertices, indices,uvs:uvs,bufferAccessibility:accessibility);
        var uvData = mesh.UVBuffer();
        if (uvData.Count != (UInt64)uvs.Length)
        {
            return TestResult.Fail("Mesh local uv data has different count than expected");
        }
        uvData[0] = new Vector2(10,10);
        uvData[1] = new Vector2(-10, -10);
        var uvI = uvData[1];
        if (uvI != new Vector2(-10, -10))
        {
            return TestResult.Fail("Assignment of uv data did not take");
        }
        var downloaded = mesh.GetUVData();
        if (downloaded[0] != new Vector2(10, 10) && downloaded[1] != new Vector2(-10, -10))
        {
            return TestResult.Fail("Assignment of uv data did not take");
        }
        return TestResult.Pass();
    }
    
    public static TestResult EditUV2s()
    {
        Mesh.MeshBufferAccessibility accessibility = new Mesh.MeshBufferAccessibility();
        accessibility.Uv2 = Buffer.Accessibility.CpuAndGpu;
        Mesh mesh = new Mesh(vertices, indices,uvs2:uvs,bufferAccessibility:accessibility);
        var uvData = mesh.UV2Buffer();
        if (uvData.Count != (UInt64)uvs.Length)
        {
            return TestResult.Fail("Mesh local uv2 data has different count than expected");
        }
        uvData[0] = new Vector2(10,10);
        uvData[1] = new Vector2(-10, -10);
        var uvI = uvData[1];
        if (uvI != new Vector2(-10, -10))
        {
            return TestResult.Fail("Assignment of uv2 data did not take");
        }
        var downloaded = mesh.GetUV2Data();
        if (downloaded[0] != new Vector2(10, 10) && downloaded[1] != new Vector2(-10, -10))
        {
            return TestResult.Fail("Assignment of uv2 data did not take");
        }
        return TestResult.Pass();
    }
    
    public static TestResult EditUV3s()
    {
        Mesh.MeshBufferAccessibility accessibility = new Mesh.MeshBufferAccessibility();
        accessibility.Uv3 = Buffer.Accessibility.CpuAndGpu;
        Mesh mesh = new Mesh(vertices, indices,uvs3:uvs,bufferAccessibility:accessibility);
        var uvData = mesh.UV3Buffer();
        if (uvData.Count != (UInt64)uvs.Length)
        {
            return TestResult.Fail("Mesh local uv3 data has different count than expected");
        }
        uvData[0] = new Vector2(10,10);
        uvData[1] = new Vector2(-10, -10);
        var uvI = uvData[1];
        if (uvI != new Vector2(-10, -10))
        {
            return TestResult.Fail("Assignment of uv3 data did not take");
        }
        var downloaded = mesh.GetUV3Data();
        if (downloaded[0] != new Vector2(10, 10) && downloaded[1] != new Vector2(-10, -10))
        {
            return TestResult.Fail("Assignment of uv3 data did not take");
        }
        return TestResult.Pass();
    }
    
    public static TestResult EditUV4s()
    {
        Mesh.MeshBufferAccessibility accessibility = new Mesh.MeshBufferAccessibility();
        accessibility.Uv4 = Buffer.Accessibility.CpuAndGpu;
        Mesh mesh = new Mesh(vertices, indices,uvs4:uvs,bufferAccessibility:accessibility);
        var uvData = mesh.UV4Buffer();
        if (uvData.Count != (UInt64)uvs.Length)
        {
            return TestResult.Fail("Mesh local uv4 data has different count than expected");
        }
        uvData[0] = new Vector2(10,10);
        uvData[1] = new Vector2(-10, -10);
        var uvI = uvData[1];
        if (uvI != new Vector2(-10, -10))
        {
            return TestResult.Fail("Assignment of uv4 data did not take");
        }
        var downloaded = mesh.GetUV4Data();
        if (downloaded[0] != new Vector2(10, 10) && downloaded[1] != new Vector2(-10, -10))
        {
            return TestResult.Fail("Assignment of uv4 data did not take");
        }
        return TestResult.Pass();
    }

    public static TestResult EditIndexes16()
    {
        Mesh.MeshBufferAccessibility accessibility = new Mesh.MeshBufferAccessibility();
        accessibility.Index = Buffer.Accessibility.CpuAndGpu;
        Mesh mesh = new Mesh(vertices, indices,bufferAccessibility:accessibility);
        var indexData = mesh.Index16Buffer();
        if (indexData.Count != (UInt64)indices.Length)
        {
            return TestResult.Fail("Mesh local index data has different count than expected");
        }
        indexData[0] = 4;
        indexData[1] = 5;
        var indexI = indexData[1];
        if (indexI != 5)
        {
            return TestResult.Fail("Assignment of index data did not take");
        }
        var downloaded = mesh.GetIndex16Data();
        if (downloaded[0] != 4 && downloaded[1] != 5)
        {
            return TestResult.Fail("Assignment of index data did not take");
        }
        return TestResult.Pass();
    }
    
    public static TestResult EditIndexes32()
    {
        Mesh.MeshBufferAccessibility accessibility = new Mesh.MeshBufferAccessibility();
        accessibility.Index = Buffer.Accessibility.CpuAndGpu;
        Mesh mesh = new Mesh(vertices, indices32,bufferAccessibility:accessibility);
        var indexData = mesh.Index32Buffer();
        if (indexData.Count != (UInt64)indices32.Length)
        {
            return TestResult.Fail("Mesh local index data has different count than expected");
        }
        indexData[0] = 4;
        indexData[1] = 5;
        var indexI = indexData[1];
        if (indexI != 5)
        {
            return TestResult.Fail("Assignment of index data did not take");
        }
        var downloaded = mesh.GetIndex32Data();
        if (downloaded[0] != 4 && downloaded[1] != 5)
        {
            return TestResult.Fail("Assignment of index data did not take");
        }
        return TestResult.Pass();
    }
    
    public static TestResult EditNormalsNoBuffer()
    {
        Mesh mesh = new Mesh(vertices, indices);
        try
        {
            var buffer = mesh.NormalBuffer();
        }
        catch (NullReferenceException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditTangentsNoBuffer()
    {
        Mesh mesh = new Mesh(vertices, indices);
        try
        {
            var buffer = mesh.TangentBuffer();
        }
        catch (NullReferenceException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditColorsNoBuffer()
    {
        Mesh mesh = new Mesh(vertices, indices);
        try
        {
            var buffer = mesh.ColorBuffer();
        }
        catch (NullReferenceException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditBoneWeightsNoBuffer()
    {
        Mesh mesh = new Mesh(vertices, indices);
        try
        {
            var buffer = mesh.BoneWeightsBuffer();
        }
        catch (NullReferenceException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditUVsNoBuffer()
    {
        Mesh mesh = new Mesh(vertices, indices);
        try
        {
            var buffer = mesh.UVBuffer();
        }
        catch (NullReferenceException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditUV2sNoBuffer()
    {
        Mesh mesh = new Mesh(vertices, indices);
        try
        {
            var buffer = mesh.UV2Buffer();
        }
        catch (NullReferenceException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditUV3sNoBuffer()
    {
        Mesh mesh = new Mesh(vertices, indices);
        try
        {
            var buffer = mesh.UV3Buffer();
        }
        catch (NullReferenceException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditUV4sNoBuffer()
    {
        Mesh mesh = new Mesh(vertices, indices);
        try
        {
            var buffer = mesh.UV4Buffer();
        }
        catch (NullReferenceException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    
    public static TestResult EditPositionsInaccessibleBuffer()
    {
        Mesh mesh = new Mesh(vertices,indices);
        try
        {
            var buffer = mesh.PositionBuffer();
        }
        catch (BufferNotLocalException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditNormalsInaccessibleBuffer()
    {
        Mesh mesh = new Mesh(vertices,indices, normals:normals);
        try
        {
            var buffer = mesh.NormalBuffer();
        }
        catch (BufferNotLocalException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditTangentsInaccessibleBuffer()
    {
        Mesh mesh = new Mesh(vertices,indices, tangents:normals);
        try
        {
            var buffer = mesh.TangentBuffer();
        }
        catch (BufferNotLocalException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditColorsInaccessibleBuffer()
    {
        Mesh mesh = new Mesh(vertices,indices, colors:colors);
        try
        {
            var buffer = mesh.ColorBuffer();
        }
        catch (BufferNotLocalException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditBoneWeightsInaccessibleBuffer()
    {
        Mesh mesh = new Mesh(vertices,indices, boneWeights:boneWeights);
        try
        {
            var buffer = mesh.BoneWeightsBuffer();
        }
        catch (BufferNotLocalException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditUVsInaccessibleBuffer()
    {
        Mesh mesh = new Mesh(vertices,indices, uvs:uvs);
        try
        {
            var buffer = mesh.UVBuffer();
        }
        catch (BufferNotLocalException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditUV2sInaccessibleBuffer()
    {
        Mesh mesh = new Mesh(vertices,indices, uvs2:uvs);
        try
        {
            var buffer = mesh.UV2Buffer();
        }
        catch (BufferNotLocalException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditUV3sInaccessibleBuffer()
    {
        Mesh mesh = new Mesh(vertices,indices, uvs3:uvs);
        try
        {
            var buffer = mesh.UV3Buffer();
        }
        catch (BufferNotLocalException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditUV4sInaccessibleBuffer()
    {
        Mesh mesh = new Mesh(vertices,indices, uvs4:uvs);
        try
        {
            var buffer = mesh.UV4Buffer();
        }
        catch (BufferNotLocalException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditIndexes16InaccessibleBuffer()
    {
        Mesh mesh = new Mesh(vertices,indices);
        try
        {
            var buffer = mesh.Index16Buffer();
        }
        catch (BufferNotLocalException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
    
    public static TestResult EditIndexes32InaccessibleBuffer()
    {
        Mesh mesh = new Mesh(vertices,indices32);
        try
        {
            var buffer = mesh.Index32Buffer();
        }
        catch (BufferNotLocalException e)
        {
            return TestResult.Pass();
        }
        return TestResult.Fail("Expected to throw an exception");
    }
}