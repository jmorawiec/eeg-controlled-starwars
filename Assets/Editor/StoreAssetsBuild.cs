using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Keep the purchased source assets and GUIDs intact; retarget through Humanoid.
public static class StoreAssetsBuild {
 const string Output="Assets/Resources/Generated/";
 static AnimationClip Motion(string fragment){
  string p=AssetDatabase.FindAssets("t:Model",new[]{"Assets/Kevin Iglesias"}).Select(AssetDatabase.GUIDToAssetPath).First(x=>x.EndsWith(fragment+".fbx")&&x.Contains("/Male/"));
  var importer=(ModelImporter)AssetImporter.GetAtPath(p);
  importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
  importer.importAnimation=true;importer.SaveAndReimport();
  var clips=importer.defaultClipAnimations;
  foreach(var c in clips){c.loopTime=true;c.loopPose=true;c.lockRootPositionXZ=false;c.keepOriginalPositionXZ=false;c.lockRootRotation=false;c.keepOriginalOrientation=true;c.lockRootHeightY=true;c.heightFromFeet=true;}
  importer.clipAnimations=clips;importer.SaveAndReimport();
  return AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().First(x=>!x.name.StartsWith("__"));
 }
 public static void Prepare(){
  var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Output+"Locomotion.controller");
  var state=controller.layers[0].stateMachine.states.First(x=>x.state.name=="Locomotion").state;
  var tree=state.motion as BlendTree;
  var children=new System.Collections.Generic.List<ChildMotion>();
  children.Add(new ChildMotion{motion=Motion("HumanM@Idle01"),position=Vector2.zero,timeScale=1});
  foreach(var gait in new[]{"Walk","Run"}){
   float speed=gait=="Walk"?1.8f:3.4f;
   foreach(var direction in new[]{"Forward","ForwardRight","Right","BackwardRight","Backward","BackwardLeft","Left","ForwardLeft"}){
    float angle=Array.IndexOf(new[]{"Forward","ForwardRight","Right","BackwardRight","Backward","BackwardLeft","Left","ForwardLeft"},direction)*Mathf.PI/4;
    children.Add(new ChildMotion{motion=Motion("HumanM@"+gait+"01_"+direction),position=new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*speed,timeScale=gait=="Walk"?1.16f:1f});
   }
  }
  children.Add(new ChildMotion{motion=Motion("HumanM@Sprint01_Forward"),position=new Vector2(0,5.2f),timeScale=1});
  tree.children=children.ToArray();EditorUtility.SetDirty(tree);EditorUtility.SetDirty(controller);
  var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/VolumetricLines/BuiltInRenderPipeline/Materials/SingleLine-LightSaber.mat");
  AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(material),Output+"BladeVolumetric.mat");
  BuildCharacter("Assets/Package Ready Assets/MP_Clovis/Model/MP_Clovis.fbx","Assets/Package Ready Assets/MP_Clovis/Prefab/MP_Clovis.prefab","Clovis");
  AssetDatabase.SaveAssets();Debug.Log("STORE_ANIMATIONS_READY: 18 dedicated idle/walk/run/sprint clips; no mirrored gait");
 }
 static Texture ReadTexture(Material material,string property){
  var array=new SerializedObject(material).FindProperty("m_SavedProperties.m_TexEnvs");
  for(int i=0;i<array.arraySize;i++){var entry=array.GetArrayElementAtIndex(i);if(entry.FindPropertyRelative("first").stringValue==property)return entry.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;}
  return null;
 }
 static void BuildCharacter(string modelPath,string prefabPath,string name){
  var importer=AssetImporter.GetAtPath(modelPath) as ModelImporter;if(importer==null)return;
  importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.importAnimation=false;importer.optimizeGameObjects=false;importer.SaveAndReimport();
  var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
  var wrapper=new GameObject(name);model.transform.SetParent(wrapper.transform,false);
  foreach(var renderer in model.GetComponentsInChildren<Renderer>()){
   var slots=renderer.sharedMaterials;
   for(int i=0;i<slots.Length;i++){
    var original=slots[i];
    if(name=="Clovis")original=AssetDatabase.LoadAssetAtPath<Material>("Assets/Package Ready Assets/MP_Clovis/Materials/"+(renderer.name.ToLowerInvariant().Contains("hair")?"ClovisHair/MP_ClovisHair_mat.mat":"MP_Clovis_mat.mat"));
    if(!original)throw new Exception(name+": missing source material "+renderer.name);
    string output=Output+name+"_"+original.name+".mat";
    var mat=AssetDatabase.LoadAssetAtPath<Material>(output);
    if(!mat){
     mat=new Material(Shader.Find("Standard"));
     mat.mainTexture=ReadTexture(original,"_BaseMap")??ReadTexture(original,"_MainTex");
     // URP source materials retain their serialized PBR textures when switching pipeline.
     if(name=="Clovis")mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Package Ready Assets/MP_Clovis/Materials/"+(renderer.name.ToLowerInvariant().Contains("hair")?"ClovisHair/MP_ClovisHair_base.png":"MP_Clovis_base.png"));
     if(!mat.mainTexture)throw new Exception("Missing albedo: "+original.name);
     foreach(string property in new[]{"_BumpMap","_MetallicGlossMap","_OcclusionMap"})mat.SetTexture(property,ReadTexture(original,property));
     if(name=="Clovis"&&!renderer.name.ToLowerInvariant().Contains("hair")){
      mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Package Ready Assets/MP_Clovis/Materials/MP_Clovis_normals.png"));
      mat.SetTexture("_OcclusionMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Package Ready Assets/MP_Clovis/Materials/MP_Clovis_AO.png"));
     }
     if(mat.GetTexture("_BumpMap"))mat.EnableKeyword("_NORMALMAP");
     mat.SetFloat("_Glossiness",.25f);mat.enableInstancing=true;
     if(original.name.ToLowerInvariant().Contains("hair")){mat.SetFloat("_Mode",1);mat.SetFloat("_Cutoff",.4f);mat.EnableKeyword("_ALPHATEST_ON");mat.SetOverrideTag("RenderType","TransparentCutout");mat.renderQueue=2450;}
     AssetDatabase.CreateAsset(mat,output);
    }
    slots[i]=mat;
   }
   renderer.sharedMaterials=slots;
  }
  var animator=model.GetComponentInChildren<Animator>();if(!animator)animator=model.AddComponent<Animator>();
  animator.avatar=AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().First();
  if(!animator.avatar.isValid||!animator.avatar.isHuman)throw new Exception(name+" is not a valid humanoid");
  animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<AnimatorController>(Output+"Locomotion.controller");animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
  animator.Rebind();animator.Update(0);
  var renderers=model.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
  float scale=1.8f/bounds.size.y;model.transform.localScale*=scale;model.transform.localPosition-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale;
  PrefabUtility.SaveAsPrefabAsset(wrapper,Output+name+".prefab");UnityEngine.Object.DestroyImmediate(wrapper);Debug.Log("STORE_CHARACTER_READY "+name);
 }
 public static void Refresh(){Prepare();MotionAudit.Run();StoreMotionAudit.Run();}
}
