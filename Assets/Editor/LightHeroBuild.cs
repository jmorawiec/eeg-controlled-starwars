using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
public static class LightHeroBuild {
 const string Folder="Assets/Adventures_woman/",Out="Assets/Resources/Generated/";
 static Texture2D T(string name)=>AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"Textures/"+name);
 public static void Build(){
  foreach(string guid in AssetDatabase.FindAssets("t:Texture",new[]{Folder.TrimEnd('/')})){
   string p=AssetDatabase.GUIDToAssetPath(guid);var ti=AssetImporter.GetAtPath(p) as TextureImporter;if(ti==null)continue;
   bool normal=p.Contains("Normal"),hairTexture=p.Contains("Hair");ti.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;ti.sRGBTexture=!normal&&p.Contains("Albedo");ti.maxTextureSize=hairTexture?2048:4096;ti.mipmapEnabled=true;ti.anisoLevel=8;ti.alphaIsTransparency=hairTexture&&p.Contains("Albedo");ti.SaveAndReimport();
  }
  string path=Folder+"Mesh/Adventures_woman.FBX";var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.optimizeGameObjects=false;importer.importAnimation=false;importer.SaveAndReimport();
  var body=new Material(Shader.Find("Standard")){mainTexture=T("Character_Clothes/T_Adventures woman_Albedo_03.tga"),color=Color.white};body.SetTexture("_BumpMap",T("Character_Clothes/T_Adventures woman_Normal.tga"));body.SetTexture("_OcclusionMap",T("Character_Clothes/T_Adventures woman_Ao.tga"));body.EnableKeyword("_NORMALMAP");body.SetFloat("_BumpScale",.65f);body.SetFloat("_Glossiness",.25f);body.SetFloat("_Metallic",0);
  var hair=new Material(Shader.Find("Force/Foliage")){mainTexture=T("Hair/T_Adventures woman_Hair_Albedo_01.tga"),color=Color.white};hair.SetFloat("_Wind",0);hair.SetFloat("_Cutoff",.42f);
  Save(body,"LightHeroBody.mat");Save(hair,"LightHeroHair.mat");
  var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));var root=new GameObject("LightHero");model.transform.SetParent(root.transform,false);
  foreach(var r in model.GetComponentsInChildren<Renderer>()){
   bool isHair=r.name.ToLowerInvariant().Contains("hair");var mats=r.sharedMaterials;
   for(int i=0;i<mats.Length;i++){bool slotHair=isHair||(mats[i]&&mats[i].name.ToLowerInvariant().Contains("hair"));mats[i]=AssetDatabase.LoadAssetAtPath<Material>(Out+(slotHair?"LightHeroHair.mat":"LightHeroBody.mat"));}r.sharedMaterials=mats;
  }
  var a=model.GetComponentInChildren<Animator>();if(!a)a=model.AddComponent<Animator>();a.avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().First();if(!a.avatar.isHuman||!a.avatar.isValid)throw new Exception("Adventure Female rig invalid");a.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Out+"Locomotion.controller");a.applyRootMotion=false;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;
  var renderers=model.GetComponentsInChildren<Renderer>();Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
  float scale=1.78f/bounds.size.y;model.transform.localScale*=scale;model.transform.localPosition-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale;
  PrefabUtility.SaveAsPrefabAsset(root,Out+"LightHero.prefab");UnityEngine.Object.DestroyImmediate(root);AssetDatabase.SaveAssets();Debug.Log("LIGHT_HERO_READY");
 }
 static void Save(Material m,string file){var old=AssetDatabase.LoadAssetAtPath<Material>(Out+file);if(old){EditorUtility.CopySerialized(m,old);UnityEngine.Object.DestroyImmediate(m);}else AssetDatabase.CreateAsset(m,Out+file);}
}
