using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
public static class CinematicAudit {
 public static void Run(){
  var report=new StringBuilder();foreach(string profile in new[]{"Clovis","LightHero","Explorer"}){var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Generated/"+profile+".prefab"));var animator=go.GetComponentInChildren<Animator>();animator.Rebind();animator.Update(0);animator.runtimeAnimatorController=null;
  for(int i=1;i<=3;i++){
   var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Resources/Generated/Cinematic/Attack"+i+".anim");Vector3 lo=Vector3.one*100,hi=Vector3.one*-100;float minFoot=100;
   for(int j=0;j<=60;j++){clip.SampleAnimation(animator.gameObject,clip.length*j/60f);Vector3 hand=go.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightHand).position);lo=Vector3.Min(lo,hand);hi=Vector3.Max(hi,hand);minFoot=Mathf.Min(minFoot,animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y);}
   report.AppendLine(profile+" / "+clip.name+": duration="+clip.length+", hand arc="+(hi-lo).magnitude+", minimum foot="+minFoot+", humanoid="+clip.isHumanMotion);
   if(!clip.isHumanMotion||(hi-lo).magnitude<.3f||minFoot<-.15f||minFoot>.17f)throw new Exception("Invalid combat animation: "+report);
  }
  UnityEngine.Object.DestroyImmediate(go);}
  Directory.CreateDirectory("Library/ForceDemoReports");File.WriteAllText("Library/ForceDemoReports/combat-audit.txt",report.ToString());Debug.Log("CINEMATIC_AUDIT_OK\n"+report);
 }
}
