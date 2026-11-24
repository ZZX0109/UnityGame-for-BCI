"""Locate Blender installations on this machine (probe-only, no writes)."""
import os
import winreg

roots = [
    "C:/Program Files",
    "C:/Program Files (x86)",
    "D:/",
    "C:/",
    "D:/program",
    "C:/Users/Ait/AppData/Local",
]
for root in roots:
    try:
        for name in os.listdir(root):
            if "blender" in name.lower():
                print(os.path.join(root, name))
    except Exception:
        pass

try:
    k = winreg.OpenKey(winreg.HKEY_CLASSES_ROOT, r"blendfile\shell\open\command")
    print("reg blendfile open:", winreg.QueryValueEx(k, "")[0])
except Exception as exc:
    print("reg blendfile: not found")

try:
    k = winreg.OpenKey(
        winreg.HKEY_CURRENT_USER,
        r"Software\Microsoft\Windows\CurrentVersion\Uninstall",
    )
    i = 0
    while True:
        sub = winreg.EnumKey(k, i)
        i += 1
        if "blender" in sub.lower():
            print("uninstall key:", sub)
            try:
                sk = winreg.OpenKey(k, sub)
                vals = {name: winreg.QueryValueEx(sk, name)[0]
                        for name in ("InstallLocation", "DisplayIcon", "UninstallString")}
                for kk, vv in vals.items():
                    print(" ", kk, "=", vv)
            except Exception:
                pass
except Exception:
    print("uninstall enum end")
