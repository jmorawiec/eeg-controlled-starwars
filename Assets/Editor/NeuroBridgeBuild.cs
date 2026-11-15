using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class NeuroBridgeBuild {
    [MenuItem("Force Demo/Stage EEG bridge")]
    public static void Stage(){
        string root=Path.GetFullPath("."),upstream=Path.Combine(root,"External/GestureInput"),output=Path.Combine(root,"Assets/StreamingAssets/NeuroBridge");
        if(!File.Exists(Path.Combine(upstream,"eeg/recorder.py")))throw new Exception("Initialize External/GestureInput: git submodule update --init --recursive");
        if(Directory.Exists(output))Directory.Delete(output,true);Directory.CreateDirectory(output);
        File.Copy(Path.Combine(root,"tools/NeuroBridge/unity_bridge.py"),Path.Combine(output,"unity_bridge.py"),true);
        File.Copy(Path.Combine(root,"tools/NeuroBridge/requirements-eeg.txt"),Path.Combine(output,"requirements-eeg.txt"),true);
        foreach(string file in Directory.GetFiles(upstream,"*",SearchOption.AllDirectories)){
            string relative=Path.GetRelativePath(upstream,file).Replace('\\','/');
            bool source=relative.EndsWith(".py")||relative=="LICENSE"||relative=="README.md"||relative=="eeg/TRAINING.md"||relative=="eeg/README.md";
            bool model=relative=="model/hand_landmarker.task"||relative=="model/keypoint_classifier/keypoint_classifier.tflite"||relative=="model/keypoint_classifier/keypoint_classifier_label.csv"||relative=="eeg/models/live_models.joblib";
            if(relative.Contains("__pycache__")||relative.StartsWith(".git")||relative.StartsWith("eeg/data/")||!(source||model))continue;
            string dest=Path.Combine(output,"upstream",relative);Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(file,dest,true);
        }
        AssetDatabase.Refresh();Debug.Log("NEURO_BRIDGE_STAGED: upstream 57f56f9; no participant recordings copied");
    }
}
