#!/usr/bin/env python3
"""Run actual ClawSession/NUnit assertions with Unity's Mono, without Editor startup.

Requires the managed compilation response files from check_csharp_sources.py.
Recompiles CURRENT Claw runtime/tests before running an explicit pure-method
allowlist. No Physics, UI, PlayerPrefs, JsonUtility or Unity lifecycle is invoked.
This is NOT Unity Test Runner, import, physics acceptance or a Player build.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys


HARNESS = r'''using System;
using System.Reflection;
using NUnit.Framework;
using ClawMachine;
using ClawMachine.Tests.EditMode;
class ClawPureRules {
    static int passed;
    static void Run(string name, Action action) {
        action(); passed++; Console.WriteLine("PURE RULE PASS: " + name);
    }
    static ClawSession EmptyReturning() {
        var s = new ClawSession(); s.Reset(2, true);
        Assert.IsTrue(s.BeginAttempt()); Assert.IsTrue(s.Advance(GraspState.ContactDescent));
        Assert.IsTrue(s.Advance(GraspState.Closing)); s.RecordFailure(GraspFailure.NoContact);
        Assert.IsTrue(s.Advance(GraspState.Returning)); return s;
    }
    static int Main() {
        try {
            var core = new ClawRuleTests();
            Run("Attempt_HasOneEntryAndRejectsSkippedPhases", core.Attempt_HasOneEntryAndRejectsSkippedPhases);
            Run("Pause_FreezesTransitionsAndBlocksAttemptEntry", core.Pause_FreezesTransitionsAndBlocksAttemptEntry);
            Run("Award_RequiresReleaseCurrentIdentityTargetAndStableContainment", core.Award_RequiresReleaseCurrentIdentityTargetAndStableContainment);
            Run("NeverReleasedPrize_CannotAwardEvenIfAtChute", core.NeverReleasedPrize_CannotAwardEvenIfAtChute);
            Run("Reset_InvalidatesOldRunAndClearsCountersPauseAndOutcome", core.Reset_InvalidatesOldRunAndClearsCountersPauseAndOutcome);
            Run("AttemptLimit_EndsAfterFinalFailedAttempt", core.AttemptLimit_EndsAfterFinalFailedAttempt);
            var optimization = new ClawPreferencesTests();
            Run("EmptyRecoveryAndSlippedDelivery_KeepDistinctAwardRules", optimization.EmptyRecoveryAndSlippedDelivery_KeepDistinctAwardRules);
            Run("Returning_PauseRetryAndSingleAttempt", () => {
                var s=EmptyReturning(); int session=s.SessionId;
                s.Pause(true); Assert.IsFalse(s.Advance(GraspState.Result)); Assert.IsFalse(s.BeginAttempt());
                s.Pause(false); Assert.IsFalse(s.BeginAttempt()); Assert.AreEqual(1,s.AttemptId);
                Assert.IsTrue(s.Advance(GraspState.Result)); Assert.IsTrue(s.Retry());
                Assert.AreEqual(session,s.SessionId); Assert.AreEqual(1,s.AttemptId);
                Assert.IsTrue(s.BeginAttempt()); Assert.AreEqual(2,s.AttemptId); Assert.AreEqual(0,s.Remaining);
            });
            Run("Returning_ResetRejectsStaleAwardAndPendingResult", () => {
                var s=EmptyReturning(); int old=s.SessionId; s.Reset(2,false);
                Assert.Greater(s.SessionId,old); Assert.AreEqual(0,s.AttemptId);
                Assert.IsFalse(s.Advance(GraspState.Result)); Assert.IsFalse(s.TryAward(old,1,"target","target",true));
                Assert.AreEqual(GraspState.Idle,s.State); Assert.IsFalse(s.Released); Assert.IsFalse(s.Awarded);
            });
            Console.WriteLine("PURE MANAGED RULES PASS: " + passed + " checks; Unity native play/tests NOT RUN");
            return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
'''


def run(project: Path, editor: Path, managed: Path, output: Path) -> int:
    data = editor.resolve().parent / "Data"
    dotnet, compiler = data / "NetCoreRuntime/dotnet.exe", data / "DotNetSdkRoslyn/csc.dll"
    mono = data / "MonoBleedingEdge/bin/mono.exe"
    for path in (dotnet, compiler, mono):
        if not path.is_file():
            raise RuntimeError(f"Missing installed Unity tool: {path}")
    output.mkdir(parents=True, exist_ok=True)
    runtime = output / "ClawMachine.Runtime.dll"
    tests = output / "ClawMachine.Runtime.Tests.dll"
    reference_paths: set[Path] = set()
    source_files: list[Path] = []

    def compile_current(name: str, root: Path, destination: Path, tests_pass: bool = False) -> None:
        template = managed / (name + ".rsp")
        if not template.is_file():
            raise RuntimeError(f"Run managed source compilation first; missing {template}")
        args = []
        for line in template.read_text(encoding="utf-8-sig").splitlines():
            if line.startswith("-out:") or line.lower().endswith('.cs"'):
                continue
            match = re.match(r'-r:"(.*)"$', line)
            if match:
                path = Path(match.group(1))
                if tests_pass and path.name == runtime.name:
                    path = runtime
                    line = '-r:"' + str(path) + '"'
                if not path.is_file():
                    raise RuntimeError(f"Missing reference; rebuild managed response files: {path}")
                reference_paths.add(path.resolve())
            args.append(line)
        current = sorted(root.rglob("*.cs"))
        if not current:
            raise RuntimeError(f"No current sources under {root}")
        source_files.extend(current)
        args.append('-out:"' + str(destination) + '"')
        args.extend('"' + str(path.resolve()) + '"' for path in current)
        response = output / (name + ".rsp")
        response.write_text("\n".join(args) + "\n", encoding="utf-8", newline="\n")
        completed = subprocess.run([str(dotnet), str(compiler), "@" + str(response)], cwd=project,
                                   capture_output=True, text=True, encoding="utf-8", errors="replace")
        log = completed.stdout + completed.stderr
        (output / (name + ".log")).write_text(log, encoding="utf-8", newline="\n")
        if completed.returncode:
            raise RuntimeError(log)
        print(f"CURRENT MANAGED COMPILE PASS: {name}, {len(current)} source files")

    compile_current("ClawMachine.Runtime", project / "Assets/ClawMachine/Scripts", runtime)
    compile_current("ClawMachine.Runtime.Tests", project / "Assets/ClawMachine/Tests", tests, True)
    harness = output / "ClawPureRules.cs"
    harness.write_text(HARNESS, encoding="utf-8", newline="\n")
    executable = output / "ClawPureRules.exe"
    refs = {path.name: path for path in reference_paths}
    refs[runtime.name], refs[tests.name] = runtime, tests
    args = ["-nologo", "-utf8output", "-target:exe", "-nostdlib+", "-langversion:9.0", '-out:"' + str(executable) + '"']
    args += ['-r:"' + str(path) + '"' for path in refs.values()]
    args.append('"' + str(harness) + '"')
    rsp = output / "ClawPureRules.rsp"
    rsp.write_text("\n".join(args) + "\n", encoding="utf-8", newline="\n")
    compiled = subprocess.run([str(dotnet), str(compiler), "@" + str(rsp)], cwd=project,
                              capture_output=True, text=True, encoding="utf-8", errors="replace")
    (output / "harness-compile.log").write_text(compiled.stdout + compiled.stderr, encoding="utf-8", newline="\n")
    if compiled.returncode:
        raise RuntimeError(compiled.stdout + compiled.stderr)
    env = os.environ.copy()
    env["MONO_PATH"] = os.pathsep.join(str(path) for path in sorted({path.parent for path in refs.values()} | {output}))
    result = subprocess.run([str(mono), str(executable)], cwd=output, env=env,
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    log = result.stdout + result.stderr
    (output / "pure-rules.log").write_text(log, encoding="utf-8", newline="\n")
    print(log, end="")
    summary = {"kind": "pure_managed_rules", "passed": result.returncode == 0, "checks": 9 if result.returncode == 0 else None,
               "unityTestRunner": "NOT RUN", "physicsUIPlayerAcceptance": "NOT RUN",
               "currentSourceSha256": {str(path.relative_to(project)): hashlib.sha256(path.read_bytes()).hexdigest() for path in source_files}}
    (output / "pure-rules.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8", newline="\n")
    return result.returncode


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-editor", type=Path, required=True)
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--managed", type=Path, default=Path("BuildOutput/Optimization-20261116/Managed"))
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    project = args.project.resolve()
    managed = args.managed if args.managed.is_absolute() else project / args.managed
    output = args.output.resolve() if args.output else managed / "PureRules"
    try:
        return run(project, args.unity_editor, managed.resolve(), output.resolve())
    except (RuntimeError, OSError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
