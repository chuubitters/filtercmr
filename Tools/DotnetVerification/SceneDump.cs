// `dotnet run -- scene` : reads a scene description (JSON, stdin) and prints the composite shader
// uniforms produced by the REAL mask generators and CompositeUniformWriter. Used by
// Tools/ShaderPreview to drive the real shader outside Unity.
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using FilterCMR.Mask;
using FilterCMR.Rendering;

public static class SceneDump
{
    sealed class RecordingSink : IUniformSink
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public void SetFloat(FxUniform u, float v) => Values["_" + u] = v;
        public void SetVector(FxUniform u, Vector4 v) => Values["_" + u] = new[] { v.X, v.Y, v.Z, v.W };
        public void SetRibbonPoints(Vector4[] p)
        {
            var a = new float[p.Length][];
            for (int i = 0; i < p.Length; i++) a[i] = new[] { p[i].X, p[i].Y, p[i].Z, p[i].W };
            Values[CompositeUniformWriter.RibbonPointsName] = a;
        }
    }

    static Vector2 V(JsonElement e) => new Vector2(e[0].GetSingle(), e[1].GetSingle());

    public static int Run()
    {
        var doc = JsonDocument.Parse(Console.In.ReadToEnd()).RootElement;
        float aspect = doc.GetProperty("aspect").GetSingle();
        float heightPx = doc.GetProperty("imageHeightPx").GetSingle();
        float time = doc.TryGetProperty("time", out var te) ? te.GetSingle() : 0f;
        var uv = doc.TryGetProperty("uvRect", out var ue) ? new Vector4(ue[0].GetSingle(), ue[1].GetSingle(), ue[2].GetSingle(), ue[3].GetSingle()) : new Vector4(0, 0, 1, 1);

        EffectValues e = EffectValues.Default;
        if (doc.TryGetProperty("palette", out var pal)) EffectValues.ApplyPalette(ref e, Enum.Parse<PalettePreset>(pal.GetString()));
        if (doc.TryGetProperty("effects", out var fx))
        {
            object boxed = e;
            foreach (var prop in fx.EnumerateObject())
            {
                var f = typeof(EffectValues).GetField(prop.Name) ?? throw new ArgumentException("unknown effect field " + prop.Name);
                object val = f.FieldType == typeof(bool) ? prop.Value.GetBoolean()
                           : f.FieldType == typeof(float) ? prop.Value.GetSingle()
                           : f.FieldType.IsEnum ? Enum.Parse(f.FieldType, prop.Value.GetString())
                           : throw new ArgumentException("unsupported field type " + f.FieldType);
                f.SetValue(boxed, val);
            }
            e = (EffectValues)boxed;
        }

        var ribbon = new RibbonShape();
        var quad = new QuadShape();
        float rw = 0, qw = 0;
        if (doc.TryGetProperty("ribbon", out var r))
        {
            Vector2 a = MaskCoordinateMapper.ImageToIso(V(r.GetProperty("a")), aspect);
            Vector2 b = MaskCoordinateMapper.ImageToIso(V(r.GetProperty("b")), aspect);
            Vector2 da = r.TryGetProperty("dirA", out var x) ? Vector2.Normalize(MaskCoordinateMapper.ImageToIso(V(x), aspect)) : Vector2.Zero;
            Vector2 db = r.TryGetProperty("dirB", out var y) ? Vector2.Normalize(MaskCoordinateMapper.ImageToIso(V(y), aspect)) : Vector2.Zero;
            RibbonMaskGenerator.Generate(a, b, da, db, e.ribbon, time, ribbon);
            rw = r.TryGetProperty("weight", out var w) ? w.GetSingle() : 1f;
        }
        if (doc.TryGetProperty("quad", out var q))
        {
            Vector2 I(string n) => MaskCoordinateMapper.ImageToIso(V(q.GetProperty(n)), aspect);
            QuadrilateralMaskGenerator.Generate(I("leftIndex"), I("leftMiddle"), I("rightIndex"), I("rightMiddle"), quad);
            qw = q.TryGetProperty("weight", out var w) ? w.GetSingle() : 1f;
        }

        var sink = new RecordingSink();
        var writer = new CompositeUniformWriter();
        var frame = new FrameMapping { uvRect = uv, aspect = aspect, imageHeightPx = heightPx, time = time, grainSeed = CompositeUniformWriter.GrainSeed(e, time) };
        writer.WriteFrame(sink, frame);
        writer.WriteMasks(sink, ribbon, rw, quad, qw, e, 1f / heightPx);
        writer.WriteEffects(sink, e);
        sink.Values["$quadValid"] = quad.Valid;
        sink.Values["$quadVertexCount"] = quad.VertexCount;
        Console.WriteLine(JsonSerializer.Serialize(sink.Values));
        return 0;
    }
}
