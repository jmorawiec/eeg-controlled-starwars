Shader "Force/Foliage" {
Properties { _Color("Tint",Color)=(1,1,1,1) _MainTex("Albedo",2D)="white"{} _Cutoff("Cutout",Range(0,1))=.4 _Wind("Wind",Float)=.045 }
SubShader { Tags {"Queue"="AlphaTest" "RenderType"="TransparentCutout"} Cull Off
CGPROGRAM
#pragma surface surf Standard fullforwardshadows addshadow alphatest:_Cutoff vertex:vert
#pragma target 3.0
sampler2D _MainTex;fixed4 _Color;float _Wind;
struct Input {float2 uv_MainTex;};
void vert(inout appdata_full v){float3 w=mul(unity_ObjectToWorld,v.vertex).xyz;float f=sin(_Time.y*1.8+w.x*.7+w.z*.6)*_Wind*saturate(v.vertex.y);v.vertex.xz+=float2(f,f*.4);}
void surf(Input IN,inout SurfaceOutputStandard o){fixed4 c=tex2D(_MainTex,IN.uv_MainTex)*_Color;o.Albedo=c.rgb;o.Alpha=c.a;o.Smoothness=.1;o.Metallic=0;}
ENDCG
}
Fallback "Transparent/Cutout/Diffuse"
}
