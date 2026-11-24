# CHECKPOINT 1 — 官方眼睛资源获取（2026-11-20 批次 3）

实际模型：glm-5.3-flash / 最高档；ZCode 本机会话。活动起 20:54:51 +0800（date 取证）。

## 阶段 1 执行记录（全部通过，无门槛失败）

| 项 | 值 |
|---|---|
| URL（唯一，官方页所列主源） | `https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip` |
| HTTP 状态 | 200 OK（headers.txt �证：Server openresty, Last-Modified 2024-04-14, ETag 10bbb7ea-6160e7c0d7996） |
| Content-Length | 280,737,770 B |
| 实收字节 | 280,737,770 B（**严格相等**） |
| curl 退出码 | 0（`curl_summary.txt`：EXIT=0，END 20:55:40，耗时 49s，无重定向 num_redirects=0 记录于 headers 之外摘要） |
| ZIP 全成员 CRC | **517 名成员 testzip ALL_PASS** |
| ZIP SHA-256 | `b542127a8e25547c7c29c19f2d1d2adb9a664c80396ecd694095dbc8028a0107` |
| 隔离路径 | `ArtSource/_download/avatar-resources/system-assets-cc0-fresh-20261120/`（全新目录；旧 .partial 未访问/未覆盖/未续传/未删除） |
| 解压策略 | 仅隔离解压 `eyes/` 子树（34 文件） |

## 眼睛依赖链核实（磁盘读取）

- `eyes/low-poly/low-poly.mhclo`（SHA `68582e28…4d8`）：头注明示 CC0（2020-09，Data Collection AB / Joel Palmius / Jonas Hauquier），`basemesh hm08`，`obj_file low-poly.obj`
- `low-poly.obj`（SHA `783fe1e6…3de`）：182 v/f 行，头部同 CC0 声明，无 mtllib 外部引用
- `material ../materials/brown.mhmat` → 已在 `eyes/materials/` 解压（brown.mhmat SHA `4abad93c…424b` + `brown_eye.png` SHA `4659691c…be24b` 等 10 色材质全套）
- 全 34 文件 SHA 清单：`system-assets-cc0-fresh-20261120/eyes_sha256.txt`

## 许可来源

包页 https://static.makehumancommunity.org/assets/assetpacks/makehuman_system_assets.html （author makehuman_system / CC0）；文件头内嵌 CC0 声明与上述页面一致。

## 下一步（阶段 2）

补丁脚本 `generated/neutral_adult/build_mpfb_avatar_patched.py` 就绪（仅两处 `bpy.mathutils` API 修正 + parents[4] 路径修正；原脚本未动）。显式七参数构建即将执行，输出锁定本目录。
