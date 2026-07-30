// Texture-override paint layer for DrawnShapeRenderer — port of
// content/materials/terrain/painted_shape.gdshader (31 lines).
//
// The mesh is the shape's fill polygon; the painted weight mask decides what shows.
// Unpainted texels are clipped so the base fill underneath shows through, and the R/G/B
// weights blend up to three textures with a feathered edge. No edge dressing, no collision:
// this is the character/prop variant of the terrain paint shader.
//
// Shader variant: a plain built-in CGPROGRAM unlit pass with NO LightMode tag, NOT a URP
// HLSL shader. The Sandbox renders through URP's 2D renderer (Renderer2DData), which draws
// untagged passes via SRPDefaultUnlit — exactly the path DrawnShapeRenderer already relies on
// for "Sprites/Default". A hand-written URP pass would have to pick a LightMode
// ("Universal2D" vs "UniversalForward") and would silently render nothing under the wrong
// renderer. Cost of the choice: built-in CG shaders are never SRP-Batcher compatible, so the
// paint layer is one extra draw call per shape (authoring-time geometry — acceptable).
//
// Coordinate contract (see m2-conventions "Shader contract"): both UV sets are derived from
// the OBJECT-SPACE vertex position, which for the PaintLayer child equals the shape's local
// XY. No special mesh UVs are needed, and DisableBatching keeps object space meaningful.
//   mask UV   = (local - _MaskOrigin) * _Resolution / _MaskSizePx
//   fill UV   = local / _FillScale        (local UNITS per repeat; Godot used local px)
Shader "PowerOfFire/DrawToPlay/PaintedShape"
{
    Properties
    {
        _MaskTex ("Paint Mask (RGB weights, A coverage)", 2D) = "black" {}
        _MaskOrigin ("Mask Rect Min (local units)", Vector) = (0, 0, 0, 0)
        _MaskSizePx ("Mask Size (pixels)", Vector) = (1, 1, 0, 0)
        _Resolution ("Mask Pixels Per Local Unit", Float) = 128
        _Cutoff ("Coverage Cutoff", Range(0, 1)) = 0.02

        _FillColor ("Fill Color (slot 1 fallback)", Color) = (0.22, 0.31, 0.47, 1)
        _FillTex ("Fill Texture (slot 1)", 2D) = "white" {}
        _UseFillTex ("Use Fill Texture", Float) = 0
        _PaintTex2 ("Paint Texture (slot 2)", 2D) = "white" {}
        _HasTex2 ("Has Paint Texture 2", Float) = 0
        _PaintTex3 ("Paint Texture (slot 3)", 2D) = "white" {}
        _HasTex3 ("Has Paint Texture 3", Float) = 0
        _FillScale ("Local Units Per Texture Repeat", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            // object space must survive to the vertex shader: batching would bake the
            // vertices into world space and shift every mask/fill lookup
            "DisableBatching" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.5

            #include "UnityCG.cginc"

            sampler2D _MaskTex;
            sampler2D _FillTex;
            sampler2D _PaintTex2;
            sampler2D _PaintTex3;

            float4 _MaskOrigin;
            float4 _MaskSizePx;
            float _Resolution;
            float _Cutoff;

            float4 _FillColor;
            float _UseFillTex;
            float _HasTex2;
            float _HasTex3;
            float _FillScale;

            struct appdata_t
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 maskUV : TEXCOORD0;
                float2 fillUV : TEXCOORD1;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float2 local = v.vertex.xy;
                o.maskUV = (local - _MaskOrigin.xy) * _Resolution
                    / max(_MaskSizePx.xy, float2(1.0, 1.0));
                o.fillUV = local / max(_FillScale, 1e-4);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 m = tex2D(_MaskTex, i.maskUV);
                // unpainted (and the mask's transparent padding) never draws
                clip(m.a - _Cutoff);

                // normalise the three weights; msum floors at 0.001 so a texel that only ever
                // received erase strokes still resolves to slot 1 instead of exploding
                float msum = max(m.r + m.g + m.b, 0.001);
                float3 w = float3(m.r, m.g, m.b) / msum;

                // sampled unconditionally and selected with lerp: cheaper to reason about than
                // a branch and free of any gradient concerns
                float4 c1 = lerp(_FillColor, tex2D(_FillTex, i.fillUV), step(0.5, _UseFillTex));
                float4 c2 = lerp(c1, tex2D(_PaintTex2, i.fillUV), step(0.5, _HasTex2));
                float4 c3 = lerp(c1, tex2D(_PaintTex3, i.fillUV), step(0.5, _HasTex3));

                return float4(w.x * c1.rgb + w.y * c2.rgb + w.z * c3.rgb, m.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
