Shader "Hidden/ForceAtmosphere" {
Properties { _MainTex("Source",2D)="white"{} }
SubShader { Cull Off ZWrite Off ZTest Always
CGINCLUDE
#include "UnityCG.cginc"
sampler2D _MainTex,_BloomTex;float4 _MainTex_TexelSize;float2 _Direction;float _Threshold,_BlurMenu,_BloomStrength;
float4 _SunUV,_SunColor,_Flash;float _Aberration,_Hurt,_Grain,_Focus;
float4 blur(v2f_img i):SV_Target {
float2 d=_MainTex_TexelSize.xy*_Direction;
return tex2D(_MainTex,i.uv)*.227027+(tex2D(_MainTex,i.uv+d*1.384615)+tex2D(_MainTex,i.uv-d*1.384615))*.316216+(tex2D(_MainTex,i.uv+d*3.230769)+tex2D(_MainTex,i.uv-d*3.230769))*.070270;
}
float4 extract(v2f_img i):SV_Target {float3 c=tex2D(_MainTex,i.uv).rgb;return float4(max(c-_Threshold,0),1);}
float hash12(float2 p){float3 p3=frac(float3(p.xyx)*.1031);p3+=dot(p3,p3.yzx+33.33);return frac((p3.x+p3.y)*p3.z);}
// Cheap volumetric sun shafts: radial march towards the sun and accumulate only the very bright sky.
float3 shafts(float2 uv){
 float strength=_SunUV.z;if(strength<=0.001)return 0;
 float2 dir=(_SunUV.xy-uv);float len=length(dir);dir/=max(len,1e-4);
 float stp=min(len,.55)/20;float3 acc=0;float2 p=uv;float w=1;
 for(int k=0;k<20;k++){p+=dir*stp;acc+=tex2D(_BloomTex,p).rgb*w;w*=.94;}
 float falloff=saturate(1-len*.85);
 return acc/20*strength*falloff*falloff*_SunColor.rgb*2.4;
}
float3 aces(float3 c){return (c*(2.51*c+.03))/(c*(2.43*c+.59)+.14);}
float4 composite(v2f_img i):SV_Target {
 float2 uv=i.uv;float2 v=uv-.5;float r2=dot(v,v);
 float2 ab=v*(_Aberration);
 float3 c;c.r=tex2D(_MainTex,uv+ab).r;c.g=tex2D(_MainTex,uv).g;c.b=tex2D(_MainTex,uv-ab).b;
 c+=tex2D(_BloomTex,uv).rgb*_BloomStrength;
 c+=shafts(uv);
 c=max(c,0);
 // Split toning before the tonemap: warm sun-lit highlights, cool teal shadows.
 float lum=dot(c,float3(.2126,.7152,.0722));
 c*=lerp(float3(.93,.99,1.04),float3(1.04,1.0,.95),smoothstep(.05,.9,lum));
 c=aces(c*.95);
 float g=dot(c,float3(.2126,.7152,.0722));c=lerp(g.xxx,c,.94);
 c=c*c*(3-2*c)*.35+c*.65; // gentle S-curve for contrast
 c*=1-smoothstep(.12,.62,r2)*.28;
 c=lerp(c,float3(1,.12,.08)*(.35+r2*1.6),saturate(_Hurt)*smoothstep(.04,.45,r2));
 c+=_Flash.rgb*_Flash.a;
 c+=(hash12(uv*_ScreenParams.xy+_Time.y*60)-.5)*_Grain;
 return float4(saturate(c),tex2D(_MainTex,i.uv).a);
}
ENDCG
Pass { CGPROGRAM
#pragma vertex vert_img
#pragma fragment blur
ENDCG }
Pass { CGPROGRAM
#pragma vertex vert_img
#pragma fragment extract
ENDCG }
Pass { CGPROGRAM
#pragma vertex vert_img
#pragma fragment composite
#pragma target 3.0
ENDCG }
}
}
