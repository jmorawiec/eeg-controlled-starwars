using UnityEngine;
namespace ForceDemo {
[RequireComponent(typeof(Camera))]
public class ForceAtmosphere:MonoBehaviour {
    public bool menu;public bool bloom=true;Material material;Camera cam;
    // Combat feedback, driven by ForceGame: impact = brief white-gold flash, hurt = red edge pulse, aberration = lens split.
    public float impact,hurt,aberration;public Color impactColor=new Color(1,.85f,.6f);
    public Vector3 sunDirection=new Vector3(-.4f,.35f,.85f);public Color sunColor=new Color(1,.75f,.5f);
    void OnEnable(){var source=Resources.Load<Material>("Generated/Atmosphere");if(source)material=new Material(source);cam=GetComponent<Camera>();cam.depthTextureMode|=DepthTextureMode.DepthNormals;}
    void Update(){impact=Mathf.MoveTowards(impact,0,Time.unscaledDeltaTime*4.5f);hurt=Mathf.MoveTowards(hurt,0,Time.unscaledDeltaTime*1.6f);aberration=Mathf.MoveTowards(aberration,0,Time.unscaledDeltaTime*.5f);}
    void OnRenderImage(RenderTexture source,RenderTexture destination){
        if(!material){Graphics.Blit(source,destination);return;}
        int w=Mathf.Max(16,source.width/4),h=Mathf.Max(16,source.height/4);
        var a=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.DefaultHDR);var b=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.DefaultHDR);
        material.SetFloat("_Threshold",1.35f);Graphics.Blit(source,a,material,1);
        material.SetVector("_Direction",new Vector2(1.5f,0));Graphics.Blit(a,b,material,0);material.SetVector("_Direction",new Vector2(0,1.5f));Graphics.Blit(b,a,material,0);
        material.SetVector("_Direction",new Vector2(2.5f,0));Graphics.Blit(a,b,material,0);material.SetVector("_Direction",new Vector2(0,2.5f));Graphics.Blit(b,a,material,0);
        material.SetTexture("_BloomTex",a);material.SetFloat("_BloomStrength",bloom?.18f:0);
        // Sun shafts: project the sky's sun onto the screen; fade out as it leaves the view.
        float facing=Vector3.Dot(cam.transform.forward,sunDirection.normalized);
        Vector3 vp=cam.WorldToViewportPoint(cam.transform.position+sunDirection.normalized*500);
        float strength=bloom&&!menu?Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.1f,.55f,facing))*.25f:0;
        material.SetVector("_SunUV",new Vector4(vp.x,vp.y,strength,0));material.SetColor("_SunColor",sunColor);
        material.SetFloat("_Aberration",aberration*.2f);material.SetFloat("_Hurt",hurt);material.SetFloat("_Grain",0f);
        material.SetColor("_Flash",new Color(impactColor.r,impactColor.g,impactColor.b,impact*.08f));
        if(menu){
            var c=RenderTexture.GetTemporary(source.width/2,source.height/2,0,RenderTextureFormat.DefaultHDR);var d=RenderTexture.GetTemporary(c.width,c.height,0,RenderTextureFormat.DefaultHDR);
            Graphics.Blit(source,c);for(int i=0;i<3;i++){material.SetVector("_Direction",new Vector2(2,0));Graphics.Blit(c,d,material,0);material.SetVector("_Direction",new Vector2(0,2));Graphics.Blit(d,c,material,0);}
            Graphics.Blit(c,destination,material,2);RenderTexture.ReleaseTemporary(c);RenderTexture.ReleaseTemporary(d);
        }else Graphics.Blit(source,destination,material,2);
        RenderTexture.ReleaseTemporary(a);RenderTexture.ReleaseTemporary(b);
    }
    void OnDestroy(){if(material)Destroy(material);}
}
}
