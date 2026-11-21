import subprocess, re, sys
import os
UCS=os.environ.get("UCS","/tmp/ucs"); U7=os.environ.get("UGUI7","/tmp/ugui/com.unity.ugui/Runtime"); U18=os.environ.get("UGUI2018","/tmp/ugui2018/UnityEngine.UI")
def g(root, pattern):
    r=subprocess.run(["grep","-rEl","--include=*.cs",pattern,root],capture_output=True,text=True)
    return [l for l in r.stdout.splitlines()]
checks = {
 # (root(s), regex) — engine
 "Object.Destroy(Object)": ([UCS], r"public static void Destroy\(Object obj\)"),
 "Object.FindObjectOfType<T>(bool includeInactive)": ([UCS], r"public static T FindObjectOfType<T>\(bool includeInactive\)"),
 "Object.FindObjectsOfType<T>()": ([UCS], r"public static T\[\] FindObjectsOfType<T>\(\)"),
 "Object.hideFlags": ([UCS], r"public extern HideFlags hideFlags"),
 "HideFlags.DontSave": ([UCS], r"DontSave = "),
 "MonoBehaviour.StartCoroutine(IEnumerator)": ([UCS], r"public Coroutine StartCoroutine\(IEnumerator routine\)"),
 "MonoBehaviour.StopCoroutine(Coroutine)": ([UCS], r"public void StopCoroutine\(Coroutine routine\)"),
 "ScriptableObject.CreateInstance<T>()": ([UCS], r"public static T CreateInstance<T>\(\)"),
 "GameObject(string, params Type[])": ([UCS], r"public GameObject\(string name, params Type\[\] components\)"),
 "GameObject.layer/SetActive/activeSelf": ([UCS], r"public extern void SetActive\(bool value\)"),
 "Transform.Find(string)": ([UCS], r"public Transform Find\(string n\)"),
 "Transform.SetParent(Transform,bool)": ([UCS], r"public extern void SetParent\(Transform parent, bool worldPositionStays\)"),
 "Transform.localEulerAngles": ([UCS], r"public Vector3 localEulerAngles"),
 "Transform.SetAsFirstSibling/Last": ([UCS], r"public extern void SetAsLastSibling\(\)"),
 "RectTransform.offsetMin": ([UCS], r"public Vector2 offsetMin"),
 "RectTransform.sizeDelta/anchoredPosition/pivot": ([UCS], r"public extern Vector2 anchoredPosition|public Vector2 anchoredPosition"),
 "Vector2.LerpUnclamped": ([UCS], r"public static Vector2 LerpUnclamped\(Vector2 a, Vector2 b, float t\)"),
 "Vector2 -> Vector4 implicit": ([UCS], r"public static implicit operator Vector4\(Vector2 v\)"),
 "Rect(x,y,w,h) / xMin / yMax / ==": ([UCS], r"public static bool operator==\(Rect lhs, Rect rhs\)"),
 "Color ==": ([UCS], r"public static bool operator==\(Color lhs, Color rhs\)"),
 "Color32 <-> Color implicit": ([UCS], r"public static implicit operator Color\(Color32 c\)"),
 "ColorUtility.ToHtmlStringRGB": ([UCS], r"public static string ToHtmlStringRGB\(Color color\)"),
 "Mathf.Repeat/Approximately/RoundToInt/Exp": ([UCS], r"public static float Repeat\(float t, float length\)"),
 "Mathf.Approximately": ([UCS], r"public static bool Approximately\(float a, float b\)"),
 "Debug.LogError(object, Object)": ([UCS], r"public static void LogError\(object message, Object context\)"),
 "Time.unscaledTime/unscaledDeltaTime": ([UCS], r"public static extern float unscaledDeltaTime"),
 "Input.GetKeyDown(KeyCode)": ([UCS], r"public static bool GetKeyDown\(KeyCode key\)"),
 "Application.runInBackground": ([UCS], r"public extern static bool runInBackground|public static extern bool runInBackground"),
 "Application.isBatchMode": ([UCS], r"isBatchMode"),
 "Application.RequestUserAuthorization": ([UCS], r"public static extern AsyncOperation RequestUserAuthorization\(UserAuthorization mode\)|AsyncOperation RequestUserAuthorization\(UserAuthorization mode\)"),
 "Application.HasUserAuthorization": ([UCS], r"bool HasUserAuthorization\(UserAuthorization mode\)"),
 "UserAuthorization.WebCam": ([UCS], r"WebCam = 1"),
 "Resources.GetBuiltinResource<T>(string)": ([UCS], r"public static T GetBuiltinResource<T>\(string path\)"),
 "Texture.wrapMode/filterMode": ([UCS], r"public extern TextureWrapMode wrapMode|public TextureWrapMode wrapMode"),
 "Texture2D(int,int,TextureFormat,bool)": ([UCS], r"public Texture2D\(int width, int height, TextureFormat textureFormat, bool mipChain\)"),
 "TextureFormat.RGB24": ([UCS], r"RGB24 = 3"),
 "ImageConversion.LoadImage(this Texture2D, byte[], bool)": ([UCS], r"LoadImage\(\[NotNull\] this Texture2D tex, byte\[\] data, bool markNonReadable\)"),
 "WebCamTexture(string,int,int,int)": ([UCS], r"public WebCamTexture\(string deviceName, int requestedWidth, int requestedHeight, int requestedFPS\)"),
 "WebCamTexture.devices": ([UCS], r"public extern static WebCamDevice\[\] devices"),
 "WebCamTexture.Play/Stop": ([UCS], r"public void Play\(\)|public extern void Play\(\)"),
 "WebCamTexture.didUpdateThisFrame": ([UCS], r"public extern bool didUpdateThisFrame"),
 "WebCamTexture.videoVerticallyMirrored": ([UCS], r"public extern bool videoVerticallyMirrored"),
 "WebCamTexture.videoRotationAngle": ([UCS], r"public extern int videoRotationAngle"),
 "WebCamDevice.name": ([UCS], r"public string name \{ get \{ return m_Name; \} \}"),
 "Canvas.renderMode/sortingOrder/pixelPerfect": ([UCS], r"public extern int sortingOrder"),
 "RenderMode.ScreenSpaceOverlay": ([UCS], r"ScreenSpaceOverlay = 0"),
 "Camera.clearFlags/backgroundColor/orthographic": ([UCS], r"public CameraClearFlags clearFlags"),
 "AudioListener": ([UCS], r"public sealed partial class AudioListener|public sealed class AudioListener|class AudioListener"),
 "MinAttribute(float)": ([UCS], r"public MinAttribute\(float min\)"),
 "RangeAttribute(float,float)": ([UCS], r"public RangeAttribute\(float min, float max\)"),
 "DefaultExecutionOrder(int)": ([UCS], r"public DefaultExecutionOrder\(int order\)"),
 "DisallowMultipleComponent": ([UCS], r"class DisallowMultipleComponent"),
 "AddComponentMenu(string)": ([UCS], r"public AddComponentMenu\(string menuName\)"),
 "CreateAssetMenu menuName/fileName": ([UCS], r"public string fileName \{ get; set; \}"),
 "Scene.path / GetRootGameObjects": ([UCS], r"public GameObject\[\] GetRootGameObjects\(\)"),
 "SceneManager.GetActiveScene": ([UCS], r"public static extern Scene GetActiveScene\(\)"),
 # editor
 "MenuItem(string) + priority": ([UCS], r"public MenuItem\(string itemName\)"),
 "EditorApplication.delayCall": ([UCS], r"public static CallbackFunction delayCall"),
 "EditorApplication.isPlayingOrWillChangePlaymode": ([UCS], r"isPlayingOrWillChangePlaymode"),
 "SessionState.GetBool/SetBool": ([UCS], r"public static extern bool GetBool\(string key, bool defaultValue\)"),
 "EditorUtility.DisplayDialog(t,m,ok,cancel)": ([UCS], r"public static bool DisplayDialog\(string title, string message, string ok, \[DefaultValue\(\"\\\"\\\"\"\)\] string cancel\)|public static bool DisplayDialog\(string title, string message, string ok, string cancel\)"),
 "AssetDatabase.LoadAssetAtPath<T>": ([UCS], r"public static T LoadAssetAtPath<T>\(string assetPath\)"),
 "AssetDatabase.CreateAsset": ([UCS], r"public static void CreateAsset\(\[NotNull\] Object asset, string path\)|public static void CreateAsset\(Object asset, string path\)"),
 "AssetDatabase.IsValidFolder": ([UCS], r"public extern static bool IsValidFolder\(string path\)|static bool IsValidFolder\(string path\)"),
 "AssetDatabase.CreateFolder": ([UCS], r"static string CreateFolder\(string parentFolder, string newFolderName\)"),
 "Selection.activeGameObject": ([UCS], r"public extern static GameObject activeGameObject"),
 "Undo.RegisterCreatedObjectUndo": ([UCS], r"public static void RegisterCreatedObjectUndo\(Object objectToUndo, string name\)|RegisterCreatedObjectUndo\(\[NotNull\] Object objectToUndo, string name\)"),
 "Undo.AddComponent<T>(GameObject)": ([UCS], r"public static T AddComponent<T>\(GameObject gameObject\) where T : Component"),
 "SerializedObject(Object) / FindProperty / GetIterator / ApplyModifiedPropertiesWithoutUndo": ([UCS], r"public extern bool ApplyModifiedPropertiesWithoutUndo\(\)"),
 "SerializedProperty.NextVisible/objectReferenceValue/propertyType": ([UCS], r"public extern bool NextVisible\(bool enterChildren\)|public bool NextVisible\(bool enterChildren\)"),
 "Camera.backgroundColor (instance)": ([UCS], r"extern public Color backgroundColor"),
 "Camera.orthographic": ([UCS], r"public bool orthographic"),
 "SerializedObject(Object)": ([UCS], r"public SerializedObject\(Object obj\)"),
 "SessionState.SetBool": ([UCS], r"public static extern void SetBool\(string key, bool value\)"),
 "EditorBuildSettingsScene(string,bool)": ([UCS], r"public EditorBuildSettingsScene\(string path, bool enabled\)"),
 "EditorBuildSettings.scenes": ([UCS], r"public static EditorBuildSettingsScene\[\] scenes"),
 "PlayerSettings.runInBackground": ([UCS], r"public static extern bool runInBackground"),
 "PlayerSettings.productName": ([UCS], r"public static extern string productName|productName"),
 "PlayerSettings.macOS.cameraUsageDescription": ([UCS], r"public extern static string cameraUsageDescription"),
 "EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo": ([UCS], r"public static bool SaveCurrentModifiedScenesIfUserWantsTo\(\)"),
 "EditorSceneManager.OpenScene(string, OpenSceneMode)": ([UCS], r"public static extern Scene OpenScene\(string scenePath, \[uei.DefaultValue\(\"OpenSceneMode.Single\"\)\] OpenSceneMode mode\)|Scene OpenScene\(string scenePath, .*OpenSceneMode mode"),
 "EditorSceneManager.NewScene(setup, mode)": ([UCS], r"Scene NewScene\(NewSceneSetup setup, .*NewSceneMode mode"),
 "EditorSceneManager.SaveScene(Scene, string)": ([UCS], r"bool SaveScene\(Scene scene, .*string dstScenePath"),
 "EditorSceneManager.MarkSceneDirty": ([UCS], r"bool MarkSceneDirty\(Scene scene\)"),
 "NewSceneSetup.EmptyScene": ([UCS], r"EmptyScene"),
 # Milestone 3/4
 "Shader.Find(string)": ([UCS], r"public static Shader Find\(string name\)"),
 "Shader.isSupported": ([UCS], r"extern public bool isSupported"),
 "Shader.PropertyToID (thread-safe)": ([UCS], r"IsThreadSafe = true\)\] extern public static int PropertyToID\(string name\)"),
 "Material(Shader) / Material(Material)": ([UCS], r"public Material\(Material source\)"),
 "Material.shader": ([UCS], r"extern public Shader shader|public extern Shader shader"),
 "Material.SetFloat(int,float)": ([UCS], r"public void SetFloat\(int nameID, float value\)"),
 "Material.SetVector(int,Vector4)": ([UCS], r"public void SetVector\(int nameID, Vector4 value\)"),
 "Material.SetVectorArray(int,Vector4[])": ([UCS], r"public void SetVectorArray\(int nameID, Vector4\[\] values\)"),
 "Canvas.scaleFactor": ([UCS], r"public extern float scaleFactor"),
 "Canvas.rootCanvas": ([UCS], r"public extern Canvas rootCanvas"),
 "Component.GetComponentInParent<T>()": ([UCS], r"public T GetComponentInParent<T>\(\)"),
 "HideInInspector": ([UCS], r"public sealed partial class HideInInspector|public sealed class HideInInspector|class HideInInspector"),
 "KeyCode.Alpha0/1/2, Keypad0/1/2, LeftBracket, RightBracket": ([UCS], r"RightBracket\s*= 93"),
 "KeyCode.Keypad2": ([UCS], r"Keypad2\s*= 258"),
 "Mathf.Clamp01": ([UCS], r"public static float Clamp01\(float value\)"),
 "EditorUtility.SetDirty": ([UCS], r"public static void SetDirty\(\[NotNull\] Object target\)|public static void SetDirty\(Object target\)|static extern void SetDirty\("),
}
ugui = {
 "Graphic.material (virtual get/set)": r"public virtual Material material",
 "Button.onClick / ButtonClickedEvent": r"public ButtonClickedEvent onClick",
 "Selectable.colors": r"public ColorBlock\s+colors",
 "Selectable.targetGraphic": r"public Graphic\s+targetGraphic",
 "ColorBlock.normalColor/highlightedColor/pressedColor": r"public Color pressedColor",
 "Slider.fillRect/handleRect": r"public RectTransform fillRect",
 "Slider.minValue/maxValue/wholeNumbers": r"public bool wholeNumbers",
 "Slider.onValueChanged (SliderEvent)": r"public SliderEvent onValueChanged",
 "DefaultControls.CreateSlider(Resources)": r"public static GameObject CreateSlider\(Resources resources\)",
 "DefaultControls.Resources": r"public struct Resources",

 "VertexHelper.AddVert(Vector3, Color32, uv0)": r"public void AddVert\(Vector3 position, Color32 color, Vector[24] uv0\)",
 "VertexHelper.AddTriangle": r"public void AddTriangle\(int idx0, int idx1, int idx2\)",
 "VertexHelper.currentVertCount": r"public int currentVertCount",
 "VertexHelper.Clear": r"public void Clear\(\)",
 "Graphic.OnPopulateMesh(VertexHelper)": r"protected virtual void OnPopulateMesh\(VertexHelper vh\)",
 "Graphic.SetVerticesDirty": r"public virtual void SetVerticesDirty\(\)",
 "Graphic.raycastTarget": r"public virtual bool raycastTarget",
 "Graphic.rectTransform": r"public RectTransform rectTransform",
 "UIBehaviour.Awake virtual": r"protected virtual void Awake\(\)",
 "RawImage.uvRect": r"public Rect uvRect",
 "RawImage.texture": r"public Texture texture",
 "Text.horizontalOverflow": r"public HorizontalWrapMode horizontalOverflow",
 "Text.supportRichText": r"public bool supportRichText",
 "Text.lineSpacing": r"public float lineSpacing",
 "Shadow.effectDistance": r"public Vector2 effectDistance",
 "Outline : Shadow": r"public class Outline : Shadow",
 "AspectRatioFitter.AspectMode.EnvelopeParent": r"EnvelopeParent",
 "AspectRatioFitter.aspectRatio": r"public float aspectRatio",
 "CanvasScaler.matchWidthOrHeight": r"public float matchWidthOrHeight",
 "CanvasScaler.ScreenMatchMode.MatchWidthOrHeight": r"MatchWidthOrHeight",
 "StandaloneInputModule": r"public class StandaloneInputModule",
 "EventSystem": r"public class EventSystem",
}
bad=0
for k,(roots,pat) in checks.items():
    hits=[f for r in roots for f in g(r,pat)]
    ok=bool(hits); bad+= not ok
    print(("OK  " if ok else "MISS"), k)
for k,pat in ugui.items():
    a=bool(g(U7,pat)); b=bool(g(U18,pat)); ok=a and b; bad+= not ok
    print(("OK  " if ok else "MISS"), "[uGUI 2018.4 %s / 7.0 %s]"%("y" if b else "n","y" if a else "n"), k)
# Added after uGUI 2018.4 (2019.1+), so present in uGUI 1.0.0 shipped with 2022.3: check the 7.0 branch only.
newer = {
 "Slider.SetValueWithoutNotify (2019.1+)": r"public virtual void SetValueWithoutNotify\(float input\)",
 "ColorBlock.selectedColor (2019.1+)": r"public Color selectedColor",
}
for k,pat in newer.items():
    ok=bool(g(U7,pat)); bad+= not ok
    print(("OK  " if ok else "MISS"), "[uGUI 7.0, added 2019.1]", k)
# UnityEvent / UnityEvent<T0> are code-generated in the engine build and not published in UnityCsReference.
# Evidence instead: uGUI itself (both branches) calls AddListener on UnityEvent<T> with a lambda.
for k,pat in {"UnityEvent<T>.AddListener(UnityAction<T>) used by uGUI": r"\.AddListener\(",}.items():
    ok=bool(g(U7,pat)) and bool(g(U18,pat)); bad+= not ok
    print(("OK  " if ok else "MISS"), k)
print("missing:",bad)
