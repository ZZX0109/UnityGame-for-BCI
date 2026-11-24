# -*- coding: utf-8 -*-
"""Brace-balance check that strips C# string/char literals and comments,
reporting the first line where depth goes negative."""
import re
import sys

FILES = [
    "Assets/VirtualHand/Scripts/Runtime/TaskController.cs",
    "Assets/VirtualHand/Scripts/Runtime/DemoUI.cs",
    "Assets/VirtualHand/Scripts/Runtime/ReplayController.cs",
    "Assets/VirtualHand/Scripts/Runtime/ReplaySource.cs",
    "Assets/VirtualHand/Scripts/Runtime/PoseLibrary.cs",
    "Assets/VirtualHand/Scripts/Runtime/ReplayUIBindings.cs",
    "Assets/VirtualHand/Scripts/Runtime/OrbitCamera.cs",
    "Assets/VirtualHand/Scripts/Editor/DemoSceneBuilder.cs",
    "Assets/VirtualHand/Scripts/Editor/CaptureRigPreview.cs",
    "Assets/VirtualHand/Scripts/Editor/SkinMaterialFactory.cs",
    "Assets/VirtualHand/Tests/RigAndTaskTests.cs",
    "Assets/VirtualHand/Tests/ContractTests.cs",
]


def strip_noncode(s):
    out = []
    i, n = 0, len(s)
    state = None  # None | 'str' | 'chr' | 'line' | 'block'
    while i < n:
        c = s[i]
        nxt = s[i + 1] if i + 1 < n else ""
        if state is None:
            if c == '"':
                state = 'str'
                out.append('""')
                i += 1
                continue
            if c == "'":
                state = 'chr'
                out.append("''")
                i += 1
                continue
            if c == '/' and nxt == '/':
                state = 'line'
                i += 2
                continue
            if c == '/' and nxt == '*':
                state = 'block'
                i += 2
                continue
            out.append(c)
            i += 1
        elif state == 'str':
            if c == '\\':
                i += 2
            elif c == '"':
                state = None
            i += 1
        elif state == 'chr':
            if c == '\\':
                i += 2
            elif c == "'":
                state = None
            i += 1
        elif state == 'line':
            if c == '\n':
                state = None
                out.append('\n')
            i += 1
        else:  # block comment
            if c == '*' and nxt == '/':
                state = None
                i += 2
            else:
                i += 1
    return ''.join(out)


ok = True
for p in FILES:
    s = open(p, encoding="utf-8-sig").read()
    code = strip_noncode(s)
    depth = 0
    first_neg = None
    for i, line in enumerate(code.split('\n'), 1):
        depth += line.count('{') - line.count('}')
        if depth < 0 and first_neg is None:
            first_neg = i
    o, c = code.count('{'), code.count('}')
    status = "OK " if o == c else f"MISMATCH (first negative at line {first_neg})"
    if o != c:
        ok = False
        print(f"{status} {p}: {{={o} }}={c}")
    else:
        print(f"OK  {p}")
sys.exit(0 if ok else 1)
