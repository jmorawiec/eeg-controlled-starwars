Shader "Force/Glow" {
Properties { _MainTex("Sprite",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) _Intensity("Intensity",Float)=1 }
SubShader { Tags {"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True"} Blend SrcAlpha One ZWrite Off Cull Off
Pass { CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
sampler2D _MainTex;float4 _MainTex_ST;fixed4 _Color;float _Intensity;
struct appdata{float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
struct v2f{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
v2f vert(appdata v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=TRANSFORM_TEX(v.uv,_MainTex);o.color=v.color;return o;}
fixed4 frag(v2f i):SV_Target{fixed4 t=tex2D(_MainTex,i.uv);fixed a=t.a*i.color.a*_Color.a;return fixed4(i.color.rgb*_Color.rgb*_Intensity,a);}
ENDCG }
}
}
