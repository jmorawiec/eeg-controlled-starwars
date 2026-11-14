using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
public static class MotionAudit {
 public static void Run(){
  var report=new StringBuilder();
  foreach(var name in new[]{"m@Idle","m@WalkForwards","m@RunForwards","m@FallLandRoll"}){
   foreach(var clip in AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/WorldAssets/"+name+".fbx").OfType<AnimationClip>())if(!clip.name.StartsWith("__")){
    report.AppendLine(name+" clip="+clip.name+" length="+clip.length+" human="+clip.isHumanMotion+" speed="+clip.averageSpeed+" empty="+clip.empty+" bindings="+AnimationUtility.GetCurveBindings(clip).Length);
   }
  }
  var instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Generated/Explorer.prefab"));var animator=instance.GetComponentInChildren<Animator>();animator.Rebind();animator.Update(0);animator.SetFloat("Speed",3.4f);animator.SetFloat("MoveZ",3.4f);
  float maxHipOffset=0;Vector3 footMin=Vector3.one*100,footMax=Vector3.one*-100;
  for(int i=0;i<130;i++){
   animator.Update(1/60f);
   var h=instance.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);maxHipOffset=Mathf.Max(maxHipOffset,new Vector2(h.x,h.z).magnitude);
   var f=instance.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position);footMin=Vector3.Min(footMin,f);footMax=Vector3.Max(footMax,f);
   if(i%10==0){var hips=animator.GetBoneTransform(HumanBodyBones.Hips);var foot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);report.AppendLine(i+" root="+animator.transform.localPosition.ToString("F3")+" hip="+instance.transform.InverseTransformPoint(hips.position).ToString("F3")+" foot="+instance.transform.InverseTransformPoint(foot.position).ToString("F3")+" state="+animator.GetCurrentAnimatorStateInfo(0).normalizedTime.ToString("F3"));}
  }
  Object.DestroyImmediate(instance);Directory.CreateDirectory("Library/ForceDemoReports");File.WriteAllText("Library/ForceDemoReports/after-import.txt",report.ToString());Debug.Log(report.ToString());
  if(maxHipOffset>.35f)throw new System.Exception("Animation mesh escapes controller: hip offset="+maxHipOffset);
  if((footMax-footMin).magnitude<.15f)throw new System.Exception("Locomotion has no visible foot movement");
  Debug.Log("MOTION_IMPORT_VERIFIED maxHipOffset="+maxHipOffset+" footExcursion="+(footMax-footMin).magnitude);
 }
}
