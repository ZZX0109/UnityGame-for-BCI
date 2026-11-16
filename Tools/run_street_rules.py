"""Run selected actual managed street rules under Unity Mono; no native scene/physics/UI."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import shutil
import subprocess
import sys
from check_csharp_sources import compile_sources

ROOT = Path(__file__).resolve().parents[1]


def main():
    if hasattr(sys.stdout, 'reconfigure'): sys.stdout.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity-editor', type=Path, required=True)
    parser.add_argument('--tmp-reference', type=Path, required=True)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    output = (args.output or ROOT / 'BuildOutput' / ('StreetRules-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%f'))).resolve()
    output.mkdir(parents=True, exist_ok=True)
    data = args.unity_editor.resolve().parent / 'Data'
    compile_sources(ROOT, args.unity_editor, ROOT / 'Assets/StreetCrossing/Scripts', 'StreetCrossing.Runtime', output, [args.tmp_reference], True)
    groups = {
        'FourLaneRulesTests': ['FourLaneCentersAreDistinctAndSymmetric', 'SidewalkOffsetMatchesTheNewSpawnAndDestination',
            'CrossingSafetyRequiresAllFourRoadSectionsBeforeFarSideArrival'],
        'StreetRulesTests': [
            'RedLightBrakingIsGradualAndStopsTheFrontBeforeTheLine', 'QueuedFollowerMaintainsGapAndRestartsWithBoundedAcceleration',
            'AmberAllowsOnlyTheCarTooCloseToStopToFinishItsCommitment',
            'ObservationRequiresSustainedVisibleHeadingAtKerbAndEmitsEachSideOnce', 'WalkEntryRemainsLegalThroughFlashingAndLaterRedUntilDestination',
            'UnclearedVehicleAtEntryCannotBeForgottenAtArrival', 'ObservationNearMissCollisionAndArrivalEventsAreIndependentlyDeduplicated',
            'DestinationAloneOrAnUnpreparedBypassCannotCompleteASafeCrossing', 'LeavingMarkedCrosswalkRecordsOneRouteViolationAndRejectsSafeArrival'],
        'AuditRulesTests': ['DefaultObservationRequirementsRejectMissingAndLateObservations',
            'ReturningToKerbDoesNotRetroactivelyChangeTheFirstEntryObservationSnapshot', 'LegacyYawOnlyCallsCannotFabricateVisibleRoadObservation',
            'VisibilityLossResetsDwellAndRoadwayEvidenceCannotFinishIt', 'StationaryContactDoesNotFinishPracticeAndMovingContactIsCountedOnce',
            'StationaryContactAndNearMissRemainSeparateFromSafetyArrival'],
        'OptimizationFeedbackTests': ['RiskOverridesInspectionAndExpiryRevealsTheRemainingMessage',
            'ObservationFreshnessCanBeRenewedWithoutChangingScoringOrEmittingDuplicateCredit',
            'TransientDangerExpiry_RevealsPersistentViolationInsteadOfErasingIt',
            'TerminalResult_SurvivesCrossingNoticeClearsAndResetRemovesEverything',
            'FreshnessRequiresRenewingBothSidesButDoesNotDuplicateObservationCredit']}
    calls = []
    for group, methods in groups.items():
        for method in methods:
            calls.append(f'new {group}().{method}(); passed++; Console.WriteLine("PURE RULE PASS: {method}");')
    for seed, count in [(3101, 6), (3202, 10), (3303, 8)]:
        calls.append(f'new StreetBackgroundRouteTests().EntireBackgroundRouteRemainsOutsideTrainingCorridorRoadAndSideJunction({seed},{count}); passed++; Console.WriteLine("PURE RULE PASS: background routes {seed}");')
    source = 'using System; using StreetCrossing.Tests.EditMode; class StreetRulesRunner { static int Main() { try { int passed=0;\n' + '\n'.join(calls) + '\nConsole.WriteLine("PURE MANAGED RULES PASS: "+passed+"; Unity Test Runner NOT RUN"); return 0; } catch(Exception e) { Console.Error.WriteLine(e); return 1; } } }'
    harness = output / 'StreetRulesRunner.cs'; harness.write_text(source, encoding='utf-8', newline='\n')
    runtime_rsp = (output / 'StreetCrossing.Runtime.Tests.rsp').read_text(encoding='utf-8')
    refs = [line for line in runtime_rsp.splitlines() if line.startswith('-r:')]
    refs.append('-r:"' + str(output / 'StreetCrossing.Runtime.Tests.dll') + '"')
    exe = output / 'StreetRulesRunner.exe'
    rsp = output / 'StreetRulesRunner.rsp'
    rsp.write_text('\n'.join(['-nologo', '-utf8output', '-target:exe', '-nostdlib+', '-out:"' + str(exe) + '"'] + refs + ['"' + str(harness) + '"']), encoding='utf-8', newline='\n')
    result = subprocess.run([str(data / 'NetCoreRuntime/dotnet.exe'), str(data / 'DotNetSdkRoslyn/csc.dll'), '@' + str(rsp)], cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')
    if result.returncode: print(result.stdout + result.stderr); return result.returncode
    for name in ['UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.SharedInternalsModule.dll']:
        shutil.copy2(data / 'Managed/UnityEngine' / name, output / name)
    nunit = next((ROOT / 'Library/PackageCache').glob('com.unity.ext.nunit@*/net35/unity-custom/nunit.framework.dll'))
    shutil.copy2(nunit, output / nunit.name)
    result = subprocess.run([str(data / 'MonoBleedingEdge/bin/mono.exe'), str(exe)], cwd=output, capture_output=True, text=True, encoding='utf-8', errors='replace')
    log = result.stdout + result.stderr; print(log)
    (output / 'pure-rules.log').write_text(log, encoding='utf-8', newline='\n')
    (output / 'pure-rules-summary.json').write_text(json.dumps({'status': 'passed' if result.returncode == 0 else 'failed', 'checks': len(calls), 'unityTests': 'not_run', 'physics': 'not_run', 'gui': 'not_run'}, indent=2) + '\n', encoding='utf-8', newline='\n')
    return result.returncode


if __name__ == '__main__': raise SystemExit(main())
