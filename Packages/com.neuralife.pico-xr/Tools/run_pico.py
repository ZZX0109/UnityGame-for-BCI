"""Run real Unity PICO configuration, regression tests and isolated VR builds."""
import argparse
import atexit
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
PICO_COMMIT = "3aa3e62bff41df618529eeb60ff02c29a515dafe"


def inspect_apk(build_dir, editor):
    manifest = json.loads((build_dir / "build-manifest.json").read_text(encoding="utf-8"))
    game = manifest["game"]
    apk = build_dir / ("Neuralife-" + game + "-PICO.apk")
    with zipfile.ZipFile(apk) as archive:
        files = archive.namelist()
        if archive.testzip() or "lib/arm64-v8a/libil2cpp.so" not in files or not any("openxr" in f.lower() and f.startswith("lib/arm64-v8a/") for f in files):
            raise ValueError("Missing ARM64 IL2CPP / OpenXR runtime or corrupt APK")
    sdks = [editor.parent / "Data/PlaybackEngines/AndroidPlayer/SDK"]
    sdks += [Path(os.environ[key]) for key in ("ANDROID_SDK_ROOT", "ANDROID_HOME") if os.environ.get(key)]
    aapt = next((p for sdk in sdks for p in sorted((sdk / "build-tools").glob("*/aapt.exe"), reverse=True)), None)
    if not aapt:
        raise ValueError("Cannot inspect the Android manifest: add the SDK build-tools directory through ANDROID_SDK_ROOT")
    badging = subprocess.check_output([str(aapt), "dump", "badging", str(apk)], text=True, encoding="utf-8", errors="replace")
    tree = subprocess.check_output([str(aapt), "dump", "xmltree", str(apk), "AndroidManifest.xml"], text=True, encoding="utf-8", errors="replace")
    app_id = "com.neuralife." + game + ".vr"
    if f"package: name='{app_id}'" not in badging or not re.search(r"(?m)^native-code:\s*'arm64-v8a'\s*$", badging):
        raise ValueError("APK application ID or native ABI does not match this game")
    if not re.search(r'pvr\.app\.type[^\n]*\n[^\n]*"vr"', tree):
        raise ValueError("PICO VR application type is missing from the packaged Android manifest")
    java_roots = [editor.parent / "Data/PlaybackEngines/AndroidPlayer/OpenJDK"]
    if os.environ.get("JAVA_HOME"):
        java_roots.append(Path(os.environ["JAVA_HOME"]))
    java = next((str(root / "bin/java.exe") for root in java_roots if (root / "bin/java.exe").is_file()), shutil.which("java"))
    signer = aapt.parent / "lib/apksigner.jar"
    if not java or not signer.is_file():
        raise ValueError("Android signature verification requires a JDK and SDK build-tools/apksigner")
    subprocess.run([java, "-jar", str(signer), "verify", str(apk)], check=True)
    return {"apk": str(apk), "applicationId": app_id, "abi": "arm64-v8a", "picoAppType": "vr", "signatureVerified": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["configure", "test", "windows", "android", "all"], nargs="?", default="all")
    parser.add_argument("--editor", type=Path, default=Path(os.environ.get("UNITY_EDITOR_PATH", "D:/SoftwareManagement/Unity/2022.3.62f3c1/Editor/Unity.exe")))
    parser.add_argument("--sdk-mirror", type=Path, help="Optional local Git mirror containing the exact official PICO commit; no project dependency is replaced.")
    parser.add_argument("--timeout", type=int, default=900)
    parser.add_argument("--test-filter", help="Unity test name or namespace, e.g. Neuralife.XR.Tests")
    args = parser.parse_args()
    if not args.editor.is_file():
        parser.error("Set UNITY_EDITOR_PATH to the installed Unity Editor.")
    if args.action == "android" and not (args.editor.parent / "Data/PlaybackEngines/AndroidPlayer").is_dir():
        parser.error("Android Build Support (SDK / NDK / OpenJDK) is missing for this Editor.")
    output = ROOT / "BuildOutput" / ("PICO-Validation-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%f"))
    output.mkdir(parents=True)
    environment = dict(os.environ, PYTHONUTF8="1", PYTHONDONTWRITEBYTECODE="1")
    if args.sdk_mirror:
        mirror = args.sdk_mirror.resolve()
        sha = subprocess.check_output(["git", "-C", str(mirror), "rev-parse", "HEAD"], text=True).strip()
        if sha != PICO_COMMIT:
            parser.error("The SDK mirror does not contain the pinned official PICO version.")
        environment.update(GIT_CONFIG_COUNT="1", GIT_CONFIG_KEY_0="url." + mirror.as_uri() + "/.insteadOf",
                           GIT_CONFIG_VALUE_0="https://github.com/Pico-Developer/PICO-Unity-OpenXR-SDK.git", GIT_TERMINAL_PROMPT="0")
    # Allow the existing keyboard adapters in desktop builds. Android uses only
    # the new backend; every VR game path avoids legacy Input polling.
    settings = ROOT / "ProjectSettings/ProjectSettings.asset"
    original = settings.read_text(encoding="utf-8-sig")
    desired = "1" if args.action == "android" else "2"
    updated = re.sub(r"(?m)^(  activeInputHandler:) \d+$", r"\g<1> " + desired, original)
    if updated != original:
        settings.write_text(updated, encoding="utf-8", newline="\n")
        previous = re.search(r"(?m)^  activeInputHandler: (\d+)$", original).group(1)
        def restore_input():
            current = settings.read_text(encoding="utf-8-sig")
            restored = re.sub(r"(?m)^(  activeInputHandler:) " + desired + "$", r"\g<1> " + previous, current)
            if current != restored:
                settings.write_text(restored, encoding="utf-8", newline="\n")
        atexit.register(restore_input)
    summary = {"headsetAcceptance": "NOT_TESTED", "androidBuild": "NOT_TESTED", "testFilter": args.test_filter, "steps": []}

    def run(label, arguments, tests=False):
        log = output / (label + ".log")
        command = [str(args.editor), "-batchmode", "-job-worker-count", "4", "-projectPath", str(ROOT), "-logFile", str(log)] + arguments
        print("Running", label, flush=True)
        process = subprocess.Popen(command, cwd=ROOT, env=environment, stdin=subprocess.DEVNULL,
                                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        xml = output / (label + ".xml")
        shutdown_complete = False
        start = time.monotonic()
        while process.poll() is None and time.monotonic() - start < args.timeout:
            if tests and xml.is_file() and time.time() - xml.stat().st_mtime > 20 and log.is_file():
                with log.open("rb") as stream:
                    stream.seek(max(0, log.stat().st_size - 4096))
                    tail = stream.read()
                if b"Input System module state changed to: Shutdown." in tail and b"channel disconnected successfully" in tail:
                    shutdown_complete = True
                    break
            time.sleep(0.5)
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.kill(); process.wait()
        code = process.returncode
        item = {"step": label, "exitCode": code, "stoppedAfterShutdown": shutdown_complete, "log": log.name}
        if tests:
            if xml.is_file():
                result = ET.parse(xml).getroot()
                item.update(result=result.get("result"), total=result.get("total"), passed=result.get("passed"), failed=result.get("failed"), skipped=result.get("skipped"))
            else:
                item["result"] = "MISSING_XML"
        summary["steps"].append(item)
        passed = item.get("result") in ("Passed", "Skipped:Ignored") and item.get("failed") == "0" and int(item.get("passed") or 0) > 0
        ok = (code == 0 or shutdown_complete) and (not tests or passed)
        item["status"] = "passed" if ok else "failed"
        (output / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(item["status"].upper(), label, flush=True)
        return ok

    ok = args.action in ("windows", "android") or run("Configure", ["-nographics", "-executeMethod", "Neuralife.XR.Editor.PicoProject.Configure", "-quit"])
    if ok and args.action in ("test", "all"):
        for mode in ("EditMode", "PlayMode"):
            selection = ["-testFilter", args.test_filter] if args.test_filter else []
            if not run(mode, ["-force-d3d11", "-runTests", "-testPlatform", mode, "-testResults", str(output / (mode + ".xml"))] + selection, tests=True):
                ok = False
    if ok and args.action in ("windows", "android", "all"):
        android = args.action == "android"
        build = ["-force-d3d11", "-executeMethod", "Neuralife.XR.Editor.PicoProject.Build" + ("Android" if android else "Windows"), "-quit"]
        if android:
            build += ["-buildTarget", "Android"]
            before = set((ROOT / "BuildOutput").glob("PICO-Android-*/build-manifest.json"))
        ok = run("Android" if android else "PCVR", build)
        if android:
            if ok:
                try:
                    created, = set((ROOT / "BuildOutput").glob("PICO-Android-*/build-manifest.json")) - before
                    summary["apkInspection"] = inspect_apk(created.parent, args.editor.resolve())
                    inspection = dict(summary["apkInspection"])
                    apk_name = Path(inspection.pop("apk")).name
                    manifest = json.loads(created.read_text(encoding="utf-8"))
                    inspection.update(artifact=apk_name, sourceFingerprint=manifest["sourceFingerprint"], headsetAcceptance="NOT_TESTED",
                                      sha256=next(entry["sha256"] for entry in manifest["files"] if entry["path"] == apk_name))
                    (created.parent / "apk-verification.json").write_text(json.dumps(inspection, indent=2) + "\n", encoding="utf-8")
                except (OSError, ValueError, KeyError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
                    ok = False
                    summary["apkInspectionError"] = str(error)
            summary["androidBuild"] = "passed" if ok else "failed"
    summary["status"] = "passed" if ok else "failed"
    (output / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Evidence:", output, flush=True)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
