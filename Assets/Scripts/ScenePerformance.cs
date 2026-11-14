using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace ForceDemo {
public class ScenePerformance:MonoBehaviour {
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Begin(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-performance")>=0)new GameObject("Performance verification").AddComponent<ScenePerformance>().StartCoroutine(Measure());}
 static IEnumerator Measure(){
  yield return null;var g=FindFirstObjectByType<ForceGame>();g.SkipInput=true;g.muted=true;g.Begin(ForceSide.Light,false);g.controls.Move=Vector2.zero;yield return new WaitForSecondsRealtime(6f);
  var samples=new List<float>();float started=Time.realtimeSinceStartup;while(Time.realtimeSinceStartup-started<5){yield return null;samples.Add(Time.unscaledDeltaTime*1000);}
  samples.Sort();float sum=0;foreach(var v in samples)sum+=v;string report="Scene rendering without screenshot capture: "+Screen.width+"x"+Screen.height+", "+samples.Count+" frames, mean "+(sum/samples.Count).ToString("F1")+" ms, median "+samples[samples.Count/2].ToString("F1")+" ms, p95 "+samples[Mathf.FloorToInt((samples.Count-1)*.95f)].ToString("F1")+" ms. Device: "+SystemInfo.graphicsDeviceName;
  string folder=Path.Combine(Application.persistentDataPath,"Performance");foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--qa-output="))folder=arg.Substring(12);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"result.txt"),report);Debug.Log(report);Application.Quit(0);
 }
}
}
