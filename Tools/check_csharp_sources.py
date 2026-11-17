#!/usr/bin/env python3
"""Compile current Unity C# sources without starting/licensing the Editor.

This is a managed compilation check, NOT Unity import, Test Runner or a Player
build. References come from the installed Editor and the project's imported
package assemblies. Output is temporary unless --output is supplied.
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


def compile_sources(project: Path, editor: Path, source_root: Path,
                    assembly: str, output: Path, extras: list[Path], tests: bool, player: bool = False,
                    rules: bool = False) -> None:
    data = editor.resolve().parent / "Data"
    runner = data / "NetCoreRuntime" / "dotnet.exe"
    compiler = data / "DotNetSdkRoslyn" / "csc.dll"
    if not runner.is_file() or not compiler.is_file():
        raise RuntimeError("Editor must contain NetCoreRuntime/dotnet.exe and DotNetSdkRoslyn/csc.dll")
    references: dict[str, Path] = {}
    defines = {"UNITY_EDITOR", "UNITY_EDITOR_WIN", "UNITY_STANDALONE_WIN", "UNITY_INCLUDE_TESTS",
               "UNITY_2022_3_OR_NEWER", "UNITY_2022_3", "UNITY_2022", "UNITY_5_3_OR_NEWER"}
    # Unity's generated response file records its exact framework profile. It
    # may predate new sources: only references/defines are reused, never sources.
    candidates = sorted((project / "Library/Bee/artifacts").glob("*/" + assembly + ".rsp"))
    editor_candidates = [path for path in candidates if path.parent.name.endswith("E.dag")]
    if editor_candidates:
        candidates = editor_candidates
    if candidates:
        for line in candidates[-1].read_text(encoding="utf-8-sig").splitlines():
            if line.startswith("-define:"):
                defines.add(line.partition(":")[2])
            match = re.match(r'-r:"(.*)"$', line)
            if not match:
                continue
            value = match.group(1)
            if "/Editor/Data/" in value.replace("\\", "/"):
                value = str(data / value.replace("\\", "/").split("/Editor/Data/", 1)[1])
            path = Path(value)
            if not path.is_absolute():
                path = project / path
            if path.is_file():
                references[path.name] = path.resolve()
    else:
        for path in (data / "Managed").glob("**/*.dll"):
            references[path.name] = path.resolve()
        for path in (data / "NetStandard/ref/2.1.0").glob("*.dll"):
            references[path.name] = path.resolve()
        for name in ("UnityEngine.UI.dll", "UnityEditor.UI.dll"):
            paths = list((project / "Library/ScriptAssemblies").glob(name))
            if not paths:
                paths = list((data / "Resources/PackageManager/ProjectTemplates/libcache").glob("*/ScriptAssemblies/" + name))
            if paths:
                references[name] = paths[0].resolve()
    # Tests can use modules absent from the runtime's transitive references
    # (for example ScreenCapture); use the same Editor's complete module set.
    for path in (data / "Managed/UnityEngine").glob("*.dll"):
        references[path.name] = path.resolve()
    for path in extras:
        references[path.name] = path.resolve()
    if player:
        defines = {value for value in defines if not value.startswith('UNITY_EDITOR') and value != 'UNITY_INCLUDE_TESTS'}
        references = {name: path for name, path in references.items() if not name.startswith('UnityEditor')}
    output.mkdir(parents=True, exist_ok=True)

    def run(name: str, sources: list[Path], refs: list[Path], executable: bool = False) -> Path:
        if not sources:
            raise RuntimeError("No sources for " + name)
        dll = output / (name + (".exe" if executable else ".dll"))
        args = ["-nologo", "-utf8output", "-preferreduilang:en-US", "-target:" + ("exe" if executable else "library"), "-nostdlib+", "-langversion:9.0", "-deterministic+", "-unsafe+",
                '-out:"' + str(dll) + '"', "-define:" + ";".join(sorted(defines))]
        args += ['-r:"' + str(path) + '"' for path in refs]
        args += ['"' + str(path.resolve()) + '"' for path in sorted(sources)]
        rsp = output / (name + ".rsp")
        rsp.write_text("\n".join(args) + "\n", encoding="utf-8", newline="\n")
        result = subprocess.run([str(runner), str(compiler), "@" + str(rsp)], cwd=project,
                                capture_output=True, text=True, encoding="utf-8", errors="replace")
        log = result.stdout + result.stderr
        (output / (name + ".log")).write_text(log, encoding="utf-8", newline="\n")
        if result.returncode:
            print(log, end="")
            raise RuntimeError("Managed compilation failed: " + name)
        print(f"MANAGED COMPILE PASS: {name} ({len(sources)} current sources, {log.count('warning CS')} warnings; see log)")
        return dll

    runtime = run(assembly, list(source_root.rglob("*.cs")), list(references.values()))
    results = {"runtime": str(runtime), "configuration": "player_defines" if player else "editor_defines", "unityTestRunner": "NOT RUN", "playerBuild": "NOT RUN"}
    if tests:
        test_refs = dict(references)
        for name in ("UnityEngine.TestRunner.dll", "UnityEditor.TestRunner.dll"):
            path = project / "Library/ScriptAssemblies" / name
            if not path.is_file():
                paths = list((data / "Resources/PackageManager/ProjectTemplates/libcache").glob("*/ScriptAssemblies/" + name))
                if not paths:
                    raise RuntimeError("Missing imported test reference: " + name)
                path = paths[0]
            test_refs[name] = path.resolve()
        nunit = list((project / "Library/PackageCache").glob("com.unity.ext.nunit@*/net35/unity-custom/nunit.framework.dll"))
        if not nunit:
            raise RuntimeError("Import test-framework package before compiling test sources")
        test_refs["nunit.framework.dll"] = nunit[0].resolve()
        test_refs[runtime.name] = runtime
        test_sources = list((source_root.parent / "Tests").rglob("*.cs"))
        tests_dll = run(assembly + ".Tests", test_sources, list(test_refs.values()))
        results["testsCompilation"] = str(tests_dll)
        if rules:
            from run_claw_rules import run as run_claw_rules
            if assembly != "ClawMachine.Runtime":
                raise RuntimeError("--rules requires the ClawMachine.Runtime assembly")
            if run_claw_rules(project, editor, output, output / "PureRules"):
                raise RuntimeError("Claw pure managed rule checks failed")
            results["pureManagedRules"] = "PASS (Claw rules; not Unity Test Runner)"
    (output / "managed-compilation.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8", newline="\n")


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-editor", type=Path, required=True)
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--source-root", type=Path, default=Path("Assets/ClawMachine/Scripts"))
    parser.add_argument("--assembly", default="ClawMachine.Runtime")
    parser.add_argument("--extra-reference", type=Path, action="append", default=[])
    parser.add_argument("--tests", action="store_true")
    parser.add_argument("--rules", action="store_true", help="run Claw pure managed rule checks (also enables --tests)")
    parser.add_argument("--player", action="store_true", help="Compile without Editor defines/references; this is not a Player build")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if (args.tests or args.rules) and args.player: parser.error('--tests/--rules require the Editor configuration')
    project = args.project.resolve()
    source = args.source_root if args.source_root.is_absolute() else project / args.source_root
    try:
        if args.output:
            compile_sources(project, args.unity_editor, source, args.assembly, args.output.resolve(), args.extra_reference, args.tests or args.rules, args.player, args.rules)
        else:
            with tempfile.TemporaryDirectory(prefix="claw-csharp-") as folder:
                compile_sources(project, args.unity_editor, source, args.assembly, Path(folder), args.extra_reference, args.tests or args.rules, args.player, args.rules)
    except RuntimeError as error:
        print(str(error))
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
