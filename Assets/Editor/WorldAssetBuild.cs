using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class WorldAssetBuild {
    const string Source="Assets/Resources/WorldAssets/",Output="Assets/Resources/Generated/";
    static Texture2D Tex(string name){return AssetDatabase.LoadAssetAtPath<Texture2D>(Source+name);}
    static void Save(UnityEngine.Object asset,string name){string p=Output+name;var old=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);if(old){EditorUtility.CopySerialized(asset,old);UnityEngine.Object.DestroyImmediate(asset);}else AssetDatabase.CreateAsset(asset,p);}
    public static void Prepare(){
        Directory.CreateDirectory(Output);AssetDatabase.Refresh();
        foreach(string p in AssetDatabase.FindAssets("t:Texture",new[]{Source.TrimEnd('/')} ).Select(AssetDatabase.GUIDToAssetPath)){
            var importer=AssetImporter.GetAtPath(p) as TextureImporter;if(importer==null)continue;
            bool normal=p.IndexOf("normal",StringComparison.OrdinalIgnoreCase)>=0;
            importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.maxTextureSize=p.Contains("dawn")?2048:2048;importer.mipmapEnabled=true;importer.anisoLevel=8;
            importer.alphaIsTransparency=p.Contains("fern_02_albedo")||p.Contains("grass_medium_01_albedo");
            importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
        }
        foreach(string id in new[]{"boulder_01","coastal_cliff_01","rock_moss_set_01","fern_02","grass_medium_01"}){
            string p=Source+id+".fbx";var importer=(ModelImporter)AssetImporter.GetAtPath(p);importer.importAnimation=false;importer.materialImportMode=ModelImporterMaterialImportMode.None;
            importer.meshCompression=ModelImporterMeshCompression.Medium;importer.isReadable=true;importer.SaveAndReimport();
            bool foliage=id.Contains("fern")||id.Contains("grass");
            var mat=new Material(Shader.Find(foliage?"Force/Foliage":"Standard"));mat.mainTexture=Tex(id+"_albedo.png");mat.color=Color.white;mat.enableInstancing=true;
            if(foliage)mat.SetFloat("_Cutoff",.38f);else {mat.SetTexture("_BumpMap",Tex(id+"_normal.png"));mat.EnableKeyword("_NORMALMAP");mat.SetFloat("_Glossiness",.12f);}
            Save(mat,id+".mat");mat=AssetDatabase.LoadAssetAtPath<Material>(Output+id+".mat");
            var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(p));model.name="Scan";
            var wrapper=new GameObject(id);model.transform.SetParent(wrapper.transform,false);
            if(foliage){
                var original=model.GetComponentsInChildren<MeshRenderer>().First();
                var instance=new GameObject("Plant");instance.transform.SetParent(wrapper.transform,false);
                instance.transform.position=original.transform.position;instance.transform.rotation=original.transform.rotation;instance.transform.localScale=original.transform.lossyScale;
                instance.AddComponent<MeshFilter>().sharedMesh=original.GetComponent<MeshFilter>().sharedMesh;instance.AddComponent<MeshRenderer>().sharedMaterial=mat;
                UnityEngine.Object.DestroyImmediate(model);model=instance;
            }
            foreach(var r in model.GetComponentsInChildren<Renderer>())r.sharedMaterials=Enumerable.Repeat(mat,r.sharedMaterials.Length).ToArray();
            var renderers=model.GetComponentsInChildren<Renderer>();Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            float size=foliage?bounds.size.y:Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
            model.transform.localScale/=size;model.transform.localPosition=(model.transform.localPosition-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z))/size;
            if(!foliage){
                var groups=model.GetComponentsInChildren<LODGroup>();
                var collisionRenderers=groups.Length>0?groups.SelectMany(group=>group.GetLODs().Last().renderers).ToArray():renderers;
                foreach(var renderer in collisionRenderers){var filter=renderer.GetComponent<MeshFilter>();if(!filter)continue;Physics.BakeMesh(filter.sharedMesh.GetEntityId(),false);var collision=renderer.gameObject.AddComponent<MeshCollider>();collision.sharedMesh=filter.sharedMesh;}
            }
            Debug.Log("SCAN "+id+" renderers="+renderers.Length+" LOD groups="+model.GetComponentsInChildren<LODGroup>().Length);
            PrefabUtility.SaveAsPrefabAsset(wrapper,Output+id+".prefab");UnityEngine.Object.DestroyImmediate(wrapper);
        }
        Tree();
        Surface("Stone","rock_face_03",new Color(.52f,.61f,.6f));Surface("Moss","mossy_rock",new Color(.7f,.8f,.7f));Surface("Ground","forest_floor",new Color(.48f,.62f,.47f));
        var sky=new Material(Shader.Find("Skybox/Panoramic"));sky.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture>(Source+"dawn.hdr"));sky.SetFloat("_Exposure",.6f);sky.SetFloat("_Rotation",150);Save(sky,"DawnSky.mat");
        Save(new Material(Shader.Find("Hidden/ForceAtmosphere")),"Atmosphere.mat");
        var dust=new Material(Shader.Find("Particles/Standard Unlit"));dust.SetFloat("_Mode",2);dust.SetFloat("_SrcBlend",5);dust.SetFloat("_DstBlend",1);dust.SetFloat("_ZWrite",0);dust.EnableKeyword("_ALPHABLEND_ON");dust.renderQueue=3000;dust.color=new Color(1,.85f,.6f,.4f);Save(dust,"Dust.mat");
        PolishAssets.Build();
        Character();EnemyModels.Build();StoreAssetsBuild.Prepare();LightHeroBuild.Build();CinematicBuild.Build();VisualAssetBuild.Build();SceneVariantsBuild.Build();AssetDatabase.SaveAssets();Debug.Log("FORCE_WORLD_ASSETS_READY");
    }
    static void Tree(){
        string id="fir_sapling",p=Source+id+".fbx";if(!File.Exists(p))return;
        var importer=(ModelImporter)AssetImporter.GetAtPath(p);importer.importAnimation=false;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.meshCompression=ModelImporterMeshCompression.Medium;importer.SaveAndReimport();
        var bark=new Material(Shader.Find("Standard"));bark.mainTexture=Tex(id+"_bark.png");if(!bark.mainTexture)bark.mainTexture=Tex(id+"_bark.jpg");bark.color=new Color(.65f,.7f,.6f);bark.SetFloat("_Glossiness",.1f);bark.enableInstancing=true;Save(bark,"TreeBark.mat");
        var leaves=new Material(Shader.Find("Force/Foliage"));leaves.mainTexture=Tex(id+"_leaves.png");leaves.color=new Color(.7f,.85f,.65f);leaves.SetFloat("_Cutoff",.4f);leaves.SetFloat("_Wind",.025f);leaves.enableInstancing=true;Save(leaves,"TreeLeaves.mat");
        var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(p));var wrapper=new GameObject(id);model.transform.SetParent(wrapper.transform,false);
        foreach(var r in model.GetComponentsInChildren<Renderer>()){
            var slots=r.sharedMaterials;for(int i=0;i<slots.Length;i++){string n=slots[i]?slots[i].name.ToLowerInvariant():"";slots[i]=AssetDatabase.LoadAssetAtPath<Material>(Output+(n.Contains("twig")||n.Contains("leaf")||n.Contains("leav")?"TreeLeaves.mat":"TreeBark.mat"));Debug.Log("TREE_SLOT "+r.name+" "+n);}r.sharedMaterials=slots;
        }
        var renderers=model.GetComponentsInChildren<Renderer>();Bounds b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);float size=b.size.y;model.transform.localScale=Vector3.one/size;model.transform.localPosition=new Vector3(-b.center.x,-b.min.y,-b.center.z)/size;
        PrefabUtility.SaveAsPrefabAsset(wrapper,Output+id+".prefab");UnityEngine.Object.DestroyImmediate(wrapper);
    }
    static void Surface(string name,string id,Color tint){var m=new Material(Shader.Find("Standard"));m.mainTexture=Tex(id+"_albedo.jpg");m.SetTexture("_BumpMap",Tex(id+"_normal.jpg"));m.EnableKeyword("_NORMALMAP");m.SetFloat("_BumpScale",.8f);m.SetFloat("_Glossiness",.12f);m.color=tint;m.enableInstancing=true;Save(m,name+".mat");}
    static AnimationClip Clip(string file,string name,bool loop=true){
        var importer=(ModelImporter)AssetImporter.GetAtPath(Source+file+".fbx");
        importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.importAnimation=true;
        if(file.StartsWith("Combat")){
            var description=importer.humanDescription;var bones=new List<HumanBone>();
            foreach(Match match in Regex.Matches(File.ReadAllText("Assets/Editor/TF-avatar-map.txt"),@"boneName: ([^\r\n]+)\s+humanName: ([^\r\n]+)"))bones.Add(new HumanBone{boneName=match.Groups[1].Value.Trim(),humanName=match.Groups[2].Value.Trim(),limit=new HumanLimit{useDefaultValues=true}});
            description.human=bones.ToArray();importer.humanDescription=description;
        }
        importer.SaveAndReimport();
        var clips=importer.defaultClipAnimations;
        foreach(var c in clips){
            c.name=name;c.loopTime=loop;c.loopPose=loop;
            // Extract the source trajectory. CharacterController owns world movement;
            // baking XZ into the skeleton makes the mesh run away then snap each loop.
            c.lockRootPositionXZ=false;c.keepOriginalPositionXZ=false;
            c.lockRootRotation=false;c.keepOriginalOrientation=file.StartsWith("Combat");
            c.lockRootHeightY=name!="Roll";c.keepOriginalPositionY=false;c.heightFromFeet=name!="Roll";
        }
        importer.clipAnimations=clips;importer.SaveAndReimport();
        var clip=AssetDatabase.LoadAllAssetsAtPath(Source+file+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
        Debug.Log("MOTION_CLIP "+name+" duration="+clip.length+" human="+clip.isHumanMotion+" velocity="+clip.averageSpeed);
        return clip;
    }
    static void Character(){
        string characterFile=File.Exists(Source+"Terraformer_A.fbx")?"Terraformer_A.fbx":"DefaultMale.fbx";
        var importer=(ModelImporter)AssetImporter.GetAtPath(Source+characterFile);importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.importAnimation=false;
        if(characterFile=="Terraformer_A.fbx"){
            var description=importer.humanDescription;var mapped=new List<HumanBone>();
            foreach(Match m in Regex.Matches(File.ReadAllText("Assets/Editor/TF-avatar-map.txt"),@"boneName: ([^\r\n]+)\s+humanName: ([^\r\n]+)"))mapped.Add(new HumanBone{boneName=m.Groups[1].Value.Trim(),humanName=m.Groups[2].Value.Trim(),limit=new HumanLimit{useDefaultValues=true}});
            description.human=mapped.ToArray();importer.humanDescription=description;
        }
        importer.SaveAndReimport();
        var idle=Clip("m@Idle","Idle");var walk=Clip("m@WalkForwards","Walk");var run=Clip("m@RunForwards","Run");
        var right=Clip("CombatStrafe","StrafeRight");var fastRight=right;var back=Clip("CombatBackward","Backward");var diagonal=Clip("CombatDiagonal","ForwardRight");
        var roll=Clip("m@FallLandRoll","Roll",false);var attack=Clip("CombatAttack","Attack",false);var guard=Clip("CombatGuard","Guard");
        string path=Output+"Locomotion.controller";
        // Keep a stable asset GUID when regenerating the state machine.
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if(!controller)controller=AnimatorController.CreateAnimatorControllerAtPath(path);
        foreach(var parameter in controller.parameters)controller.RemoveParameter(parameter);
        controller.AddParameter("Speed",AnimatorControllerParameterType.Float);controller.AddParameter("MoveX",AnimatorControllerParameterType.Float);controller.AddParameter("MoveZ",AnimatorControllerParameterType.Float);
        var sm=controller.layers[0].stateMachine;foreach(var state in sm.states)sm.RemoveState(state.state);
        var stateIdle=sm.AddState("Locomotion");sm.defaultState=stateIdle;
        var tree=new BlendTree{name="Directional locomotion",blendType=BlendTreeType.FreeformDirectional2D,blendParameter="MoveX",blendParameterY="MoveZ",useAutomaticThresholds=false};AssetDatabase.AddObjectToAsset(tree,controller);
        tree.AddChild(idle,Vector2.zero);tree.AddChild(walk,new Vector2(0,1.8f));tree.AddChild(run,new Vector2(0,3.6f));
        tree.AddChild(right,new Vector2(1.8f,0));tree.AddChild(right,new Vector2(-1.8f,0));
        tree.AddChild(fastRight,new Vector2(3.4f,0));tree.AddChild(fastRight,new Vector2(-3.4f,0));
        tree.AddChild(back,new Vector2(0,-3.4f));
        tree.AddChild(diagonal,new Vector2(2.4f,2.4f));tree.AddChild(diagonal,new Vector2(-2.4f,2.4f));
        var children=tree.children;foreach(int index in new[]{4,6,9}){var child=children[index];child.mirror=true;children[index]=child;}tree.children=children;stateIdle.motion=tree;
        for(int i=1;i<=3;i++){var state=sm.AddState("Attack"+i);state.motion=attack;state.speed=attack.length/(i==3?.68f:.58f);}
        var rolling=sm.AddState("Roll");rolling.motion=roll;rolling.speed=roll.length/ForceDemo.CombatTuning.RollDuration;
        var guarding=sm.AddState("Guard");guarding.motion=guard;
        var casting=sm.AddState("Cast");casting.motion=guard;
        CombatClips.Apply(controller);
        var mat=new Material(Shader.Find("Standard"));mat.mainTexture=Tex("DefaultMale_Albedo.tif");mat.SetTexture("_BumpMap",Tex("DefaultMale_Normal.tif"));mat.EnableKeyword("_NORMALMAP");mat.SetTexture("_OcclusionMap",Tex("DefaultMale_Occlusion.tif"));mat.SetFloat("_Glossiness",.25f);mat.SetFloat("_Metallic",.12f);mat.color=new Color(.42f,.47f,.42f);Save(mat,"ExplorerSuit.mat");
        var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Source+characterFile));model.name="Humanoid";
        var wrapper=new GameObject("Explorer");model.transform.SetParent(wrapper.transform,false);
        foreach(var r in model.GetComponentsInChildren<Renderer>()){
            var slots=r.sharedMaterials;
            for(int i=0;i<slots.Length;i++){
                string n=slots[i]?slots[i].name:"Suit";string lower=n.ToLowerInvariant();
                string part=lower.Contains("backpack")?"Backpack":lower.Contains("helmet")?"Helmet":lower.Contains("hair")?"Hair":lower.Contains("teeth")?"Teeth":lower.Contains("eye")?"Eye":(lower.Contains("skin")||lower.Contains("head"))?"Head":"Body";
                string materialName="Terraformer_"+part+(lower.Contains("glass")?"_Glass":"");
                var asset=AssetDatabase.LoadAssetAtPath<Material>(Output+materialName+".mat");
                if(!asset){var m=new Material(Shader.Find("Standard"));m.mainTexture=Tex("Terraformer_A_"+part+"_Albedo.png");m.SetTexture("_BumpMap",Tex("Terraformer_A_"+part+"_Normal.png"));m.EnableKeyword("_NORMALMAP");m.SetFloat("_Glossiness",lower.Contains("glass")?.8f:.35f);m.SetFloat("_Metallic",lower.Contains("glass")?.65f:part=="Helmet"?.35f:0);m.color=lower.Contains("glass")?new Color(.18f,.24f,.23f):Color.white;Save(m,materialName+".mat");asset=AssetDatabase.LoadAssetAtPath<Material>(Output+materialName+".mat");}
                slots[i]=asset;Debug.Log("CHARACTER_SLOT "+r.name+" "+n+" => "+part);
            }r.sharedMaterials=slots;
        }
        var animator=model.GetComponent<Animator>();if(!animator)animator=model.AddComponent<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        if(!animator.avatar){var avatar=AssetDatabase.LoadAllAssetsAtPath(Source+characterFile).OfType<Avatar>().FirstOrDefault();animator.avatar=avatar;}
        Debug.Log("HUMANOID_AVATAR valid="+(animator.avatar&&animator.avatar.isValid)+" human="+(animator.avatar&&animator.avatar.isHuman));
        var renderers=model.GetComponentsInChildren<Renderer>();Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        float scale=1.86f/bounds.size.y;model.transform.localScale=Vector3.one*scale;model.transform.localPosition=Vector3.up*(-bounds.min.y*scale);
        PrefabUtility.SaveAsPrefabAsset(wrapper,Output+"Explorer.prefab");UnityEngine.Object.DestroyImmediate(wrapper);
    }
}
