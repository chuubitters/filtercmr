"""Compile and RUN the FILTER CMR composite shader outside Unity, then check the results.

Pipeline (development tool, not needed to use the project):
  1. Extract the CGPROGRAM block of FILTERCMR_WebcamComposite.shader.
  2. Compile vertex + fragment with glslang's HLSL front end against Unity's built-in
     CGIncludes (UnityCG.cginc / UnityUI.cginc), for each keyword variant.
  3. Translate SPIR-V -> GLSL 4.5 with spirv-cross.
  4. Render on a software GPU (Mesa llvmpipe via moderngl) with uniforms produced by the
     REAL C# mask generators + CompositeUniformWriter (Tools/DotnetVerification `scene` mode).
  5. Assert the acceptance criteria numerically and write preview PNGs.

Requirements: glslangValidator, spirv-cross, Mesa EGL, python: moderngl numpy opencv scikit-image,
.NET 8 SDK, Unity built-in CGIncludes (e.g. github.com/TwoTailsGames/Unity-Built-in-Shaders).

    python3 Tools/ShaderPreview/shader_check.py --cgincludes /tmp/ubs/CGIncludes --out /tmp/preview
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SHADER = ROOT / "UnityProject/Assets/FILTERCMR/Shaders/FILTERCMR_WebcamComposite.shader"
SHADER_DIR = SHADER.parent
PRELUDE = Path(__file__).with_name("UnityPrelude.hlsl")
DOTNET = ROOT / "Tools/DotnetVerification"

W, H = 1280, 720
ASPECT = W / H
RESULTS: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> None:
    RESULTS.append((name, bool(ok), detail))
    print(f"[{'PASS' if ok else 'FAIL'}] {name}  {detail}")


# ------------------------------------------------------------------------------ compile
def compile_variant(cgincludes: Path, defines: list[str], work: Path) -> tuple[str, str]:
    src = SHADER.read_text()
    body = re.search(r"CGPROGRAM(.*?)ENDCG", src, re.S).group(1)
    body = re.sub(r"#pragma[^\n]*", "", body)
    tag = "_".join(defines) or "base"
    hlsl = work / f"prog_{tag}.hlsl"
    hlsl.write_text("".join(f"#define {d} 1\n" for d in defines) + PRELUDE.read_text() + body)
    out = {}
    for stage in ("vert", "frag"):
        spv = work / f"{stage}_{tag}.spv"
        r = subprocess.run(["glslangValidator", "-D", "-S", stage, "-e", stage, "--auto-map-locations",
                            "--auto-map-bindings", f"-I{cgincludes}", f"-I{SHADER_DIR}", "-V", "-o", str(spv), str(hlsl)],
                           capture_output=True, text=True)
        if r.returncode != 0:
            raise RuntimeError(f"{stage} [{tag}] failed:\n{r.stdout}\n{r.stderr}")
        r = subprocess.run(["spirv-cross", str(spv), "--version", "450", "--no-es",
                            "--glsl-emit-ubo-as-plain-uniforms"], capture_output=True, text=True)
        if r.returncode != 0:
            raise RuntimeError(f"spirv-cross {stage} [{tag}] failed: {r.stderr}")
        out[stage] = r.stdout
    return out["vert"], out["frag"]


# ------------------------------------------------------------------------------ scene
def scene_uniforms(scene: dict) -> dict:
    r = subprocess.run(["dotnet", "run", "--no-build", "--", "scene"], cwd=DOTNET, input=json.dumps(scene),
                       capture_output=True, text=True)
    if r.returncode != 0:
        raise RuntimeError(r.stdout + r.stderr)
    return json.loads(r.stdout.strip().splitlines()[-1])


def test_image() -> np.ndarray:
    """1280x720 webcam-like frame (RGB, top row first) built around a public-domain NASA portrait."""
    from skimage import data
    face = cv2.resize(data.astronaut(), (720, 720), interpolation=cv2.INTER_CUBIC)
    bg = cv2.resize(face, (W, H), interpolation=cv2.INTER_AREA)
    bg = cv2.GaussianBlur(bg, (0, 0), 25)
    img = bg.copy()
    img[:, (W - 720) // 2:(W + 720) // 2] = face
    # A dark and a bright patch for tone-response checks, plus a sharp edge.
    img[40:200, 40:300] = (18, 22, 30)
    img[40:200, W - 300:W - 40] = (235, 240, 245)
    return img


# ------------------------------------------------------------------------------ renderer
class Renderer:
    def __init__(self, vert: str, frag: str, linear: bool):
        import moderngl
        self.ctx = moderngl.create_standalone_context(backend="egl")
        self.prog = self.ctx.program(vertex_shader=vert, fragment_shader=frag)
        self.linear = linear
        self.fbo = None
        self.tex = None

    def _set(self, name: str, value) -> bool:
        hit = False
        for key in self.prog:
            base = key.split("[")[0]
            if base.endswith("." + name) or base == name:
                u = self.prog[key]
                try:
                    if isinstance(value, (list, tuple)) and value and isinstance(value[0], (list, tuple)):
                        u.write(np.asarray(value, np.float32).tobytes())
                    else:
                        u.value = value
                    hit = True
                except Exception as exc:  # pragma: no cover - diagnostic
                    raise RuntimeError(f"cannot set {key}: {exc}")
        return hit

    def render(self, image_rgb: np.ndarray, uniforms: dict, uv_rect=(0, 0, 1, 1)) -> np.ndarray:
        ctx = self.ctx
        rgba = np.dstack([image_rgb, np.full(image_rgb.shape[:2], 255, np.uint8)])
        if self.tex is None:
            fmt = 0x8C43 if self.linear else None  # GL_SRGB8_ALPHA8: sample webcam as sRGB in a Linear project
            self.tex = ctx.texture((W, H), 4, **({"internal_format": fmt} if fmt else {}))
            self.tex.filter = (0x2601, 0x2601)     # GL_LINEAR, like a bilinear webcam texture
            color = ctx.texture((W, H), 4, internal_format=0x8C43) if self.linear else ctx.texture((W, H), 4)
            self.fbo = ctx.framebuffer(color_attachments=[color])
        self.tex.write(np.flipud(rgba).tobytes())  # upload THIS frame (GL rows are bottom-up)
        self.fbo.use()
        if self.linear:
            ctx.enable_direct(0x8DB9)  # GL_FRAMEBUFFER_SRGB: like Unity's sRGB back buffer in Linear space
        ctx.clear(0, 0, 0, 1)
        identity = tuple(np.eye(4, dtype=np.float32).flatten())
        for n in ("unity_ObjectToWorld", "unity_MatrixVP", "unity_MatrixV", "unity_WorldToObject"):
            self._set(n, identity)
        self._set("_ProjectionParams", (1.0, 0.3, 1000.0, 0.001))
        self._set("_ScreenParams", (float(W), float(H), 1 + 1.0 / W, 1 + 1.0 / H))
        self._set("_MainTex_ST", (1.0, 1.0, 0.0, 0.0))
        self._set("_Color", (1.0, 1.0, 1.0, 1.0))
        self._set("_TextureSampleAdd", (0.0, 0.0, 0.0, 0.0))
        self._set("_ClipRect", (-1e5, -1e5, 1e5, 1e5))
        missing = [k for k, v in uniforms.items() if not k.startswith("$") and not self._set(k, v)]
        if missing:
            raise RuntimeError(f"uniforms not found in program: {missing}")
        for key in self.prog:
            if "MainTex" in key and "ST" not in key and hasattr(self.prog[key], "value") and "sampler" in str(type(self.prog[key])).lower() or key.endswith("_MainTex"):
                try:
                    self.prog[key].value = 0
                except Exception:
                    pass
        self.tex.use(0)
        x, y, w, h = uv_rect
        # RawImage quad: corner (s,t) -> uv = (x + w*s, y + h*t); positions straight in clip space.
        verts = []
        for s, t in ((0, 0), (1, 0), (0, 1), (1, 1)):
            verts += [s * 2 - 1, t * 2 - 1, 0, 1, 1, 1, 1, 1, x + w * s, y + h * t]
        vbo = ctx.buffer(np.array(verts, np.float32).tobytes())
        vao = ctx.vertex_array(self.prog, [(vbo, "4f 4f 2f", "v_vertex", "v_color", "v_texcoord")])
        vao.render(mode=ctx.TRIANGLE_STRIP)
        out = np.frombuffer(self.fbo.read(components=3), np.uint8).reshape(H, W, 3)
        vao.release()
        vbo.release()
        return np.flipud(out).copy()


# ------------------------------------------------------------------------------ helpers
def ribbon_sdf_px(u: dict) -> np.ndarray:
    """Signed distance (screen pixels) to the uploaded ribbon, per pixel (same maths as the shader)."""
    pts = np.array(u["_RibbonPts"], np.float32)[: int(u["_RibbonCount"])]
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    P = np.stack([(xx + 0.5) / W * ASPECT, (yy + 0.5) / H], -1)
    d = np.full((H, W), 1e5, np.float32)
    for a, b in zip(pts[:-1], pts[1:]):
        ab = b[:2] - a[:2]
        h = np.clip(((P - a[:2]) @ ab) / max(ab @ ab, 1e-10), 0, 1)
        r = a[2] + (b[2] - a[2]) * h
        d = np.minimum(d, np.linalg.norm(P - a[:2] - h[..., None] * ab, axis=-1) - r)
    return d * H


def quad_sdf_px(u: dict) -> np.ndarray:
    v = np.array([u["_QuadV01"][:2], u["_QuadV01"][2:], u["_QuadV23"][:2], u["_QuadV23"][2:]], np.float32)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    P = np.stack([(xx + 0.5) / W * ASPECT, (yy + 0.5) / H], -1)
    d = np.sum((P - v[0]) ** 2, -1)
    s = np.ones((H, W), np.float32)
    j = 3
    for i in range(4):
        e = v[j] - v[i]
        w = P - v[i]
        ee = e @ e
        t = np.clip((w @ e) / ee, 0, 1) if ee > 1e-12 else np.zeros((H, W), np.float32)
        b = w - t[..., None] * e
        d = np.minimum(d, np.sum(b * b, -1))
        c1 = P[..., 1] >= v[i][1]
        c2 = P[..., 1] < v[j][1]
        c3 = e[0] * w[..., 1] > e[1] * w[..., 0]
        flip = (c1 & c2 & c3) | (~c1 & ~c2 & ~c3)
        s = np.where(flip, -s, s)
        j = i
    return s * np.sqrt(d) * H


def mirrored(img: np.ndarray) -> np.ndarray:
    return img[:, ::-1].copy()


def label(img: np.ndarray, text: str) -> np.ndarray:
    out = img.copy()
    cv2.rectangle(out, (0, 0), (W, 34), (7, 21, 47), -1)
    cv2.putText(out, text, (12, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (165, 243, 252), 1, cv2.LINE_AA)
    return out


# ------------------------------------------------------------------------------ main
def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--cgincludes", type=Path, required=True)
    ap.add_argument("--out", type=Path, default=Path("/tmp/filtercmr_preview"))
    args = ap.parse_args(argv)
    args.out.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix="fcmr_shader_"))

    subprocess.run(["dotnet", "build", "-nologo", "-v", "q"], cwd=DOTNET, check=True, capture_output=True)

    # 1. every keyword variant compiles
    variants = [[], ["UNITY_UI_CLIP_RECT"], ["UNITY_UI_ALPHACLIP"], ["UNITY_UI_CLIP_RECT", "UNITY_UI_ALPHACLIP"],
                ["UNITY_COLORSPACE_GAMMA"], ["UNITY_COLORSPACE_GAMMA", "UNITY_UI_CLIP_RECT"]]
    compiled = {}
    for v in variants:
        try:
            compiled[tuple(v)] = compile_variant(args.cgincludes, v, work)
            check(f"compile variant {v or ['base']}", True)
        except RuntimeError as exc:
            check(f"compile variant {v or ['base']}", False, str(exc)[:400])
    if not all(ok for _, ok, _ in RESULTS):
        return 1

    img = test_image()
    cv2.imwrite(str(args.out / "00_input.png"), cv2.cvtColor(img, cv2.COLOR_RGB2BGR))
    base = {"aspect": ASPECT, "imageHeightPx": H, "time": 1.25}
    ribbon = {"a": [0.30, 0.62], "b": [0.71, 0.50], "dirA": [0.1, -1], "dirB": [-0.15, -1]}
    quad = {"leftIndex": [0.30, 0.30], "leftMiddle": [0.27, 0.72], "rightIndex": [0.70, 0.24], "rightMiddle": [0.74, 0.70]}

    for space, defines in (("gamma", ("UNITY_COLORSPACE_GAMMA",)), ("linear", ())):
        rend = Renderer(*compiled[defines], linear=(space == "linear"))
        tol = 0 if space == "gamma" else 1

        # 2. no gesture: identical to the webcam
        out = rend.render(img, scene_uniforms(base))
        diff = np.abs(out.astype(int) - img.astype(int))
        check(f"[{space}] no mask: output == webcam", diff.max() <= tol, f"max diff {diff.max()}")

        # 3. ribbon
        u = scene_uniforms({**base, "ribbon": ribbon})
        out = rend.render(img, u)
        d = ribbon_sdf_px(u)
        margin_px = 0.5 * 0.035 * H + 4 * 0.02 * H + 0.3 * 0.035 * H + 6
        outside = d > margin_px
        inside = d < -0.6 * 0.035 * H
        diff = np.abs(out.astype(int) - img.astype(int)).max(-1)
        check(f"[{space}] ribbon: outside untouched", diff[outside].max() <= tol, f"{outside.mean()*100:.0f}% of pixels, max diff {diff[outside].max()}")
        check(f"[{space}] ribbon: inside transformed", (diff[inside] > 30).mean() > 0.6, f"{(diff[inside] > 30).mean()*100:.0f}% of inner pixels changed >30/255")
        ins = out[inside].astype(float)
        check(f"[{space}] ribbon: blue/cyan treatment", (ins[:, 2] > ins[:, 0] + 20).mean() > 0.7, f"blue>red on {(ins[:, 2] > ins[:, 0] + 20).mean()*100:.0f}% of inner pixels")
        if space == "gamma":
            cv2.imwrite(str(args.out / "01_ribbon.png"), cv2.cvtColor(label(out, "TWO FINGER POINT - ribbon (default effects)"), cv2.COLOR_RGB2BGR))
            # feathered edge: effect strength rises smoothly across the boundary
            band = [np.abs(out.astype(int) - img.astype(int)).sum(-1)[(d > lo) & (d <= lo + 4)].mean()
                    for lo in range(-24, 24, 4)]
            # No hard step: between adjacent 4 px bands the effect changes by < 45% of its inner level,
            # and the fall-off from 80% to 10% of that level spans at least 8 px (feather 0.035 = 25 px).
            inner = max(band[:3])
            steps_ok = all(abs(b2 - b1) < 0.45 * inner for b1, b2 in zip(band, band[1:]))
            idx80 = next(i for i, b in enumerate(band) if i > 2 and b < 0.8 * inner)
            idx10 = next(i for i, b in enumerate(band) if b < 0.1 * inner)
            check("[gamma] ribbon: soft feathered edge (no hard step)", steps_ok and (idx10 - idx80) * 4 >= 8,
                  "effect by 4px band from 24px inside to 24px outside: " + " ".join(f"{b:.0f}" for b in band))

        # 4. quad
        u = scene_uniforms({**base, "quad": quad})
        out = rend.render(img, u)
        d = quad_sdf_px(u)
        outside = d > margin_px
        inside = d < -0.6 * 0.035 * H
        diff = np.abs(out.astype(int) - img.astype(int)).max(-1)
        check(f"[{space}] quad: valid 4-vertex polygon", u["$quadValid"] and u["$quadVertexCount"] == 4)
        check(f"[{space}] quad: outside untouched", diff[outside].max() <= tol, f"max diff {diff[outside].max()}")
        check(f"[{space}] quad: inside transformed", (diff[inside] > 30).mean() > 0.6, f"{(diff[inside] > 30).mean()*100:.0f}%")
        if space == "gamma":
            cv2.imwrite(str(args.out / "02_quad.png"), cv2.cvtColor(label(out, "TWO FINGER FRAME - quad, perspective dots"), cv2.COLOR_RGB2BGR))

    rend = Renderer(*compiled[("UNITY_COLORSPACE_GAMMA",)], linear=False)

    # 5. halftone off + other effects off -> mask region is the normal webcam image
    quiet = {"halftoneEnabled": False, "rgbSplitEnabled": False, "glitchEnabled": False, "grainEnabled": False,
             "edgeGlow": 0.0, "printTexture": 0.0}
    out = rend.render(img, scene_uniforms({**base, "quad": quad, "effects": quiet}))
    diff = np.abs(out.astype(int) - img.astype(int))
    check("halftone+fx off: region returns to normal webcam", diff.max() <= 1, f"max diff {diff.max()}")

    # 6. each effect alone changes only the inside
    for name, fx in (("rgb split", {"rgbSplitEnabled": True, "rgbSplitPx": 6.0}),
                     ("glitch", {"glitchEnabled": True, "glitchIntensity": 1.0, "glitchFrequency": 1.0}),
                     ("grain", {"grainEnabled": True, "grainIntensity": 0.8})):
        u = scene_uniforms({**base, "quad": quad, "effects": {**quiet, **fx}})
        out = rend.render(img, u)
        d = quad_sdf_px(u)
        diff = np.abs(out.astype(int) - img.astype(int)).max(-1)
        check(f"{name} only: confined to mask", diff[d > margin_px].max() <= 0 and diff[d < -20].max() > 3,
              f"outside max {diff[d > margin_px].max()}, inside max {diff[d < -20].max()}")

    # 7. dot spacing follows the parameter (autocorrelation period of the dot pattern)
    def period(spacing: float) -> float:
        fx = {**quiet, "halftoneEnabled": True, "dotSpacingPx": spacing, "gridAngleDeg": 0.0, "perspectiveDots": False, "toneMix": 0.0}
        sq = {"leftIndex": [0.25, 0.2], "leftMiddle": [0.25, 0.85], "rightIndex": [0.75, 0.2], "rightMiddle": [0.75, 0.85]}
        o = rend.render(np.full_like(img, 170), scene_uniforms({**base, "quad": sq, "effects": fx}))
        ac = 0
        for y in range(H // 2 - 60, H // 2 + 60):          # sum row autocorrelations: robust to row phase
            row = o[y, W // 2 - 200:W // 2 + 200, 0].astype(float)
            row -= row.mean()
            ac = ac + np.correlate(row, row, "full")[len(row) - 1:]
        lags = np.arange(len(ac))
        valid = lags > 2
        return float(lags[valid][np.argmax(ac[valid])])

    for sp in (6.0, 9.0, 16.0):
        p = period(sp)
        check(f"dot spacing {sp:.0f}px -> measured period", abs(p - sp) <= 1.0, f"{p:.0f}px")

    # 8. darker image -> bigger ink dots (print style)
    fx = {**quiet, "halftoneEnabled": True, "toneMix": 0.0, "perspectiveDots": False}
    sq = {"leftIndex": [0.0, 0.0], "leftMiddle": [0.0, 1.0], "rightIndex": [1.0, 0.0], "rightMiddle": [1.0, 1.0]}
    coverage = []
    for level in (40, 128, 215):
        o = rend.render(np.full_like(img, level), scene_uniforms({**base, "quad": sq, "effects": fx}))
        patch = o[300:420, 500:780].astype(float)
        paper = np.array([0xDD, 0xF8, 0xFD], float)           # FILTER CMR Blue paper colour
        coverage.append((np.linalg.norm(patch - paper, axis=-1) > 60).mean())  # pixels covered by ink
    check("tone response: dark > mid > light dot coverage", coverage[0] > coverage[1] > coverage[2],
          " / ".join(f"{c*100:.0f}%" for c in coverage))

    # 9. mirroring: mask follows the IMAGE (landmark) coordinates on a mirrored display
    u = scene_uniforms({**base, "uvRect": [1, 0, -1, 1], "ribbon": {"a": [0.15, 0.5], "b": [0.40, 0.5]}})
    out = rend.render(img, u, uv_rect=(1, 0, -1, 1))
    ref = mirrored(img)
    diff = np.abs(out.astype(int) - ref.astype(int)).max(-1)
    cols = np.where(diff.max(0) > 30)[0]
    check("mirrored display: effect at landmark x (left side of screen)", cols.size > 0 and cols.min() > 0.10 * W and cols.max() < 0.46 * W,
          f"changed columns {cols.min() if cols.size else -1}..{cols.max() if cols.size else -1} (expected ~{0.15*W:.0f}..{0.40*W:.0f})")
    check("mirrored display: rest is the mirrored webcam", diff[:, int(0.6 * W):].max() == 0)

    # 10. dot shapes, palettes and styles render (previews)
    tiles = []
    for shape in ("Circle", "Square", "SoftCircle", "Irregular"):
        o = rend.render(img, scene_uniforms({**base, "quad": sq, "effects": {"dotShape": shape, "dotSpacingPx": 12.0, "glitchEnabled": False}}))
        tiles.append(cv2.resize(o[180:540, 460:820], (360, 360), interpolation=cv2.INTER_NEAREST))
        check(f"dot shape {shape} renders", np.isfinite(o).all() and o.std() > 10)
    cv2.imwrite(str(args.out / "03_dot_shapes.png"), cv2.cvtColor(np.hstack(tiles), cv2.COLOR_RGB2BGR))
    pals = []
    for pal in ("FilterCMRBlue", "CyanPrint", "Midnight", "IceRiso"):
        o = rend.render(img, scene_uniforms({**base, "palette": pal, "quad": quad}))
        pals.append(cv2.resize(label(o, pal), (640, 360), interpolation=cv2.INTER_AREA))
    cv2.imwrite(str(args.out / "04_palettes.png"), cv2.cvtColor(np.vstack([np.hstack(pals[:2]), np.hstack(pals[2:])]), cv2.COLOR_RGB2BGR))

    # 11. cross-fade frame (ribbon fading out, quad fading in) has no hard seams
    u = scene_uniforms({**base, "ribbon": {**ribbon, "weight": 0.45}, "quad": {**quad, "weight": 0.55}})
    out = rend.render(img, u)
    cv2.imwrite(str(args.out / "05_crossfade.png"), cv2.cvtColor(label(out, "point -> peace cross-fade"), cv2.COLOR_RGB2BGR))
    check("cross-fade renders", out.std() > 10)

    passed = sum(ok for _, ok, _ in RESULTS)
    print(f"\n{passed}/{len(RESULTS)} checks passed. Previews: {args.out}")
    return 0 if passed == len(RESULTS) else 1


if __name__ == "__main__":
    sys.exit(main())
