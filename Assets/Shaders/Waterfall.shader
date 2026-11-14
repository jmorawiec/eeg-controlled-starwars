Shader "Force/Waterfall" {
Properties {_Color("Water",Color)=(.44,.67,.72,.55)}
SubShader {Tags {"Queue"="Transparent" "RenderType"="Transparent"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
Pass {CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_fog
#include "UnityCG.cginc"
struct v2f{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;UNITY_FOG_COORDS(1)};
float4 _Color;
v2f vert(appdata_base v){v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.texcoord;UNITY_TRANSFER_FOG(o,o.pos);return o;}
float4 frag(v2f i):SV_Target {float2 u=i.uv;float ripple=sin(u.x*84+sin(u.y*25+_Time.y*3)*1.4)*.5+.5;float stream=sin(u.y*42+_Time.y*8+u.x*7)*.5+.5;float edge=smoothstep(0,.13,u.x)*smoothstep(1,.87,u.x);float4 c=_Color;c.rgb+=ripple*.08+stream*.035;c.a*=edge*(.6+ripple*.4);UNITY_APPLY_FOG(i.fogCoord,c);return c;}
ENDCG}}
}
