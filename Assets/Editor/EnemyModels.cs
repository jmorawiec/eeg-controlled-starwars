using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Builds the robed humanoid ("Acolyte") from the second rig in the project, so casters and the mentor
// no longer share a body with the armoured sentinels.
public static class EnemyModels {
    const string Source = "Assets/Resources/WorldAssets/", Output = "Assets/Resources/Generated/";
    public static void Build() {
        string p = Source + "DefaultMale.fbx"; if (!File.Exists(p)) { Debug.LogWarning("DefaultMale.fbx missing"); return; }
        var importer = (ModelImporter)AssetImporter.GetAtPath(p);
        importer.animationType = ModelImporterAnimationType.Human; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; importer.importAnimation = false; importer.SaveAndReimport();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Output + "Locomotion.controller");
        var suitSource = AssetDatabase.LoadAssetAtPath<Material>(Output + "ExplorerSuit.mat");
        string matPath = Output + "AcolyteSuit.mat"; var suit = suitSource ? new Material(suitSource) : new Material(Shader.Find("Standard"));
        suit.color = Color.white; suit.SetFloat("_Glossiness", .12f); suit.SetFloat("_Metallic", 0); suit.enableInstancing = true;
        var old = AssetDatabase.LoadAssetAtPath<Material>(matPath); if (old) { EditorUtility.CopySerialized(suit, old); Object.DestroyImmediate(suit); } else AssetDatabase.CreateAsset(suit, matPath);
        suit = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(p)); model.name = "Humanoid";
        var wrapper = new GameObject("Acolyte"); model.transform.SetParent(wrapper.transform, false);
        foreach (var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterials = Enumerable.Repeat(suit, r.sharedMaterials.Length).ToArray();
        var animator = model.GetComponent<Animator>(); if (!animator) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (!animator.avatar) animator.avatar = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
        Debug.Log("ACOLYTE_AVATAR valid=" + (animator.avatar && animator.avatar.isValid) + " human=" + (animator.avatar && animator.avatar.isHuman));
        var renderers = model.GetComponentsInChildren<Renderer>(); Bounds b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
        float scale = 1.84f / b.size.y; model.transform.localScale = Vector3.one * scale; model.transform.localPosition = Vector3.up * (-b.min.y * scale);
        PrefabUtility.SaveAsPrefabAsset(wrapper, Output + "Acolyte.prefab"); Object.DestroyImmediate(wrapper);
        AssetDatabase.SaveAssets(); Debug.Log("ENEMY_MODELS_READY");
    }
}
