"""Retired asset serializer. Use the Claw scene generator."""
import sys


def main():
    print(
        "Asset generation has moved to Unity. No files were changed.\n"
        "Use python docs/Game02_ClawMachine/generate_prototype.py, then:\n"
        "Unity -batchmode -quit -projectPath . -executeMethod "
        "ClawMachine.Editor.ClawSceneBuilder.PrepareForValidation\n"
        "For read-only checks: python Tools/validate_project.py",
        file=sys.stderr,
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())
