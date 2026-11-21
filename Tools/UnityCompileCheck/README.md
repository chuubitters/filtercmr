# Unity compile check (optional, development only)

This compiles every FILTER CMR Unity script (runtime + editor) with the .NET SDK against **signature-only stubs** of the UnityEngine, UnityEditor and uGUI APIs the project uses. It targets `netstandard2.1`, Unity 2022.3's API level. It catches C# errors without a Unity install. It doesn't replace opening the project in Unity.

Each stub member was checked against Unity's official C# reference source at tag **2022.3.62f1** and, for uGUI, against both the `2018.4` and the 7.0 (`main`) branches, which bracket uGUI 1.0.0. `verify_api.py` repeats that check:

```bash
git clone --depth 1 --branch 2022.3.62f1 --filter=blob:none --sparse https://github.com/Unity-Technologies/UnityCsReference /tmp/ucs
(cd /tmp/ucs && git sparse-checkout set Runtime Modules Editor/Mono)
git clone --depth 1 https://github.com/Unity-Technologies/uGUI /tmp/ugui
git clone --depth 1 --branch 2018.4 https://github.com/Unity-Technologies/uGUI /tmp/ugui2018
python3 verify_api.py             # every listed API must print OK
dotnet build                      # 0 errors, 0 warnings expected
```

When a script starts using a new Unity API, add it to `stubs/` **and** to `verify_api.py`.
