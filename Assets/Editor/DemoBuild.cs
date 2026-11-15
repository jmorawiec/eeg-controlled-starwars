using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using ForceDemo;

public static class DemoBuild {
    public const string ScenePath="Assets/Scenes/Temple.unity";
    [MenuItem("Force Demo/Prepare arena")]
    public static void Prepare(){
        WorldAssetBuild.Prepare();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("Force Demo").AddComponent<ForceGame>();
        EditorSceneManager.SaveScene(scene,ScenePath);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
        PlayerSettings.companyName="Heroes of the Brain";PlayerSettings.productName="Echoes of the Force";
        PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
        PlayerSettings.macRetinaSupport=false;PlayerSettings.runInBackground=true;PlayerSettings.defaultIsNativeResolution=false;PlayerSettings.resizableWindow=true;PlayerSettings.colorSpace=ColorSpace.Linear;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX,new[]{GraphicsDeviceType.Metal});
        var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var shaders=graphics.FindProperty("m_AlwaysIncludedShaders");
        foreach(string shaderName in new[]{"Standard","Unlit/Color","Skybox/Panoramic","Particles/Standard Unlit","Hidden/ForceAtmosphere","Force/Foliage","Force/Water","Force/Glow","Force/Waterfall"}) {
            Shader shader=Shader.Find(shaderName);bool found=false;
            for(int i=0;i<shaders.arraySize;i++)if(shaders.GetArrayElementAtIndex(i).objectReferenceValue==shader)found=true;
            if(!found){shaders.InsertArrayElementAtIndex(shaders.arraySize);shaders.GetArrayElementAtIndex(shaders.arraySize-1).objectReferenceValue=shader;}
        }
        graphics.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();Debug.Log("FORCE_DEMO_PREPARED");
    }
    public static void BuildMac(){
        Prepare();BuildPreparedMac();
    }
    public static void BuildPreparedMac(){
        NeuroBridgeBuild.Stage();
        PlayerSettings.bundleVersion="0.13.0";PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone,"com.HeroesOfTheBrain.EchoesOfTheForceV13");
        PlayerSettings.macRetinaSupport=false;PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;PlayerSettings.defaultIsNativeResolution=false;
        MotionAudit.Run();CinematicAudit.Run();
        var report=BuildPipeline.BuildPlayer(new[]{ScenePath},"../Echoes of the Force v13.app",BuildTarget.StandaloneOSX,BuildOptions.None);
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Build failed: "+report.summary.result);
        Debug.Log("FORCE_DEMO_BUILD_OK");
    }
    public static void BuildWeb(){
        Prepare();PlayerSettings.WebGL.compressionFormat=WebGLCompressionFormat.Disabled;
        var report=BuildPipeline.BuildPlayer(new[]{ScenePath},"../WebDemo",BuildTarget.WebGL,BuildOptions.None);
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Web build failed");
    }
}
