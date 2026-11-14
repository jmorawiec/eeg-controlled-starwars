Shader "Force/Water" {
Properties {
    _Deep("Deep colour",Color)=(.03,.12,.14,.92) _Shallow("Shallow colour",Color)=(.16,.4,.4,.75)
    _Flow("Flow speed",Float)=.05 _Ripple("Ripple strength",Float)=.35
}
SubShader {
    Tags {"Queue"="Transparent" "RenderType"="Transparent"} Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
    CGPROGRAM
    #pragma surface surf StandardSpecular alpha:fade fullforwardshadows vertex:vert
    #pragma target 3.0
    fixed4 _Deep,_Shallow;float _Flow,_Ripple;
    struct Input {float3 worldPos;float3 viewDir;};
    float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
    float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+float2(1,1)),f.x),f.y);}
    float height(float2 p){float t=_Time.y*_Flow*20;return noise(p*1.3+float2(t,t*.6))*.55+noise(p*3.1-float2(t*.8,t))*.3+noise(p*7.3+float2(-t*.5,t*1.4))*.15;}
    void vert(inout appdata_full v){}
    void surf(Input IN,inout SurfaceOutputStandardSpecular o){
        float2 p=IN.worldPos.xz;float e=.06;
        float h=height(p);float hx=height(p+float2(e,0)),hz=height(p+float2(0,e));
        float3 n=normalize(float3((h-hx)*_Ripple*14,(h-hz)*_Ripple*14,1)); // tangent space, water meshes use world-aligned UVs
        float fres=pow(1-saturate(dot(normalize(IN.viewDir),n)),3);
        fixed4 c=lerp(_Deep,_Shallow,saturate(h*1.3));
        o.Albedo=c.rgb;o.Normal=n;o.Specular=float3(.012,.012,.012);o.Smoothness=.35;o.Alpha=saturate(c.a+fres*.2);
        o.Emission=fres*float3(.012,.025,.03);
    }
    ENDCG
}
Fallback "Transparent/Diffuse"
}
