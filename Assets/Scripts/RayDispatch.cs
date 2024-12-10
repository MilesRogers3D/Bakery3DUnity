using System;
using System.Collections;
using System.Collections.Generic;
using System.Timers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class RayDispatch : MonoBehaviour
{
    [Header("Ray Tracing Shader")]
    
    [SerializeField] private RayTracingShader _rtShader;
    [SerializeField] private RenderTexture _rtOutput;
    [SerializeField] private RawImage _imageOutput;
    [SerializeField] private int _imageSize = 512;

    private RayTracingAccelerationStructure _aStruct;
    private Camera _camera;

    public bool visualizePoints = false;
    [SerializeField] private GameObject _pointVisualizerObject;
    [SerializeField] private MeshFilter _referenceMeshFilter;
    
    public struct RaycastPoint
    {
        public Vector3 position;
        public Vector3 direction;
    };

    private int _kernelID = 0;

    private ComputeBuffer _raycastPoints;

    private ComputeBuffer _vertexBuffer;
    private ComputeBuffer _indexBuffer;
    private ComputeBuffer _outPointsBuffer;
    
    // Start is called before the first frame update
    void Start()
    {
        _camera = GetComponent<Camera>();
        SampleSurfacePoints();
        
        _rtShader.SetFloat(
            "_Zoom", 
            Mathf.Tan(Mathf.Deg2Rad * _camera.fieldOfView * 0.5F)
        );
        _rtShader.SetInt("_ImageSize", _imageSize);
        _rtShader.SetBuffer("RaycastPoints", _raycastPoints);
        
        RayTracingAccelerationStructure.RASSettings settings = new RayTracingAccelerationStructure.RASSettings();
        
        settings.layerMask = 255; // All layers
        settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
        settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;

        _aStruct = new(settings);
        _aStruct.Build();

        CreateRT();
        
        _rtShader.SetShaderPass("PathTracing");
        _rtShader.SetAccelerationStructure("_SceneAccelStruct", _aStruct);
        _rtShader.SetTexture("RenderTarget", _rtOutput);
    }

    private Timer _csTimer;

    private void LateUpdate()
    {
        _aStruct.Build();
        
        _rtShader.SetShaderPass("PathTracing");
        _rtShader.SetTexture("RenderTarget", _rtOutput);
        
        _rtShader.Dispatch(
            "MyRaygenShader", 
            _imageSize, 
            _imageSize, 
            1
        );
    }

    private void CreateRT()
    {
        RenderTextureDescriptor rtDesc = new RenderTextureDescriptor()
        {
            dimension = TextureDimension.Tex2D,
            width = _imageSize,
            height = _imageSize,
            depthBufferBits = 0,
            volumeDepth = 1,
            msaaSamples = 1,
            vrUsage = VRTextureUsage.OneEye,
            graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
            enableRandomWrite = true,
        };
        
        _rtOutput = new RenderTexture(rtDesc);
        _rtOutput.Create();

        _imageOutput.texture = _rtOutput;
    }

    private void OnDestroy()
    {
        // Cleanup accel structs
        if (_aStruct != null)
        {
            _aStruct.Release();
            _aStruct = null;
        }

        if (_aStruct != null)
        {
            _rtOutput.Release();
            _rtOutput = null;
        }
        
        // Cleanup ComputeBuffers
        CleanupBuffer(_vertexBuffer);
        CleanupBuffer(_indexBuffer);
        CleanupBuffer(_outPointsBuffer);
        CleanupBuffer(_raycastPoints);
    }

    private void CleanupBuffer(ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            buffer.Release();
            buffer = null;
        }
    }
    
    public void SampleSurfacePoints()
    {
        RaycastPoint[] raycastPoints = new RaycastPoint[_imageSize * _imageSize];

        for (int i = 0; i < raycastPoints.Length; i++)
        {
            RaycastPoint initValue;

            initValue.position = new Vector3(-9999.0F, -9999.0F, -9999.0F);
            initValue.direction = Vector3.down;

            raycastPoints[i] = initValue;
        }
        
        _referenceMeshFilter.mesh.RecalculateNormals();
        _referenceMeshFilter.mesh.RecalculateTangents();
        
        Matrix4x4 objToWorld = _referenceMeshFilter.transform.localToWorldMatrix;
        
        Vector3[] verts = _referenceMeshFilter.mesh.vertices;
        
        // Transform to world space
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i] = objToWorld.MultiplyPoint(verts[i]);
        }
        
        Vector3[] normals = _referenceMeshFilter.mesh.normals;
        
        // Transform to world space
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.Normalize(objToWorld.MultiplyVector(normals[i]));
        }

        int[] indices = _referenceMeshFilter.mesh.triangles;
        Vector2[] uvs = _referenceMeshFilter.mesh.uv;

        for (int i = 0; i < _imageSize; i++)
        {
            for (int j = 0; j < _imageSize; j++)
            {
                Vector2 sampleCoord = new Vector2(
                    (float)i / _imageSize,
                    (float)j / _imageSize
                );

                for (int triIndex = 0; triIndex < indices.Length; triIndex += 3)
                {
                    int v1 = indices[triIndex];
                    int v2 = indices[triIndex + 1];
                    int v3 = indices[triIndex + 2];

                    if (PointInTriangle(sampleCoord,
                        uvs[v1],
                        uvs[v2],
                        uvs[v3]))
                    {
                        Vector3 bary = GetBarycentricCoords(sampleCoord, uvs[v1], uvs[v2], uvs[v3]);

                        Vector3 pos = InterpFromBarycentricCoords(
                            bary,
                            verts[v1],
                            verts[v2],
                            verts[v3]
                        );
                        
                        Vector3 normalDir = InterpFromBarycentricCoords(
                            bary,
                            normals[v1],
                            normals[v2],
                            normals[v3]
                        );

                        Vector3 offsetPos = pos + (normalDir * 1.5F);

                        if (visualizePoints)
                        {
                            var dot = Instantiate(
                                _pointVisualizerObject,
                                offsetPos,
                                Quaternion.identity
                            );

                            dot.transform.LookAt(pos);
                        }

                        RaycastPoint pt;
                        
                        pt.position = offsetPos;
                        pt.direction = -normalDir;

                        int insertionIndex = j * _imageSize + i;
                        raycastPoints[insertionIndex] = pt;

                        break;
                    }
                }
            }
        }

        _raycastPoints = new ComputeBuffer(raycastPoints.Length, sizeof(float) * 6);
        _raycastPoints.SetData(raycastPoints);
    }
    
    float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
    
    bool PointInTriangle (Vector2 pt, Vector2 v1, Vector2 v2, Vector2 v3)
    {
        float d1, d2, d3;
        bool has_neg, has_pos;

        d1 = Sign(pt, v1, v2);
        d2 = Sign(pt, v2, v3);
        d3 = Sign(pt, v3, v1);

        has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(has_neg && has_pos);
    }

    Vector3 GetBarycentricCoords(
        Vector2 p,
        Vector2 a,
        Vector2 b,
        Vector2 c)
    {
        Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
        
        float d00 = Vector2.Dot(v0, v0);
        float d01 = Vector2.Dot(v0, v1);
        float d11 = Vector2.Dot(v1, v1);
        float d20 = Vector2.Dot(v2, v0);
        float d21 = Vector2.Dot(v2, v1);
        
        float denom = d00 * d11 - d01 * d01;
        
        float v = (d11 * d20 - d01 * d21) / denom;
        float w = (d00 * d21 - d01 * d20) / denom;
        float u = 1.0F - v - w;

        return new Vector3(u, v, w);
    }
    
    Vector3 InterpFromBarycentricCoords(
        Vector3 bary,
        Vector3 v1,
        Vector3 v2,
        Vector3 v3)
    {
        float x = (bary.x * v1.x) + (bary.y * v2.x) + (bary.z * v3.x);
        float y = (bary.x * v1.y) + (bary.y * v2.y) + (bary.z * v3.y);
        float z = (bary.x * v1.z) + (bary.y * v2.z) + (bary.z * v3.z);

        return new Vector3(x, y, z);
    }
}
