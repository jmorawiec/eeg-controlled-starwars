using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
public static class SceneVariantsBuild {
 public static void Build(){
  foreach(string id in new[]{"fir_sapling","shrub_01","shrub_03","celandine_01","rock_moss_set_02"}){
   var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Generated/"+id+".prefab");if(!source)continue;
   var instance=Object.Instantiate(source);var renderers=instance.GetComponentsInChildren<MeshRenderer>();if(renderers.Length<2){Object.DestroyImmediate(instance);continue;}
   string folder="Assets/Resources/Generated/Variants/"+id;Directory.CreateDirectory(folder);AssetDatabase.Refresh();int index=0;
   foreach(var r in renderers){
    var wrapper=new GameObject(id+" variant "+index);var part=Object.Instantiate(r.gameObject);part.transform.SetParent(wrapper.transform,false);part.transform.position=r.transform.position;part.transform.rotation=r.transform.rotation;part.transform.localScale=r.transform.lossyScale;
    var renderer=part.GetComponent<Renderer>();renderer.enabled=true;var bounds=renderer.bounds;bool rock=id.Contains("rock");float size=rock?Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z):bounds.size.y;
    part.transform.localPosition=(part.transform.localPosition-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z))/size;part.transform.localScale/=size;
    PrefabUtility.SaveAsPrefabAsset(wrapper,folder+"/"+(index++)+".prefab");Object.DestroyImmediate(wrapper);
   }
   int triangles=renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);Debug.Log("SCENE_VARIANTS "+id+": "+index+" separate plants/rocks; old group triangles="+triangles);
   Object.DestroyImmediate(instance);
  }
  AssetDatabase.SaveAssets();
 }
 public static void BuildAndRelease(){Build();DemoBuild.BuildPreparedMac();}
}
