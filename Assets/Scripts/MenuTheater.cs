using UnityEngine;
namespace ForceDemo {
// Real animated game models, photographed with dedicated studio lighting for the menu.
public class MenuTheater:MonoBehaviour {
 public ForceGame game;public RenderTexture lightPortrait,darkPortrait;Camera[] cameras=new Camera[2];Transform[] heroes=new Transform[2];GameObject stage;
 const int Layer=29;
 void Start(){
  stage=new GameObject("Character portrait stage");stage.transform.position=new Vector3(1200,0,0);
  for(int i=0;i<2;i++){
   Vector3 origin=new Vector3(1200+i*20,0,0);Color accent=i==0?new Color(.16f,.63f,1):new Color(1,.12f,.10f);
   bool saved=CharacterVisual.LightHeroSelected;CharacterVisual.LightHeroSelected=i==0;var hero=CharacterVisual.Create("Portrait Apprentice",origin,accent,stage.transform,false,true);heroes[i]=hero;CharacterVisual.LightHeroSelected=saved;hero.rotation=Quaternion.Euler(0,i==0?-22:22,0);var cv=hero.GetComponent<CharacterVisual>();cv.baseTint=i==0?Color.white:new Color(.32f,.31f,.34f);cv.blocking=i==0;cv.channel=i==1?1:0;cv.animator.updateMode=AnimatorUpdateMode.UnscaledTime;
   foreach(var tr in hero.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=Layer;
   var cam=new GameObject("Portrait lens").AddComponent<Camera>();cam.transform.SetParent(stage.transform);cam.fieldOfView=30;cam.nearClipPlane=.1f;cam.farClipPlane=18;cam.cullingMask=1<<Layer;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(0,0,0,0);cam.gameObject.AddComponent<ForceAtmosphere>();cam.allowHDR=true;cam.allowMSAA=true;
   cam.transform.position=origin+new Vector3(.15f,1.15f,3.8f);cam.transform.LookAt(origin+Vector3.up*.98f);
   var rt=new RenderTexture(800,1000,24,RenderTextureFormat.ARGB32){antiAliasing=2,name=i==0?"Jedi portrait":"Sith portrait"};rt.Create();cam.targetTexture=rt;cameras[i]=cam;if(i==0)lightPortrait=rt;else darkPortrait=rt;
   Spot(origin+new Vector3(-1.5f,2.6f,2.4f),origin+Vector3.up*1.15f,new Color(.80f,.88f,1),.9f,62);
   Spot(origin+new Vector3(1.2f,2,-1.2f),origin+Vector3.up,accent,1.8f,65);
   Spot(origin+new Vector3(0,.4f,2.2f),origin+Vector3.up,new Color(.47f,.56f,.65f),.25f,80);
   for(int n=-1;n<=1;n+=2){var column=GameObject.CreatePrimitive(PrimitiveType.Cylinder);column.name="Portrait weathered column";column.transform.SetParent(stage.transform);column.layer=Layer;column.transform.position=origin+new Vector3(n*1.45f,1.5f,-1.1f);column.transform.localScale=new Vector3(.62f,2.3f,.62f);column.GetComponent<Renderer>().sharedMaterial=WorldBuilder.Stone;Destroy(column.GetComponent<Collider>());}
  }
  game.view.cullingMask&=~(1<<Layer);
 }
 void Spot(Vector3 p,Vector3 target,Color c,float intensity,float angle){var l=new GameObject("Portrait softbox").AddComponent<Light>();l.transform.SetParent(stage.transform);l.transform.position=p;l.transform.LookAt(target);l.type=LightType.Spot;l.color=c;l.intensity=intensity;l.range=8;l.spotAngle=angle;l.cullingMask=1<<Layer;l.shadows=LightShadows.None;}
 void LateUpdate(){if(!game||!stage)return;bool visible=game.stage==DemoStage.Choice;for(int i=0;i<2;i++){if(!cameras[i])continue;cameras[i].enabled=visible&&(i==0||game.GetComponent<ForceHUD>().page==MenuPage.Side);heroes[i].gameObject.SetActive(visible);if(visible)heroes[i].rotation=Quaternion.Euler(0,(i==0?-22:22)+Mathf.Sin(Time.unscaledTime*.18f)*3,0);}}
 void OnDestroy(){if(stage)Destroy(stage);if(lightPortrait)lightPortrait.Release();if(darkPortrait)darkPortrait.Release();}
}
}
