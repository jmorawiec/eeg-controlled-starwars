using UnityEditor;
using UnityEngine;
public static class VisualAssetBuild {
 static void Tune(string name,Color tint,float gloss,float bump=1){var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Generated/"+name+".mat");if(!m)return;m.color=tint;m.SetFloat("_Glossiness",gloss);m.SetFloat("_BumpScale",bump);EditorUtility.SetDirty(m);}
 public static void Build(){
  var beam=new Texture2D(128,128,TextureFormat.RGBA32,false);
  for(int y=0;y<128;y++)for(int x=0;x<128;x++){float dx=(x/127f-.5f)*2,dy=(y/127f-.5f)*2;float a=Mathf.Exp(-(dx*dx+dy*dy)*5.5f);beam.SetPixel(x,y,new Color(a,a,a,a));}
  System.IO.File.WriteAllBytes("Assets/Resources/Generated/BladeFalloff.png",beam.EncodeToPNG());Object.DestroyImmediate(beam);AssetDatabase.ImportAsset("Assets/Resources/Generated/BladeFalloff.png");
  var ti=(TextureImporter)AssetImporter.GetAtPath("Assets/Resources/Generated/BladeFalloff.png");ti.wrapMode=TextureWrapMode.Clamp;ti.mipmapEnabled=false;ti.sRGBTexture=false;ti.SaveAndReimport();
  var blade=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Generated/BladeVolumetric.mat");blade.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Generated/BladeFalloff.png");blade.SetInt("_UvBasedLightSaberFactor",0);EditorUtility.SetDirty(blade);
  var grip=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Generated/WeaponGrip.mat");if(!grip){grip=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(grip,"Assets/Resources/Generated/WeaponGrip.mat");}grip.color=new Color(.045f,.055f,.068f);grip.SetFloat("_Metallic",.6f);grip.SetFloat("_Glossiness",.38f);EditorUtility.SetDirty(grip);

  Tune("PlazaStone",new Color(.70f,.77f,.78f),.23f,.65f);Tune("CobbleMoss",new Color(.62f,.70f,.67f),.16f,.7f);Tune("RuinWall",new Color(.69f,.75f,.73f),.12f,.85f);Tune("MossWall",new Color(.62f,.7f,.64f),.1f,.7f);Tune("ValleyGround",new Color(.56f,.64f,.59f),.08f,.65f);
  foreach(var spec in new[]{("RuinWall","rabdentse_ruins_wall",.5f),("PlazaStone","monastery_stone_floor",.14f),("CobbleMoss","mossy_cobblestone",.3f)}){
   string path="Assets/Resources/WorldAssets/"+spec.Item2+"_arm.jpg";
   var importer=AssetImporter.GetAtPath(path) as TextureImporter;if(importer==null)continue;
   importer.sRGBTexture=false;importer.anisoLevel=8;importer.mipmapEnabled=true;importer.maxTextureSize=2048;importer.SaveAndReimport();
   var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Generated/"+spec.Item1+".mat");m.shader=Shader.Find("Force/AncientStone");m.SetTexture("_ARM",AssetDatabase.LoadAssetAtPath<Texture2D>(path));m.SetTexture("_MossTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/WorldAssets/mossy_rock_albedo.jpg"));m.SetFloat("_MossAmount",spec.Item3);EditorUtility.SetDirty(m);
  }
  var sky=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Generated/SunsetSky.mat");if(sky){sky.SetFloat("_Exposure",.48f);sky.SetColor("_Tint",new Color(.4f,.48f,.58f));EditorUtility.SetDirty(sky);}
  var water=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Generated/Water.mat");if(water){water.SetColor("_Deep",new Color(.015f,.075f,.095f,.88f));water.SetColor("_Shallow",new Color(.05f,.16f,.18f,.8f));water.SetFloat("_Ripple",.045f);EditorUtility.SetDirty(water);}
  foreach(string p in AssetDatabase.FindAssets("t:Material",new[]{"Assets/Resources/Generated"})){
   var m=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(p));if(m.shader.name=="Force/Foliage"){m.SetFloat("_Wind",.018f);m.color=Color.Lerp(m.color,new Color(.65f,.77f,.69f),.32f);EditorUtility.SetDirty(m);}
  }
  if(!AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Generated/Waterfall.mat"))AssetDatabase.CreateAsset(new Material(Shader.Find("Force/Waterfall")),"Assets/Resources/Generated/Waterfall.mat");
  AssetDatabase.SaveAssets();Debug.Log("CINEMATIC_MATERIALS_READY");
 }
}
