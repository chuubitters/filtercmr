# Shader check and preview (optional, development only)

Compiles **and runs** `FILTERCMR_WebcamComposite.shader` outside Unity, then checks the results numerically:

1. Extracts the shader's `CGPROGRAM` block. It compiles every keyword variant (UI clip rect, alpha clip, Gamma/Linear) with glslang's HLSL front end, against Unity's built-in `UnityCG.cginc` / `UnityUI.cginc`.
2. Translates the SPIR-V to GLSL 4.5 (spirv-cross) and renders on a software GPU (Mesa llvmpipe via moderngl).
3. Builds the uniforms with the **real** C# code: mask generators and `CompositeUniformWriter`, via `Tools/DotnetVerification` `scene` mode.
4. Asserts:
   * the output is pixel-identical to the input outside the masks (Gamma and Linear);
   * the inside is transformed and blue/cyan;
   * every effect is confined to the mask;
   * the dot period equals the spacing parameter;
   * the tone response is correct;
   * the mirrored display is correct;
   * the edge is soft.
5. Writes preview PNGs: ribbon, quad, dot shapes, palettes, cross-fade.

`UnityPrelude.hlsl` maps Unity's DX9-style `sampler2D` / `tex2D` / `tex2Dlod` to DX10 `Texture2D.Sample`, **for this harness only**. glslang's DX9 compatibility mode lowers them incorrectly, while Unity's compiler supports them natively. The shader source itself is unchanged.

## Requirements

```bash
sudo apt-get install glslang-tools spirv-cross libegl1 libgl1-mesa-dri      # or the Homebrew equivalents
pip install moderngl numpy opencv-python scikit-image
git clone --depth 1 https://github.com/TwoTailsGames/Unity-Built-in-Shaders /tmp/ubs   # Unity built-in CGIncludes
```

Plus the .NET 8 SDK, for `Tools/DotnetVerification`.

## Run

```bash
python3 Tools/ShaderPreview/shader_check.py --cgincludes /tmp/ubs/CGIncludes --out /tmp/filtercmr_preview
```

The test image is built around `skimage.data.astronaut()`, a public-domain NASA portrait. This tool doesn't replace opening the project in Unity: Unity's Metal shader compiler is the final word.
