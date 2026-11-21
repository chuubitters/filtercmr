#pragma warning disable
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace UnityEditor {
  public sealed class MenuItem : Attribute { public MenuItem(string itemName) {} public int priority; }
  public sealed class InitializeOnLoadAttribute : Attribute {}
  public static class EditorApplication {
    public delegate void CallbackFunction();
    public static CallbackFunction delayCall;
    public static bool isPlaying => false; public static bool isPlayingOrWillChangePlaymode => false;
  }
  public static class SessionState { public static bool GetBool(string key, bool defaultValue) => false; public static void SetBool(string key, bool value) {} }
  public static class EditorUtility {
    public static bool DisplayDialog(string title, string message, string ok) => true;
    public static bool DisplayDialog(string title, string message, string ok, string cancel) => true;
    public static void SetDirty(UnityEngine.Object target) {}
  }
  public static class AssetDatabase {
    public static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object => null;
    public static void CreateAsset(UnityEngine.Object asset, string path) {} public static void SaveAssets() {}
    public static bool IsValidFolder(string path) => true; public static string CreateFolder(string parentFolder, string newFolderName) => "";
  }
  public static class Selection { public static GameObject activeGameObject { get; set; } }
  public static class Undo {
    public static void RegisterCreatedObjectUndo(UnityEngine.Object objectToUndo, string name) {}
    public static T AddComponent<T>(GameObject gameObject) where T : Component => null;
  }
  public enum SerializedPropertyType { Generic = -1, Integer = 0, ObjectReference = 5 }
  public class SerializedProperty {
    public string name => ""; public SerializedPropertyType propertyType => 0; public UnityEngine.Object objectReferenceValue { get; set; }
    public bool NextVisible(bool enterChildren) => false;
  }
  public class SerializedObject {
    public SerializedObject(UnityEngine.Object obj) {} public UnityEngine.Object targetObject => null;
    public SerializedProperty FindProperty(string propertyPath) => null; public SerializedProperty GetIterator() => null;
    public bool ApplyModifiedPropertiesWithoutUndo() => true;
  }
  public class EditorBuildSettingsScene { public EditorBuildSettingsScene(string path, bool enabled) {} public string path { get; set; } }
  public class EditorBuildSettings { public static EditorBuildSettingsScene[] scenes { get; set; } }
  public sealed partial class PlayerSettings : UnityEngine.Object {
    public static bool runInBackground { get; set; } public static string productName { get; set; }
    public class macOS { public static string cameraUsageDescription { get; set; } }
  }
}
namespace UnityEditor.SceneManagement {
  public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
  public enum NewSceneMode { Single, Additive }
  public enum OpenSceneMode { Single, Additive, AdditiveWithoutLoading }
  public sealed class EditorSceneManager : UnityEngine.SceneManagement.SceneManager {
    public static bool SaveCurrentModifiedScenesIfUserWantsTo() => true;
    public static Scene OpenScene(string scenePath, OpenSceneMode mode) => default;
    public static Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;
    public static bool SaveScene(Scene scene, string dstScenePath) => true;
    public static bool MarkSceneDirty(Scene scene) => true;
  }
}
