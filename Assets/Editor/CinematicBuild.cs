using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
public static class CinematicBuild {
 const string Lib2="Assets/AnimationLibrary/UAL2_Standard.fbx",Lib1="Assets/AnimationLibrary/UAL1_Standard.fbx",Out="Assets/Resources/Generated/Cinematic/";
 static Dictionary<string,AnimationClip> library=new Dictionary<string,AnimationClip>();
 static void Import(string path){
  var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.optimizeGameObjects=false;importer.importAnimation=true;importer.SaveAndReimport();
  var settings=importer.defaultClipAnimations;
  foreach(var c in settings){c.name=c.name.Split('|').Last();c.loopTime=c.name.Contains("Loop");c.loopPose=c.loopTime;c.lockRootPositionXZ=false;c.keepOriginalPositionXZ=false;c.lockRootRotation=false;c.keepOriginalOrientation=true;c.lockRootHeightY=true;c.heightFromFeet=true;}
  importer.clipAnimations=settings;importer.SaveAndReimport();
  foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(x=>!x.name.StartsWith("__")))library[clip.name]=clip;
 }
 static AnimationClip Bake(string name,float duration,Func<float,(AnimationClip,float)> sample,bool loop=false){
  var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Generated/Clovis.prefab"));var a=go.GetComponentInChildren<Animator>();a.runtimeAnimatorController=null;a.applyRootMotion=false;
  var handler=new HumanPoseHandler(a.avatar,a.transform);var pose=new HumanPose();
  int count=HumanTrait.MuscleCount;var curves=Enumerable.Range(0,count+7).Select(x=>new AnimationCurve()).ToArray();
  Quaternion previous=Quaternion.identity;
  for(int frame=0;frame<=Mathf.CeilToInt(duration*60);frame++){
   float t=Mathf.Min(duration,frame/60f);var pair=sample(t);pair.Item1.SampleAnimation(a.gameObject,pair.Item2);handler.GetHumanPose(ref pose);
   for(int i=0;i<count;i++)curves[i].AddKey(t,pose.muscles[i]);
   var pos=pose.bodyPosition;var q=pose.bodyRotation;if(frame>0&&Quaternion.Dot(previous,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous=q;
   float[] root={0,pos.y,0,q.x,q.y,q.z,q.w};for(int i=0;i<7;i++)curves[count+i].AddKey(t,root[i]);
  }
  var clip=new AnimationClip{name=name,frameRate=60};var props=HumanTrait.MuscleName.Concat(new[]{"RootT.x","RootT.y","RootT.z","RootQ.x","RootQ.y","RootQ.z","RootQ.w"}).ToArray();
  for(int i=0;i<curves.Length;i++)AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),props[i]),curves[i]);
  // Humanoid body translation is normalized by avatar scale. Ground the sampled clip
  // on the target rig before saving, rather than inheriting the source mannequin's offset.
  clip.SampleAnimation(a.gameObject,0);
  float contact=Mathf.Min(a.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,a.GetBoneTransform(HumanBodyBones.RightFoot).position.y);
  float offset=(contact-.07f)/a.humanScale;
  var height=curves[count+1];for(int i=0;i<height.length;i++){var key=height[i];key.value-=offset;height.MoveKey(i,key);}
  AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),"RootT.y"),height);
  var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);
  string path=Out+name+".anim";var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(existing){EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);}else AssetDatabase.CreateAsset(clip,path);
  handler.Dispose();UnityEngine.Object.DestroyImmediate(go);return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
 }
 public static void Build(){
  Directory.CreateDirectory(Out);AssetDatabase.Refresh();Import(Lib1);Import(Lib2);
  Directory.CreateDirectory("Library/ForceDemoReports");File.WriteAllText("Library/ForceDemoReports/animation-inventory.txt",string.Join("\n",library.Select(x=>x.Key+" | "+x.Value.length)));
  var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Resources/Generated/Locomotion.controller");
  var result=new Dictionary<string,AnimationClip>();
  for(int i=1;i<=3;i++){
   int index=i;var action=library["Sword_Regular_"+(char)('A'+i-1)];
   AnimationClip recovery=i<3?library["Sword_Regular_"+(char)('A'+i-1)+"_Rec"]:action;
   float duration=ForceDemo.CombatTuning.AttackDuration(i),swing=i==3?.62f:.43f;
   result["Attack"+i]=Bake("Attack"+i,duration,t=>t<swing?(action,t/swing*(i==3?1.15f:action.length)):(recovery,i==3?Mathf.Lerp(1.15f,action.length,(t-swing)/(duration-swing)):(t-swing)/(duration-swing)*recovery.length));
  }
  var idle=library.ContainsKey("Sword_Idle")?library["Sword_Idle"]:library["Idle_Loop"];
  result["Ready"]=Bake("Ready",idle.length,t=>(idle,t),true);
  var guard=library["Sword_Block"];result["Guard"]=Bake("Guard",1.6f,t=>(guard,.46f+Mathf.Sin(t/1.6f*Mathf.PI*2)*.035f),true);
  var cast=library.First(x=>x.Key.Contains("Spell")&&x.Key.Contains("Idle")).Value;result["Cast"]=Bake("Cast",cast.length,t=>(cast,t),true);
  var hit=library.ContainsKey("Hit_Chest")?library["Hit_Chest"]:library["Hit_Knockback"];result["Hit"]=Bake("Hit",.42f,t=>(hit,t/.42f*hit.length));
  var death=library["Death01"];result["Death"]=Bake("Death",1.4f,t=>(death,t/1.4f*death.length));
  result["Roll"]=AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/WorldAssets/m@FallLandRoll.fbx").OfType<AnimationClip>().First(x=>!x.name.StartsWith("__"));
  foreach(var child in controller.layers[0].stateMachine.states){string key=child.state.name;if(result.TryGetValue(key,out var motion)){child.state.motion=motion;child.state.speed=key=="Roll"?motion.length/ForceDemo.CombatTuning.RollDuration:1;}}
  foreach(string key in new[]{"Hit","Death"}){var sm=controller.layers[0].stateMachine;var state=sm.states.FirstOrDefault(x=>x.state.name==key).state;if(!state)state=sm.AddState(key);state.motion=result[key];state.speed=1;}
  var locomotion=controller.layers[0].stateMachine.states.First(x=>x.state.name=="Locomotion").state.motion as BlendTree;var children=locomotion.children;children[0].motion=result["Ready"];locomotion.children=children;
  if(controller.layers.Length>1)foreach(var child in controller.layers[1].stateMachine.states){string key=child.state.name.Replace("Up","");if(result.TryGetValue(key,out var motion)){child.state.motion=motion;child.state.speed=1;}}
  // A finger-only layer holds the hilt without altering the authored arm motion.
  var grip=new AnimationClip{name="WeaponGrip",frameRate=30};
  foreach(string muscle in HumanTrait.MuscleName)if(muscle.StartsWith("Right ")&&muscle.Contains("Stretched"))AnimationUtility.SetEditorCurve(grip,EditorCurveBinding.FloatCurve("",typeof(Animator),muscle),AnimationCurve.Constant(0,1,muscle.Contains("Thumb")?-.35f:-.80f));
  string gripPath=Out+"WeaponGrip.anim";var oldGrip=AssetDatabase.LoadAssetAtPath<AnimationClip>(gripPath);if(oldGrip){EditorUtility.CopySerialized(grip,oldGrip);UnityEngine.Object.DestroyImmediate(grip);}else{AssetDatabase.CreateAsset(grip,gripPath);oldGrip=grip;}
  string maskPath=Out+"RightFingers.mask";var mask=AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);if(!mask){mask=new AvatarMask();AssetDatabase.CreateAsset(mask,maskPath);}
  foreach(AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))if(part!=AvatarMaskBodyPart.LastBodyPart)mask.SetHumanoidBodyPartActive(part,part==AvatarMaskBodyPart.RightFingers);
  if(controller.layers.Length<3)controller.AddLayer("Weapon grip");var layers=controller.layers;layers[2].avatarMask=mask;layers[2].defaultWeight=1;layers[2].blendingMode=AnimatorLayerBlendingMode.Override;controller.layers=layers;
  var gripMachine=layers[2].stateMachine;foreach(var child in gripMachine.states)gripMachine.RemoveState(child.state);var gripState=gripMachine.AddState("Hold hilt");gripState.motion=oldGrip;gripMachine.defaultState=gripState;EditorUtility.SetDirty(mask);
  EditorUtility.SetDirty(controller);EditorUtility.SetDirty(locomotion);AssetDatabase.SaveAssets();Debug.Log("CINEMATIC_COMBAT_READY "+string.Join(", ",result.Keys));
 }
 public static void Inspect(){library.Clear();Import(Lib1);Import(Lib2);Directory.CreateDirectory("Library/ForceDemoReports");File.WriteAllText("Library/ForceDemoReports/animation-inventory.txt",string.Join("\n",library.Select(x=>x.Key+" | "+x.Value.length)));}
 public static void BuildAndRelease(){LightHeroBuild.Build();Build();VisualAssetBuild.Build();SceneVariantsBuild.Build();DemoBuild.BuildPreparedMac();}
}
