using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Only present on the complete-project branch. A fresh Editor opens the playable scene.
[InitializeOnLoad]
public static class CompleteProjectBootstrap {
    static CompleteProjectBootstrap(){if(!Application.isBatchMode)EditorApplication.delayCall+=OpenInitialScene;}
    static void OpenInitialScene(){
        if(EditorApplication.isCompiling||EditorApplication.isUpdating){EditorApplication.delayCall+=OpenInitialScene;return;}
        string key="CompleteProject.Scene."+Application.dataPath;
        if(SessionState.GetBool(key,false)||EditorApplication.isPlayingOrWillChangePlaymode)return;
        SessionState.SetBool(key,true);
        var scene=SceneManager.GetActiveScene();
        if(string.IsNullOrEmpty(scene.path)&&!scene.isDirty)EditorSceneManager.OpenScene(DemoBuild.ScenePath,OpenSceneMode.Single);
    }
    public static void VerifyEditorStartup(){
        EditorApplication.delayCall+=()=>{
            bool ready=SceneManager.GetActiveScene().path==DemoBuild.ScenePath;
            Directory.CreateDirectory("Library/ForceDemoReports");
            File.WriteAllText("Library/ForceDemoReports/editor-startup.txt",ready?"PASS: a fresh interactive Editor opened Assets/Scenes/Temple.unity automatically.":"FAIL: playable scene did not open automatically.");
            Debug.Log(ready?"COMPLETE_PROJECT_EDITOR_READY":"COMPLETE_PROJECT_EDITOR_FAILED");
            EditorApplication.Exit(ready?0:1);
        };
    }
    [MenuItem("Force Demo/Verify complete project")]
    public static void Verify(){
        foreach(string path in new[]{"Assets/Adventures_woman/Mesh/Adventures_woman.FBX","Assets/Package Ready Assets/MP_Clovis/Model/MP_Clovis.fbx","Assets/Resources/Generated/BladeVolumetric.mat","Assets/StreamingAssets/NeuroBridge/unity_bridge.py","Assets/StreamingAssets/NeuroBridge/upstream/model/__init__.py","External/GestureInput/model/__init__.py"})
            if(!File.Exists(path))throw new Exception("Missing complete-project dependency: "+path);
        if(AssetDatabase.FindAssets("t:Model",new[]{"Assets/Kevin Iglesias"}).Length<18)throw new Exception("Directional locomotion source clips are incomplete.");
        foreach(string name in new[]{"LightHero","Clovis","Explorer"}){
            string path="Assets/Resources/Generated/"+name+".prefab";
            var instance=PrefabUtility.LoadPrefabContents(path);
            try{
                var animator=instance.GetComponentInChildren<Animator>(true);
                if(!animator||!animator.avatar||!animator.avatar.isHuman||!animator.avatar.isValid||!animator.runtimeAnimatorController)throw new Exception(name+": humanoid or controller is missing.");
                var meshes=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if(meshes.Length==0||meshes.Any(r=>!r.sharedMesh))throw new Exception(name+": source mesh is missing.");
                foreach(var renderer in meshes)foreach(var mat in renderer.sharedMaterials)if(!mat||!mat.shader)throw new Exception(name+": material or shader is missing.");
                if(animator.runtimeAnimatorController.animationClips.Length<20)throw new Exception(name+": locomotion/combat animation dependencies are missing.");
            }finally{PrefabUtility.UnloadPrefabContents(instance);}
        }
        MotionAudit.Run();CinematicAudit.Run();
        Directory.CreateDirectory("Library/ForceDemoReports");
        File.WriteAllText("Library/ForceDemoReports/complete-project.txt","PASS: all Asset Store dependencies, LightHero/Clovis/Explorer meshes, avatars, materials and animation controllers resolved after a clean Unity import. Motion and combat audits passed. Prepared EEG bridge is present.");
        Debug.Log("COMPLETE_PROJECT_IMPORT_OK");
    }
    public static void VerifyAndBuild(){Verify();DemoBuild.BuildPreparedMac();}
}
