Shader "Unlit/MetaballsControlled"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        iChannel0 ("Texture iChannel0", Cube) = "white" {}

        _UseExternal ("Use External (0 Demo / 1 External)", Float) = 1
        _Threshold ("Threshold", Float) = 0.2
        _BoundsPad ("Bounds Pad", Float) = 0.1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            samplerCUBE iChannel0;

            float _UseExternal;
            float _Threshold;
            float _BoundsPad;

            #define samples 4
            #define MAX_BLOBS 64

            int _BlobCount;
            float4 _Blobs[MAX_BLOBS]; // xyz=位置, w=半径

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            float hash1(float n)
            {
                return frac(sin(n) * 43758.5453123);
            }

            float2 hash2(float n)
            {
                return frac(sin(float2(n, n + 1.0)) * float2(43758.5453123, 22578.1459123));
            }

            float3 hash3(float n)
            {
                return frac(sin(float3(n, n + 1.0, n + 2.0)) * float3(43758.5453123, 22578.1459123, 19642.3490423));
            }

            int GetCount()
            {
                return (_UseExternal >= 0.5) ? _BlobCount : 8;
            }

            float4 GetBlob(int i, float time)
            {
                if (_UseExternal >= 0.5)
                {
                    return _Blobs[i];
                }

                float h = float(i) / 8.0;
                float3 p3 = 2.0 * sin(6.2831 * hash3(h * 1.17) + hash3(h * 13.7) * time);
                float  r  = 1.7 + 0.9 * sin(6.28 * hash1(h * 23.13));
                return float4(p3, r);
            }

            float sdMetaBalls(float3 pos, float time)
            {
                float m = 0.0;
                float p = 0.0;
                float dmin = 1e20;

                float hLip = 1.0;

                int count = clamp(GetCount(), 0, MAX_BLOBS);

                for (int i = 0; i < MAX_BLOBS; i++)
                {
                    if (i >= count) break;

                    float4 b = GetBlob(i, time);

                    float db = length(b.xyz - pos);
                    if (db < b.w)
                    {
                        float x = db / b.w;
                        p += 1.0 - x * x*x*(x*(x*6.0 - 15.0) + 10.0);
                        m += 1.0;
                        hLip = max(hLip, 0.5333 * b.w);
                    }
                    else
                    {
                        dmin = min(dmin, db - b.w);
                    }
                }

                float d = dmin + _BoundsPad;

                if (m > 0.5)
                {
                    d = hLip * (_Threshold - p);
                }

                return d;
            }

            float3 norMetaBalls(float3 pos, float time)
            {
                float3 nor = float3(0.0, 0.0001, 0.0);

                int count = clamp(GetCount(), 0, MAX_BLOBS);

                for (int i = 0; i < MAX_BLOBS; i++)
                {
                    if (i >= count) break;

                    float4 b = GetBlob(i, time);

                    float db = length(b.xyz - pos);
                    float x = clamp(db / b.w, 0.0, 1.0);
                    float pp = x * x * (30.0 * x * x - 60.0 * x + 30.0);
                    nor += normalize(pos - b.xyz) * pp / b.w;
                }

                return normalize(nor);
            }

            static const float precis = 0.01;

            float map(float3 p, float time)
            {
                return sdMetaBalls(p, time);
            }

            float2 intersect(float3 ro, float3 rd, float time)
            {
                float maxd = 15.0;
                float h = precis * 2.0;
                float t = 0.0;
                float m = 1.0;

                for (int i = 0; i < 75; i++)
                {
                    if (h < precis || t > maxd) continue;
                    t += h;
                    h = map(ro + rd * t, time);
                }

                if (t > maxd) m = -1.0;
                return float2(t, m);
            }

            float3 calcNormal(float3 pos, float time)
            {
                return norMetaBalls(pos, time);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 q = i.uv;

                float2 m = float2(0.5, 0.5);

                float msamples = sqrt(float(samples));
                float3 tot = float3(0.0, 0.0, 0.0);

                #if samples > 1
                for (int a = 0; a < samples; a++)
                #else
                float a = 0.0;
                #endif
                {
                    float2 poff = float2(fmod(float(a), msamples), floor(float(a) / msamples)) / msamples;
                    float time = _Time.y;

                    // 关键：固定相机，不绕圈
                    float3 ro = float3(0.0, 0.0, -8.0);
                    float3 ta = float3(0.0, 0.0, 0.0);

                    float2 p = -1.0 + 2.0 * (q * _ScreenParams.xy + poff) / _ScreenParams.xy;
                    p.x *= _ScreenParams.x / _ScreenParams.y;
                    p.x *= -1;

                    float3 ww = normalize(ta - ro);
                    float3 uu = normalize(cross(ww, float3(0.0, 1.0, 0.0)));
                    float3 vv = normalize(cross(uu, ww));

                    float3 rd = normalize(p.x * uu + p.y * vv + 2.0 * ww);

                    float3 col = pow(texCUBE(iChannel0, rd).xyz, float3(2.2, 2.2, 2.2));

                    float2 tmat = intersect(ro, rd, time);
                    if (tmat.y > -0.5)
                    {
                        float3 pos = ro + tmat.x * rd;
                        float3 nor = calcNormal(pos, time);
                        float3 ref = reflect(rd, nor);

                        float3 mate = float3(0.0, 0.0, 0.0);
                        float wsum = 0.01;

                        int count = clamp(GetCount(), 0, MAX_BLOBS);

                        for (int bi = 0; bi < MAX_BLOBS; bi++)
                        {
                            if (bi >= count) break;

                            float h = float(bi) / 8.0;

                            float3 ccc = float3(1.0, 1.0, 1.0);
                            ccc = lerp(ccc, float3(1.0, 0.20, 0.55), smoothstep(0.65, 0.66, sin(30.0*h)));
                            ccc = lerp(ccc, float3(0.3, 0.20, 0.95), smoothstep(0.65, 0.66, sin(15.0*h)));

                            float4 b = GetBlob(bi, time);

                            float x = clamp(length(b.xyz - pos) / b.w, 0.0, 1.0);
                            float pp = 1.0 - x * x * (3.0 - 2.0 * x);

                            mate += pp * ccc;
                            wsum += pp;
                        }

                        mate /= wsum;

                        float3 lin = float3(0.0, 0.0, 0.0);
                        lin += lerp(float3(0.05, 0.02, 0.0), 1.2 * float3(0.8, 0.9, 1.0), 0.5 + 0.5 * nor.y);
                        lin *= 1.0 + 1.5 * float3(0.7, 0.5, 0.3) * pow(clamp(1.0 + dot(nor, rd), 0.0, 1.0), 2.0);
                        lin += 1.5 * clamp(0.3 + 2.0 * nor.y, 0.0, 1.0) * pow(texCUBE(iChannel0, ref).xyz, float3(2.2, 2.2, 2.2))
                               * (0.04 + 0.96 * pow(clamp(1.0 + dot(nor, rd), 0.0, 1.0), 4.0));

                        col = lin * mate;
                    }

                    tot += col;
                }

                tot /= float(samples);

                tot = pow(clamp(tot, 0.0, 1.0), float3(0.45, 0.45, 0.45));
                tot *= 0.5 + 0.5 * pow(16.0 * q.x * q.y * (1.0 - q.x) * (1.0 - q.y), 0.15);

                return float4(tot, 1.0);
            }
            ENDCG
        }
    }
}
