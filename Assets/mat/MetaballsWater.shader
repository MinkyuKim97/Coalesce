Shader "Unlit/MetaballsWater"
{
    Properties
    {
        iChannel0 ("Environment Cubemap", Cube) = "white" {}

        _UseExternal ("Use External (0 Demo / 1 External)", Float) = 1
        _Threshold ("Threshold", Float) = 0.12
        _BoundsPad ("Bounds Pad", Float) = 0.02

        // 水材质参数
        _WaterTint ("Water Tint (RGB)", Color) = (0.85, 0.95, 1.0, 1.0)
        _Opacity ("Opacity", Range(0,1)) = 0.22
        _FresnelPower ("Fresnel Power", Range(1,12)) = 6
        _Reflection ("Reflection Strength", Range(0,2)) = 1.0
        _Refraction ("Refraction Strength", Range(0,2)) = 1.0
        _IOR ("IOR (1.0~1.6)", Range(1.0, 1.6)) = 1.33
        _Specular ("Specular Strength", Range(0,3)) = 1.2
        _Smoothness ("Smoothness", Range(0.2,1)) = 0.92
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            samplerCUBE iChannel0;

            float _UseExternal;
            float _Threshold;
            float _BoundsPad;

            float4 _WaterTint;
            float _Opacity;
            float _FresnelPower;
            float _Reflection;
            float _Refraction;
            float _IOR;
            float _Specular;
            float _Smoothness;

            #define samples 4
            #define MAX_BLOBS 64

            int _BlobCount;
            float4 _Blobs[MAX_BLOBS]; // xyz=位置, w=半径

            // -------------------------
            // Demo 随机哈希（和你之前一致）
            // -------------------------
            float hash1(float n) { return frac(sin(n) * 43758.5453123); }

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
                if (_UseExternal >= 0.5) return _Blobs[i];

                // Demo：随机动画（只在 UseExternal=0 时）
                float h = float(i) / 8.0;
                float3 p3 = 2.0 * sin(6.2831 * hash3(h * 1.17) + hash3(h * 13.7) * time);
                float  r  = 1.7 + 0.9 * sin(6.28 * hash1(h * 23.13));
                return float4(p3, r);
            }

            // -------------------------
            // Metaball 场函数（SDF-ish）
            // -------------------------
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

            // -------------------------
            // 水滴着色（透明 + 菲涅尔 + 折射 + 高光）
            // -------------------------
            float3 ShadeWater(float3 rd, float3 nor)
            {
                // 视线方向：rd 指向场景，视线向量是 -rd
                float3 V = normalize(-rd);

                // 菲涅尔：视线越贴边，反射越强
                float fres = pow(1.0 - saturate(dot(nor, V)), _FresnelPower);

                // 反射方向
                float3 R = reflect(rd, nor);

                // 折射方向（从空气到水，eta = 1/IOR）
                float eta = 1.0 / max(1.0001, _IOR);
                float3 T = refract(rd, nor, eta);

                // 从环境取样（用 cubemap）
                float3 envRefl = texCUBE(iChannel0, R).xyz;
                float3 envRefr = texCUBE(iChannel0, T).xyz;

                // 简单“高光”：用反射方向对 V 的对齐（类似 Blinn/Phong 的效果）
                // smoothness 越高，高光越锐
                float specPow = lerp(16.0, 256.0, saturate(_Smoothness));
                float spec = pow(saturate(dot(normalize(-R), V)), specPow) * _Specular;

                // 折射部分乘一点水色（吸收/染色）
                float3 refrCol = envRefr * _WaterTint.rgb;

                // 反射与折射混合：菲涅尔控制比例
                float3 col = 0;
                col += refrCol * (_Refraction * (1.0 - fres));
                col += envRefl * (_Reflection * fres);
                col += spec;

                return col;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 q = i.uv;

                float msamples = sqrt(float(samples));
                float3 tot = float3(0.0, 0.0, 0.0);
                float alphaTot = 0.0;

                #if samples > 1
                for (int a = 0; a < samples; a++)
                #else
                float a = 0.0;
                #endif
                {
                    float2 poff = float2(fmod(float(a), msamples), floor(float(a) / msamples)) / msamples;
                    float time = _Time.y;

                    // 固定相机（你之前要求不转）
                    float3 ro = float3(0.0, 0.0, -8.0);
                    float3 ta = float3(0.0, 0.0, 0.0);

                    float2 p = -1.0 + 2.0 * (q * _ScreenParams.xy + poff) / _ScreenParams.xy;
                    p.x *= _ScreenParams.x / _ScreenParams.y;
                    p.x *= -1;

                    float3 ww = normalize(ta - ro);
                    float3 uu = normalize(cross(ww, float3(0.0, 1.0, 0.0)));
                    float3 vv = normalize(cross(uu, ww));

                    float3 rd = normalize(p.x * uu + p.y * vv + 2.0 * ww);

                    // 背景：环境 cubemap（透明材质也需要背景色来混）
                    float3 bg = texCUBE(iChannel0, rd).xyz;

                    float2 tmat = intersect(ro, rd, time);
                    if (tmat.y > -0.5)
                    {
                        float3 pos = ro + tmat.x * rd;
                        float3 nor = norMetaBalls(pos, time);

                        float3 water = ShadeWater(rd, nor);

                        // alpha：基础透明度 + 边缘更不透明一点（也由菲涅尔增强）
                        float fres = pow(1.0 - saturate(dot(nor, normalize(-rd))), _FresnelPower);
                        float aOut = saturate(_Opacity + 0.35 * fres);

                        // 透明混合：在 shader 内先把“水”混到 bg 上，再交给 Blend
                        float3 col = lerp(bg, water, aOut);

                        tot += col;
                        alphaTot += aOut;
                    }
                    else
                    {
                        tot += bg;
                        alphaTot += 0.0;
                    }
                }

                tot /= float(samples);
                float aFinal = saturate(alphaTot / float(samples));

                // gamma（保持你之前风格）
                tot = pow(clamp(tot, 0.0, 1.0), float3(0.45, 0.45, 0.45));

                // 轻微暗角（可留可去）
                tot *= 0.5 + 0.5 * pow(16.0*q.x*q.y*(1.0 - q.x)*(1.0 - q.y), 0.15);

                return float4(tot, aFinal);
            }
            ENDCG
        }
    }
}
