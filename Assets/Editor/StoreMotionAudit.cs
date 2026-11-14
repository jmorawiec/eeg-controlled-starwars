using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
public static class StoreMotionAudit {
 public static void Run(){
  var report=new StringBuilder();
  foreach(string name in new[]{"Explorer","Clovis","Hero"}){
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Generated/"+name+".prefab");if(!prefab)continue;
   var instance=UnityEngine.Object.Instantiate(prefab);var animator=instance.GetComponentInChildren<Animator>();
   if(!animator.isHuman||!animator.avatar.isValid)throw new Exception(name+": invalid avatar");
   foreach(var velocity in new[]{new Vector2(0,1.8f),new Vector2(0,3.4f),new Vector2(0,5.2f),new Vector2(-3.4f,0),new Vector2(3.4f,0),new Vector2(0,-3.4f),new Vector2(2.4f,2.4f),new Vector2(-2.4f,-2.4f)}){
    animator.Rebind();animator.Update(0);animator.SetFloat("MoveX",velocity.x);animator.SetFloat("MoveZ",velocity.y);
    float maxOffset=0,maxJump=0;Vector3 previous=Vector3.zero;Vector3 low=Vector3.one*100,high=Vector3.one*-100;
    for(int i=0;i<180;i++){
     animator.Update(1/60f);Vector3 hip=instance.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
     maxOffset=Mathf.Max(maxOffset,new Vector2(hip.x,hip.z).magnitude);if(i>0)maxJump=Mathf.Max(maxJump,Vector3.Distance(hip,previous));previous=hip;
     var foot=instance.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position);low=Vector3.Min(low,foot);high=Vector3.Max(high,foot);
    }
    report.AppendLine(name+" "+velocity+" maxHipOffset="+maxOffset+" maxLoopStep="+maxJump+" footTravel="+(high-low).magnitude);
    if(maxOffset>.35f||maxJump>.2f||(high-low).magnitude<.3f)throw new Exception("Retargeted motion failed: "+report);
   }
   foreach(var renderer in instance.GetComponentsInChildren<Renderer>())foreach(var mat in renderer.sharedMaterials)if(!mat||!mat.shader||mat.shader.name.Contains("Error"))throw new Exception(name+": unsupported material");
   UnityEngine.Object.DestroyImmediate(instance);
  }
  Directory.CreateDirectory("Library/ForceDemoReports");File.WriteAllText("Library/ForceDemoReports/rig-audit.txt",report.ToString());Debug.Log("STORE_RIG_AUDIT_OK\n"+report);
 }
}
