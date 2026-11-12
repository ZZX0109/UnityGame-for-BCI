"""独立 BFS 校验器：检查所有关卡地图可解并输出最少推动数。

用法：python Tools/validate_levels.py
不依赖 Unity，用于在加入新关卡前做一次可解性回归。
"""
from collections import deque
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
LEVELS = ROOT / "Assets" / "FirstPersonSokoban" / "Resources" / "Levels"
DIRECTIONS = [(-1, 0), (1, 0), (0, -1), (0, 1)]
MAX_STATES = 200000


def parse(path):
    rows = [line.rstrip("\r\n") for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]
    width = len(rows[0])
    assert all(len(r) == width for r in rows), f"{path.name}: 行宽不一致"
    walls, boxes, goals, players = set(), [], set(), []
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            p = (x, y)
            if ch == "#":
                walls.add(p)
            elif ch == "G":
                goals.add(p)
            elif ch == "B":
                boxes.append(p)
            elif ch == "P":
                players.append(p)
    assert len(players) == 1, f"{path.name}: 玩家起点必须唯一"
    assert len(boxes) == len(goals) and boxes, f"{path.name}: 箱子/目标数量不匹配"
    return walls, width, len(rows), tuple(sorted(boxes)), frozenset(goals), players[0]


def reachable(player, boxes, walls, width, height):
    occupied = set(boxes)
    seen = {player}
    queue = deque([player])
    while queue:
        x, y = queue.popleft()
        for dx, dy in DIRECTIONS:
            nxt = (x + dx, y + dy)
            if not (0 <= nxt[0] < width and 0 <= nxt[1] < height):
                continue
            if nxt in walls or nxt in occupied or nxt in seen:
                continue
            seen.add(nxt)
            queue.append(nxt)
    return seen


def solve(path):
    walls, width, height, boxes, goals, player = parse(path)
    start = (player, boxes)
    visited = {start}
    queue = deque([(player, boxes, 0)])
    while queue:
        p, current, depth = queue.popleft()
        if set(current) == goals:
            return depth, len(visited)
        free = reachable(p, current, walls, width, height)
        for box in current:
            for dx, dy in DIRECTIONS:
                stand = (box[0] - dx, box[1] - dy)
                dest = (box[0] + dx, box[1] + dy)
                if stand not in free or dest in current or dest in walls:
                    continue
                if not (0 <= dest[0] < width and 0 <= dest[1] < height):
                    continue
                nxt = tuple(sorted([b for b in current if b != box] + [dest]))
                state = (box, nxt)
                if state in visited:
                    continue
                visited.add(state)
                if len(visited) > MAX_STATES:
                    return None, len(visited)
                queue.append((box, nxt, depth + 1))
    return None, len(visited)


def main():
    ok = True
    for path in sorted(LEVELS.glob("Level*.txt")):
        pushes, states = solve(path)
        if pushes is None:
            print(f"[FAIL] {path.name}: 无解或超出 {MAX_STATES} 状态 (探索 {states})")
            ok = False
        else:
            print(f"[OK]   {path.name}: 最少 {pushes} 推，探索 {states} 状态")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
