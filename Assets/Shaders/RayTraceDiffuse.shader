Shader "DXR/RayTraceDiffuse"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "DisableBatching" = "True"    
        }
        LOD 200
        
        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv0 : TEXCOORD0;
                float3 normal : NORMAL;
                float3 normalTS : TANGENT;
                float4 vertex : SV_POSITION;
            };

            float4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.normalTS = mul(
                    transpose(CreateTangentToWorldPerVertex(v.normal, v.tangent.xyz, v.tangent.w)),
                    v.normal
                );
                o.uv0 = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = _Color;

                float3 lightPos = _WorldSpaceLightPos0.xyz;
                float3 lightDir = normalize(lightPos - i.vertex).xyz;
                float3 norm = normalize(i.normal);

                float diffuse = max(dot(norm, lightDir), 0.0);

                return float4(i.normalTS * 0.5 + 0.5, 1.0);
                //return col * diffuse;
            }

            ENDCG
        }
    }

    SubShader
    {
        Pass
        {
            Name "PathTracing"
            Tags
            {
                "LightMode" = "RayTracing"
            }
            
            HLSLPROGRAM

            #include "UnityRayTracingMeshUtils.cginc"
            #include "Assets/Shaders/RayPayload.hlsl"

            #pragma raytracing test

            float4 _Color;

            struct AttributeData
            {
                float2 barycentrics;
            };

            struct Vertex
            {
                float3 position;
                float3 normal;
                float3 normalTS;
                float2 uv;
            };

            Vertex FetchVertex(uint vertexIndex)
            {
                Vertex v;
                v.position = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
                v.normal = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
                v.uv = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
                v.normalTS = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeTangent);
                
                return v;
            }

            Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
            {
                Vertex v;
                #define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
                INTERPOLATE_ATTRIBUTE(position);
                INTERPOLATE_ATTRIBUTE(normal);
                INTERPOLATE_ATTRIBUTE(normalTS);
                INTERPOLATE_ATTRIBUTE(uv);
                return v;
            }

            [shader("closesthit")]
            void ClosestHitMain(
                inout RayPayload payload : SV_RayPayload,
                AttributeData attribs : SV_IntersectionAttributes)
            {
                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                Vertex v0 = FetchVertex(triangleIndices.x);
                Vertex v1 = FetchVertex(triangleIndices.y);
                Vertex v2 = FetchVertex(triangleIndices.z);

                float3 bary = float3(
                    1.0 - attribs.barycentrics.x - attribs.barycentrics.y,
                    attribs.barycentrics.x,
                    attribs.barycentrics.y
                );
                Vertex v = InterpolateVertices(v0, v1, v2, bary);
                
                payload.color = float4(v.normalTS.xyz * 0.5 + 0.5, 1.0);
            }
            
            ENDHLSL
        }
    }
}