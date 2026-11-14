Shader "Force/AncientStone" {
Properties {
 _Color("Stone tint",Color)=(1,1,1,1) _MainTex("Scanned albedo",2D)="white"{} _BumpMap("Scanned normal",2D)="bump"{}
 _ARM("Occlusion / roughness / metal",2D)="white"{} _MossTex("Moss albedo",2D)="white"{} _MossAmount("Moss coverage",Range(0,1))=.3
 _BumpScale("Normal strength",Range(0,2))=.8 _Glossiness("Polish",Range(0,1))=.2
}
SubShader {Tags {"RenderType"="Opaque"} LOD 300
CGPROGRAM
#pragma surface surf Standard fullforwardshadows addshadow
#pragma target 3.0
#include "UnityStandardUtils.cginc"
sampler2D _MainTex,_BumpMap,_ARM,_MossTex;fixed4 _Color;half _MossAmount,_BumpScale,_Glossiness;
struct Input {float2 uv_MainTex;float3 worldPos;float3 worldNormal;INTERNAL_DATA};
float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+float2(1,1)),f.x),f.y);}
void surf(Input IN,inout SurfaceOutputStandard o){
 float3 n=normalize(WorldNormalVector(IN,float3(0,0,1)));float3 w=abs(n);w/=max(dot(w,1),.001);
 float3 p=IN.worldPos*.65;float3 moss=tex2D(_MossTex,p.yz).rgb*w.x+tex2D(_MossTex,p.xz).rgb*w.y+tex2D(_MossTex,p.xy).rgb*w.z;
 float patch=noise(IN.worldPos.xz*.35+IN.worldPos.y*.13);float growth=saturate(n.y*.8+.2)+saturate(1-IN.worldPos.y)*.5;
 float amount=smoothstep(.35,.72,patch)*growth*_MossAmount;
 float3 base=tex2D(_MainTex,IN.uv_MainTex).rgb*_Color.rgb;
 o.Albedo=lerp(base,moss*_Color.rgb*.85,amount);o.Normal=UnpackScaleNormal(tex2D(_BumpMap,IN.uv_MainTex),_BumpScale);
 float3 arm=tex2D(_ARM,IN.uv_MainTex).rgb;o.Occlusion=lerp(1,arm.r,.65);o.Smoothness=lerp((1-arm.g)*.45+.04,.06,amount);o.Metallic=0;
}
ENDCG
} Fallback "Standard"
}
