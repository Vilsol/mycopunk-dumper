import os
import sys

import UnityPy
import UnityPy.config

UnityPy.config.FALLBACK_UNITY_VERSION = sys.argv[3]

bundle, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
env = UnityPy.load(bundle)
n = 0
for obj in env.objects:
    if obj.type.name != "Texture2D":
        continue
    tex = obj.read()
    name = getattr(tex, "m_Name", None) or getattr(tex, "name", "unnamed")
    tex.image.save(os.path.join(out, f"{name}.png"))
    n += 1
    print(f"{name}: {tex.m_Width}x{tex.m_Height}")
print(f"exported {n} textures")
