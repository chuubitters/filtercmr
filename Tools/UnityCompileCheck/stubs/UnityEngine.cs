// Compile-only stubs. Every member mirrors a signature verified in UnityCsReference 2022.3.62f1
// or in uGUI (both 2018.4 and 7.0.0 branches). Bodies are irrelevant.
#pragma warning disable
using System;
using System.Collections;
namespace UnityEngine {
  public class Object {
    public string name { get; set; }
    public HideFlags hideFlags { get; set; }
    public static void Destroy(Object obj) {}
    public static T FindObjectOfType<T>() where T : Object => null;
    public static T FindObjectOfType<T>(bool includeInactive) where T : Object => null;
    public static T[] FindObjectsOfType<T>() where T : Object => null;
    public static implicit operator bool(Object o) => o != null;
  }
  [Flags] public enum HideFlags { None = 0, DontSave = 52 }
  public class Component : Object {
    public GameObject gameObject => null; public Transform transform => null;
    public T GetComponent<T>() => default;
    public T GetComponentInParent<T>() => default;
    public string tag { get; set; }
  }
  public class Behaviour : Component { public bool enabled { get; set; } public bool isActiveAndEnabled => true; }
  public class MonoBehaviour : Behaviour {
    public Coroutine StartCoroutine(IEnumerator routine) => null;
    public void StopCoroutine(Coroutine routine) {}
  }
  public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject => null; }
  public sealed class Coroutine {}
  public class YieldInstruction {}
  public class AsyncOperation : YieldInstruction {}
  public class GameObject : Object {
    public GameObject() {} public GameObject(string name) {} public GameObject(string name, params Type[] components) {}
    public Transform transform => null; public int layer { get; set; } public string tag { get; set; }
    public bool activeSelf => true; public void SetActive(bool value) {}
    public T GetComponent<T>() => default; public T AddComponent<T>() where T : Component => null;
  }
  public class Transform : Component, IEnumerable {
    public Transform Find(string n) => null; public void SetParent(Transform p, bool worldPositionStays) {}
    public Vector3 localEulerAngles { get; set; }
    public void SetAsFirstSibling() {} public void SetAsLastSibling() {}
    public IEnumerator GetEnumerator() => null;
  }
  public sealed class RectTransform : Transform {
    public Rect rect => default; public Vector2 anchorMin { get; set; } public Vector2 anchorMax { get; set; }
    public Vector2 pivot { get; set; } public Vector2 anchoredPosition { get; set; } public Vector2 sizeDelta { get; set; }
    public Vector2 offsetMin { get; set; } public Vector2 offsetMax { get; set; }
  }
  public struct Vector2 {
    public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; }
    public static Vector2 zero => default; public static Vector2 one => default;
    public float magnitude => 0;
    public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t) => a;
    public static Vector2 operator +(Vector2 a, Vector2 b) => a; public static Vector2 operator -(Vector2 a, Vector2 b) => a;
    public static Vector2 operator -(Vector2 a) => a;
    public static Vector2 operator *(Vector2 a, float d) => a; public static Vector2 operator *(float d, Vector2 a) => a;
    public static implicit operator Vector3(Vector2 v) => default; public static implicit operator Vector2(Vector3 v) => default;
    public static implicit operator Vector4(Vector2 v) => default;
  }
  public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
  public struct Vector4 { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; } }
  public struct Rect : IEquatable<Rect> {
    public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    public float x { get; set; } public float y { get; set; } public float width { get; set; } public float height { get; set; }
    public float xMin => x; public float yMax => y + height;
    public static bool operator ==(Rect a, Rect b) => true; public static bool operator !=(Rect a, Rect b) => false;
    public bool Equals(Rect o) => true; public override bool Equals(object o) => true; public override int GetHashCode() => 0;
  }
  public struct Color {
    public float r, g, b, a; public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    public static Color white => default; public static Color clear => default;
    public static bool operator ==(Color a, Color b) => true; public static bool operator !=(Color a, Color b) => false;
    public override bool Equals(object o) => true; public override int GetHashCode() => 0;
    public static implicit operator Color32(Color c) => default; public static implicit operator Color(Color32 c) => default;
  }
  public struct Color32 { public byte r, g, b, a; public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; } }
  public static class ColorUtility { public static string ToHtmlStringRGB(Color color) => ""; }
  public struct Mathf {
    public static float Clamp01(float value) => 0; public static float Max(float a, float b) => 0;
    public const float PI = 3.14159274F;
    public static float Exp(float p) => 0; public static float Abs(float f) => 0; public static float Cos(float f) => 0; public static float Sin(float f) => 0;
    public static int Max(int a, int b) => 0; public static float Repeat(float t, float length) => 0;
    public static bool Approximately(float a, float b) => true; public static int RoundToInt(float f) => 0;
  }
  public static class Debug {
    public static void Log(object m) {} public static void LogWarning(object m) {} public static void LogError(object m) {}
    public static void LogError(object m, Object context) {}
  }
  public static class Time { public static float unscaledTime => 0; public static float unscaledDeltaTime => 0; }
  public static class Input { public static bool GetKeyDown(KeyCode key) => false; }
  public enum KeyCode { Alpha0 = 48, Alpha1 = 49, Alpha2 = 50, LeftBracket = 91, RightBracket = 93, C = 99, D = 100, G = 103, H = 104, M = 109, P = 112, R = 114, S = 115, T = 116, V = 118, X = 120, Keypad0 = 256, Keypad1 = 257, Keypad2 = 258 }
  public enum UserAuthorization { WebCam = 1, Microphone = 2 }
  public static class Application {
    public static bool runInBackground { get; set; } public static bool isPlaying => true; public static bool isBatchMode => false;
    public static bool HasUserAuthorization(UserAuthorization mode) => true;
    public static AsyncOperation RequestUserAuthorization(UserAuthorization mode) => null;
  }
  public static class Resources { public static T GetBuiltinResource<T>(string path) where T : Object => null; }
  public class Texture : Object {
    public virtual int width { get; set; } public virtual int height { get; set; }
    public TextureWrapMode wrapMode { get; set; } public FilterMode filterMode { get; set; }
  }
  public enum TextureWrapMode { Repeat, Clamp }
  public enum FilterMode { Point, Bilinear, Trilinear }
  public enum TextureFormat { RGB24 = 3 }
  public sealed class Texture2D : Texture { public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) {} }
  public static class ImageConversion { public static bool LoadImage(this Texture2D tex, byte[] data, bool markNonReadable) => true; }
  public struct WebCamDevice { public string name => ""; }
  public sealed class WebCamTexture : Texture {
    public WebCamTexture(string deviceName, int requestedWidth, int requestedHeight, int requestedFPS) {}
    public static WebCamDevice[] devices => null;
    public void Play() {} public void Stop() {} public bool isPlaying => true;
    public int videoRotationAngle => 0; public bool videoVerticallyMirrored => false; public bool didUpdateThisFrame => true;
  }
  public sealed class Font : Object {}
  public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
  public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
  public enum HorizontalWrapMode { Wrap, Overflow }
  public enum VerticalWrapMode { Truncate, Overflow }
  public sealed class CanvasRenderer : Component {}
  public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
  public sealed class Canvas : Behaviour { public RenderMode renderMode { get; set; } public int sortingOrder { get; set; } public bool pixelPerfect { get; set; } public float scaleFactor { get; set; } public Canvas rootCanvas => null; }
  public class Camera : Behaviour { public CameraClearFlags clearFlags { get; set; } public Color backgroundColor { get; set; } public bool orthographic { get; set; } }
  public enum CameraClearFlags { Skybox = 1, SolidColor = 2 }
  public sealed class AudioListener : Behaviour {}
  public class PropertyAttribute : Attribute {}
  [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute {}
  public sealed class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string tooltip) {} }
  public sealed class HeaderAttribute : PropertyAttribute { public HeaderAttribute(string header) {} }
  public sealed class MinAttribute : PropertyAttribute { public MinAttribute(float min) {} }
  public sealed class RangeAttribute : PropertyAttribute { public RangeAttribute(float min, float max) {} }
  public sealed class DisallowMultipleComponent : Attribute {}
  public sealed class HideInInspector : Attribute {}
  [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public sealed class RequireComponent : Attribute { public RequireComponent(Type t) {} }
  public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) {} }
  public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string menuName) {} }
  public sealed class CreateAssetMenuAttribute : Attribute { public string menuName { get; set; } public string fileName { get; set; } }
}
namespace UnityEngine.SceneManagement {
  public struct Scene { public string path => ""; public GameObject[] GetRootGameObjects() => null; }
  public class SceneManager { public static Scene GetActiveScene() => default; }
}
namespace UnityEngine.EventSystems {
  public abstract class UIBehaviour : MonoBehaviour { protected virtual void Awake() {} protected virtual void OnEnable() {} }
  public class EventSystem : UIBehaviour {}
  public abstract class BaseInputModule : UIBehaviour {}
  public class StandaloneInputModule : BaseInputModule {}
}
namespace UnityEngine.UI {
  using UnityEngine.EventSystems;
  public class VertexHelper {
    public int currentVertCount => 0; public void Clear() {}
    public void AddVert(Vector3 position, Color32 color, Vector4 uv0) {} // 7.0.0; 2018.4 has Vector2 — Vector2 arg converts to both
    public void AddTriangle(int a, int b, int c) {}
  }
  public abstract class Graphic : UIBehaviour {
    public virtual Color color { get; set; } public virtual bool raycastTarget { get; set; }
    public RectTransform rectTransform => null; public virtual void SetVerticesDirty() {}
    public virtual Material material { get; set; }
    protected virtual void OnPopulateMesh(VertexHelper vh) {}
  }
  public abstract class MaskableGraphic : Graphic {}
  public class Image : MaskableGraphic {}
  public class RawImage : MaskableGraphic { public Texture texture { get; set; } public Rect uvRect { get; set; } }
  public class Text : MaskableGraphic {
    public string text { get; set; } public Font font { get; set; } public int fontSize { get; set; } public FontStyle fontStyle { get; set; }
    public TextAnchor alignment { get; set; } public bool supportRichText { get; set; } public float lineSpacing { get; set; }
    public HorizontalWrapMode horizontalOverflow { get; set; } public VerticalWrapMode verticalOverflow { get; set; }
  }
  public abstract class BaseMeshEffect : UIBehaviour {}
  public class Shadow : BaseMeshEffect { public Color effectColor { get; set; } public Vector2 effectDistance { get; set; } }
  public class Outline : Shadow {}
  public class AspectRatioFitter : UIBehaviour {
    public enum AspectMode { None, WidthControlsHeight, HeightControlsWidth, FitInParent, EnvelopeParent }
    public AspectMode aspectMode { get; set; } public float aspectRatio { get; set; }
  }
  public class CanvasScaler : UIBehaviour {
    public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
    public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
    public ScaleMode uiScaleMode { get; set; } public Vector2 referenceResolution { get; set; }
    public ScreenMatchMode screenMatchMode { get; set; } public float matchWidthOrHeight { get; set; }
  }
  public class GraphicRaycaster : MonoBehaviour {}
}

namespace UnityEngine {
  public sealed class Shader : Object {
    public static Shader Find(string name) => null; public bool isSupported => true;
    public static int PropertyToID(string name) => 0;
  }
  public class Material : Object {
    public Material(Shader shader) {} public Material(Material source) {}
    public Shader shader { get; set; }
    public void SetFloat(int nameID, float value) {} public void SetVector(int nameID, Vector4 value) {}
    public void SetVectorArray(int nameID, Vector4[] values) {}
  }
}
namespace UnityEngine.Events {
  public delegate void UnityAction(); public delegate void UnityAction<T0>(T0 arg0);
  public abstract class UnityEventBase {}
  public class UnityEvent : UnityEventBase { public void AddListener(UnityAction call) {} }
  public abstract class UnityEvent<T0> : UnityEventBase { public void AddListener(UnityAction<T0> call) {} }
}
namespace UnityEngine.UI {
  using UnityEngine.EventSystems;
  public struct ColorBlock { public Color normalColor { get; set; } public Color highlightedColor { get; set; } public Color pressedColor { get; set; } public Color selectedColor { get; set; } }
  public class Selectable : UIBehaviour { public Graphic targetGraphic { get; set; } public ColorBlock colors { get; set; } }
  public class Button : Selectable { public class ButtonClickedEvent : UnityEngine.Events.UnityEvent {} public ButtonClickedEvent onClick { get; set; } }
  public class Slider : Selectable {
    public class SliderEvent : UnityEngine.Events.UnityEvent<float> {}
    public RectTransform fillRect { get; set; } public RectTransform handleRect { get; set; }
    public float minValue { get; set; } public float maxValue { get; set; } public bool wholeNumbers { get; set; }
    public SliderEvent onValueChanged { get; set; } public virtual void SetValueWithoutNotify(float input) {}
  }
  public static class DefaultControls {
    public struct Resources { public Sprite standard; public Sprite background; public Sprite knob; }
    public static GameObject CreateSlider(Resources resources) => null;
  }
}
namespace UnityEngine { public sealed class Sprite : Object {} }
