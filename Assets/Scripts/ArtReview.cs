using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
namespace ForceDemo {
public class ArtReview:MonoBehaviour {
 ForceGame g;ForceHUD hud;string output;bool recording,uiOnly;float next;int frame;StringBuilder times=new StringBuilder();
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void StartReview(){var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--art-review")<0&&Array.IndexOf(args,"--ui-review")<0)return;var q=new GameObject("Visual review").AddComponent<ArtReview>();q.uiOnly=Array.IndexOf(args,"--ui-review")>=0;q.output=Path.GetFullPath("../../work/v10/art-review");foreach(string a in args)if(a.StartsWith("--qa-output="))q.output=a.Substring(12);Directory.CreateDirectory(q.output);Directory.CreateDirectory(q.output+"/frames");q.StartCoroutine(q.Run());}
 void Update(){if(recording&&Time.unscaledTime>=next){next=Time.unscaledTime+1/15f;times.AppendLine(Time.unscaledTime.ToString("F6",System.Globalization.CultureInfo.InvariantCulture));ScreenCapture.CaptureScreenshot(output+"/frames/"+frame++.ToString("D4")+".png");}}
 IEnumerator Shot(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(output+"/"+name+".png");yield return null;}
 IEnumerator ReviewUI(){
  yield return Shot("01-menu");hud.SetPage(MenuPage.Side);yield return new WaitForSeconds(.3f);yield return Shot("02-jedi");hud.selection=1;yield return Shot("03-sith");
  hud.SetPage(MenuPage.Controls);yield return Shot("04-controls");hud.SetPage(MenuPage.Settings);yield return Shot("05-settings");
  g.Begin(ForceSide.Light,false);yield return new WaitForSeconds(.6f);yield return Shot("06-hud");
  g.paused=true;Time.timeScale=0;yield return null;yield return Shot("07-pause");g.paused=false;Time.timeScale=1;yield return null;
  g.stage=DemoStage.BossIntro;yield return Shot("08-boss-intro");g.stage=DemoStage.Victory;yield return Shot("09-victory");g.stage=DemoStage.Defeat;yield return Shot("10-defeat");
  File.WriteAllText(output+"/complete.txt","Reviewed home, both side selections, controls, settings, HUD, pause, boss intro and both endings.");
 }
 IEnumerator Run(){
  yield return null;g=FindFirstObjectByType<ForceGame>();g.SkipInput=true;g.muted=true;g.controls.UsingPad=true;hud=g.GetComponent<ForceHUD>();yield return new WaitForSeconds(1.2f);
  if(uiOnly){yield return ReviewUI();Application.Quit(0);yield break;}
  yield return Shot("01-menu");hud.SetPage(MenuPage.Side);yield return new WaitForSeconds(.5f);yield return Shot("02-jedi-selected");hud.selection=1;yield return new WaitForSeconds(.5f);yield return Shot("03-sith-selected");
  g.Begin(ForceSide.Light,false);g.controls.Move=Vector2.zero;yield return new WaitForSeconds(.7f);yield return Shot("04-valley");
  g.enemies[0].body.position=g.player.position+Vector3.forward*4;g.enemies[1].body.position=new Vector3(5,0,4);g.enemies[0].hp=500;g.enemies[0].maxHP=500;g.lockedTarget=g.enemies[0];
  recording=true;next=Time.unscaledTime;g.controls.Move=Vector2.up;yield return new WaitForSeconds(.65f);g.controls.Move=Vector2.zero;yield return new WaitForSeconds(.3f);
  for(int i=0;i<3;i++){g.BasicAttack();yield return new WaitForSeconds(i==2?.44f:.29f);yield return Shot("05-combo-"+i);yield return new WaitForSeconds(i==2?.65f:.53f);}
  g.controls.Block=true;yield return new WaitForSeconds(.3f);g.DamagePlayer(15,g.player.position+g.player.forward*2);yield return new WaitForSeconds(.6f);g.controls.Block=false;g.stamina=100;
  g.Roll(-g.player.right);yield return new WaitForSeconds(1.1f);g.Push();yield return new WaitForSeconds(.6f);recording=false;File.WriteAllText(output+"/times.txt",times.ToString());
  g.lockedTarget=null;g.orbit.yaw=70;yield return new WaitForSeconds(.6f);yield return Shot("06-garden");
  g.StartBoss();g.Teleport(new Vector3(0,0,-1));g.orbit.yaw=0;yield return new WaitForSeconds(.8f);yield return Shot("07-boss");
  g.ReturnToMenu();g.Begin(ForceSide.Dark,false);yield return new WaitForSeconds(.5f);yield return Shot("08-dark");
  File.WriteAllText(output+"/complete.txt","Visual review captured menu, both side selections, terrain, all three attacks, guard, roll, Force, boss and dark path.");Application.Quit(0);
 }
}
}
