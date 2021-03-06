// Shader for the textures and flats in a level

Shader "Doom/Texture" {
 
Properties {
    _MainTex ("Render Map", 2D) = "white" {}
    _Palette ("Palette", 2D) = "white" {}
    _Colormap ("Colormap", 2D) = "white" {}
    _Brightness ("Brightness", float) = 1.0
}

SubShader {
    Tags {"Queue"="Geometry" "IgnoreProjector"="True" "RenderType"="Opaque"}
    LOD 200

    Pass {  
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "doomlight.cginc"

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float brightness : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _Palette;
            sampler2D _Colormap;
            float _Brightness;
            float4 _MainTex_ST;

            v2f vert (appdata_base v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                // o.brightness = _Brightness;
                o.brightness = floor(_Brightness*16)/16;

                float3 worldNormal = mul( unity_ObjectToWorld, float4( v.normal, 0.0 ) ).xyz;
                // o.brightness -= 8.0/256.0;
                o.brightness -= saturate(abs(worldNormal.z) * 50) * 20/256;
                o.brightness += saturate(abs(worldNormal.x) * 50) * 20/256;
                // if (worldNormal.y > 0.95) o.brightness = 0;
                
                return o;
            }

            

            fixed4 frag (v2f i) : SV_Target
            {

                
                float odepth = doomLight(i.vertex.z, i.brightness);

                float indexCol = tex2D(_MainTex, i.texcoord).r;

                float alpha = tex2D(_MainTex, i.texcoord).a;
                float colormapIndex = indexCol;
                float brightnessLookup = (floor((1.0-odepth) * 32.0)) / 32.0;

                float paletteIndex = tex2D(_Colormap, float2(colormapIndex + (0.5/256.0), brightnessLookup * (32.0/34.0)));

                
                float4 col = tex2D(_Palette, float2(paletteIndex + (.5/256.0), 0.0));
                col.a = alpha;
                clip(col.a - 0.9);
                return col;
            }
        ENDCG
    }
}

}