#!/usr/bin/env python3
"""Download and safely extract the selected Game04 avatar reference resources."""

from __future__ import annotations

import argparse
import hashlib
import http.client
import json
import ntpath
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import sys
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Iterable
from urllib.error import URLError
from urllib.request import Request, urlopen
import zipfile


PROJECT_ROOT = Path(__file__).resolve().parents[1]
RESOURCE_ROOT = PROJECT_ROOT / "ArtSource" / "_download" / "avatar-resources"
MANIFEST_NAME = "manifest.json"
CHUNK_SIZE = 1024 * 1024
NETWORK_TIMEOUT_SECONDS = 45
MAX_DOWNLOAD_ATTEMPTS = 3
ASSET_SUFFIXES = {".mhmat", ".mhclo", ".mhm"}
MANIFEST_HASH_SCOPE = (
    "SHA-256 of the locally stored ZIP bytes; this is a local identity record, "
    "not an upstream checksum verification."
)


@dataclass(frozen=True)
class Resource:
    resource_id: str
    title: str
    filename: str
    extraction_dir: str
    url: str
    license: str
    category: str


RESOURCES = (
    Resource(
        resource_id="makehuman_system_assets",
        title="MakeHuman System Assets CC0",
        filename="makehuman_system_assets_cc0.zip",
        extraction_dir="makehuman_system_assets_cc0",
        url=(
            "https://files.makehumancommunity.org/asset_packs/"
            "makehuman_system_assets/makehuman_system_assets_cc0.zip"
        ),
        license="CC0",
        category="MakeHuman system assets",
    ),
    Resource(
        resource_id="skins02",
        title="MakeHuman skins02 CC0",
        filename="skins02_cc0.zip",
        extraction_dir="skins02_cc0",
        url="https://files.makehumancommunity.org/asset_packs/skins02/skins02_cc0.zip",
        license="CC0",
        category="Skin textures",
    ),
    Resource(
        resource_id="mpfb2",
        title="MPFB2 v2.0.18 source",
        filename="mpfb2_v2.0.18.zip",
        extraction_dir="mpfb2_v2.0.18",
        url="https://github.com/makehumancommunity/mpfb2/archive/refs/tags/v2.0.18.zip",
        license="GPLv3 for MPFB code; bundled graphic assets CC0",
        category="MPFB2 source code",
    ),
)
RESOURCE_BY_ID = {resource.resource_id: resource for resource in RESOURCES}


class ArchiveSafetyError(ValueError):
    """Raised when an archive is corrupt or contains unsafe entries."""


class ManifestError(ValueError):
    """Raised when the local download manifest cannot be trusted."""


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while True:
            chunk = source.read(CHUNK_SIZE)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def _resource_entry_parts(info: zipfile.ZipInfo) -> tuple[tuple[str, ...], bool]:
    raw_name = info.filename
    is_directory = info.is_dir() or raw_name.endswith("/")

    if not raw_name or "\x00" in raw_name or "\\" in raw_name:
        raise ArchiveSafetyError(f"unsafe ZIP member name: {raw_name!r}")
    if raw_name.startswith("/") or PurePosixPath(raw_name).is_absolute():
        raise ArchiveSafetyError(f"absolute ZIP member path rejected: {raw_name!r}")
    drive, _ = ntpath.splitdrive(raw_name)
    if drive or ":" in raw_name:
        raise ArchiveSafetyError(f"drive or stream path rejected: {raw_name!r}")

    trimmed_name = raw_name[:-1] if is_directory and raw_name.endswith("/") else raw_name
    parts = tuple(trimmed_name.split("/"))
    if not parts or any(part in {"", ".", ".."} for part in parts):
        raise ArchiveSafetyError(f"traversal or empty ZIP path component: {raw_name!r}")

    reserved_names = {"CON", "PRN", "AUX", "NUL"}
    reserved_names.update(f"COM{index}" for index in range(1, 10))
    reserved_names.update(f"LPT{index}" for index in range(1, 10))
    for part in parts:
        if part.endswith((" ", ".")):
            raise ArchiveSafetyError(f"non-portable ZIP path component: {raw_name!r}")
        if part.split(".", 1)[0].upper() in reserved_names:
            raise ArchiveSafetyError(f"reserved ZIP path component: {raw_name!r}")

    mode = (info.external_attr >> 16) & 0xFFFF
    file_type = stat.S_IFMT(mode)
    if file_type == stat.S_IFLNK:
        raise ArchiveSafetyError(f"symbolic link rejected: {raw_name!r}")
    if file_type not in {0, stat.S_IFREG, stat.S_IFDIR}:
        raise ArchiveSafetyError(f"special ZIP entry rejected: {raw_name!r}")
    if file_type == stat.S_IFDIR and not is_directory:
        raise ArchiveSafetyError(f"directory entry has a file path: {raw_name!r}")
    if file_type == stat.S_IFREG and is_directory:
        raise ArchiveSafetyError(f"file entry has a directory path: {raw_name!r}")

    return parts, is_directory


def _validated_entries(
    infos: Iterable[zipfile.ZipInfo],
) -> list[tuple[zipfile.ZipInfo, tuple[str, ...], bool]]:
    entries: list[tuple[zipfile.ZipInfo, tuple[str, ...], bool]] = []
    explicit_paths: set[str] = set()
    node_kinds: dict[str, str] = {}

    for info in infos:
        parts, is_directory = _resource_entry_parts(info)
        normalized_path = "/".join(parts).casefold()
        if normalized_path in explicit_paths:
            raise ArchiveSafetyError(f"duplicate ZIP member path: {info.filename!r}")
        explicit_paths.add(normalized_path)

        for index in range(1, len(parts) + 1):
            node_path = "/".join(parts[:index]).casefold()
            expected_kind = "directory" if index < len(parts) or is_directory else "file"
            previous_kind = node_kinds.get(node_path)
            if previous_kind is not None and previous_kind != expected_kind:
                raise ArchiveSafetyError(
                    f"file/directory path collision in ZIP: {info.filename!r}"
                )
            node_kinds[node_path] = expected_kind

        entries.append((info, parts, is_directory))

    if not entries:
        raise ArchiveSafetyError("empty ZIP archive")
    return entries


def validate_archive(path: Path) -> list[zipfile.ZipInfo]:
    """Validate member paths, entry types, and CRCs without extracting files."""
    try:
        with zipfile.ZipFile(path, "r") as archive:
            infos = archive.infolist()
            _validated_entries(infos)
            try:
                damaged_member = archive.testzip()
            except (OSError, EOFError, RuntimeError, NotImplementedError, zipfile.BadZipFile) as exc:
                raise ArchiveSafetyError(f"corrupt ZIP archive {path}: {exc}") from exc
            if damaged_member is not None:
                raise ArchiveSafetyError(
                    f"corrupt ZIP archive {path}: CRC check failed for {damaged_member!r}"
                )
            return infos
    except ArchiveSafetyError:
        raise
    except (OSError, EOFError, RuntimeError, NotImplementedError, zipfile.BadZipFile) as exc:
        raise ArchiveSafetyError(f"corrupt or unreadable ZIP archive {path}: {exc}") from exc


def _check_existing_target(root: Path, parts: tuple[str, ...], is_directory: bool) -> None:
    root_resolved = root.resolve(strict=True)
    target = root.joinpath(*parts)
    try:
        resolved_target = target.resolve(strict=False)
    except (OSError, RuntimeError) as exc:
        raise ArchiveSafetyError(f"cannot safely resolve extraction path {target}: {exc}") from exc
    if not resolved_target.is_relative_to(root_resolved):
        raise ArchiveSafetyError(f"extraction path escapes destination: {target}")

    current = root
    for index, part in enumerate(parts):
        current = current / part
        if current.is_symlink():
            raise ArchiveSafetyError(f"existing symbolic link in extraction path: {current}")
        if not current.exists():
            continue
        is_final = index == len(parts) - 1
        expects_directory = not is_final or is_directory
        if expects_directory and not current.is_dir():
            raise ArchiveSafetyError(f"non-directory blocks extraction path: {current}")
        if is_final and not is_directory and current.is_dir():
            raise ArchiveSafetyError(f"directory blocks extracted file: {current}")


def extract_validated_archive(
    archive_path: Path,
    destination: Path,
    infos: Iterable[zipfile.ZipInfo],
) -> list[str]:
    """Extract previously validated ZIP entries without following local symlinks."""
    entries = _validated_entries(infos)
    if destination.is_symlink():
        raise ArchiveSafetyError(f"symbolic link extraction directory rejected: {destination}")
    destination.mkdir(parents=True, exist_ok=True)
    if not destination.is_dir():
        raise ArchiveSafetyError(f"extraction destination is not a directory: {destination}")
    root = destination.resolve(strict=True)

    for _, parts, is_directory in entries:
        _check_existing_target(root, parts, is_directory)

    try:
        with zipfile.ZipFile(archive_path, "r") as archive:
            for info, parts, is_directory in entries:
                target = root.joinpath(*parts)
                _check_existing_target(root, parts, is_directory)
                if is_directory:
                    target.mkdir(parents=True, exist_ok=True)
                    continue
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(info, "r") as source, target.open("wb") as output:
                    shutil.copyfileobj(source, output, length=CHUNK_SIZE)
    except ArchiveSafetyError:
        raise
    except (EOFError, RuntimeError, NotImplementedError, zipfile.BadZipFile) as exc:
        raise ArchiveSafetyError(f"corrupt ZIP archive {archive_path}: {exc}") from exc

    return list_asset_files(root)


def safe_extract_zip(archive_path: Path, destination: Path) -> list[str]:
    """Validate and extract a ZIP, returning its MakeHuman asset candidates."""
    infos = validate_archive(archive_path)
    return extract_validated_archive(archive_path, destination, infos)


def list_asset_files(directory: Path) -> list[str]:
    """List .mhmat, .mhclo, and .mhm files below a directory, without following links."""
    if directory.is_symlink() or not directory.is_dir():
        return []

    candidates: list[str] = []
    for current, subdirectories, filenames in os.walk(directory, followlinks=False):
        current_path = Path(current)
        subdirectories[:] = [
            name
            for name in subdirectories
            if not (current_path / name).is_symlink()
        ]
        for filename in filenames:
            path = current_path / filename
            if path.is_symlink() or path.suffix.lower() not in ASSET_SUFFIXES:
                continue
            candidates.append(path.relative_to(directory).as_posix())
    return sorted(candidates, key=str.casefold)


def _ensure_resource_root(path: Path) -> None:
    project_root = PROJECT_ROOT
    guarded_paths = (
        project_root / "ArtSource",
        project_root / "ArtSource" / "_download",
        path,
    )
    for guarded_path in guarded_paths:
        if guarded_path.is_symlink():
            raise OSError(f"refusing to use symbolic link in resource output path: {guarded_path}")
        if guarded_path.exists() and not guarded_path.is_dir():
            raise OSError(f"resource output path component is not a directory: {guarded_path}")
    path.mkdir(parents=True, exist_ok=True)


def _new_manifest() -> dict[str, object]:
    return {
        "format_version": 1,
        "encoding": "UTF-8",
        "line_endings": "LF",
        "hash_scope": MANIFEST_HASH_SCOPE,
        "resources": {},
    }


def load_manifest(path: Path) -> dict[str, object]:
    if path.is_symlink():
        raise ManifestError(f"symbolic link manifest rejected: {path}")
    if not path.exists():
        return _new_manifest()
    try:
        with path.open("r", encoding="utf-8") as source:
            value = json.load(source)
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ManifestError(f"cannot read manifest {path}: {exc}") from exc

    if not isinstance(value, dict) or value.get("format_version") != 1:
        raise ManifestError(f"unsupported or malformed manifest: {path}")
    resources = value.get("resources")
    if not isinstance(resources, dict):
        raise ManifestError(f"manifest resources must be an object: {path}")
    for resource_id, record in resources.items():
        if not isinstance(resource_id, str) or not isinstance(record, dict):
            raise ManifestError(f"malformed resource record in manifest: {path}")
    value["encoding"] = "UTF-8"
    value["line_endings"] = "LF"
    value["hash_scope"] = MANIFEST_HASH_SCOPE
    return value


def write_manifest(path: Path, manifest: dict[str, object]) -> None:
    if path.is_symlink():
        raise ManifestError(f"symbolic link manifest rejected: {path}")
    temporary_path = path.with_name(path.name + ".partial")
    if temporary_path.is_symlink():
        raise ManifestError(f"symbolic link manifest temporary file rejected: {temporary_path}")
    serialized = json.dumps(manifest, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    try:
        with temporary_path.open("w", encoding="utf-8", newline="\n") as output:
            output.write(serialized)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary_path, path)
    except OSError as exc:
        raise ManifestError(f"cannot write manifest {path}: {exc}") from exc


def _record_matches_resource(
    record: object, resource: Resource, local_sha256: str
) -> bool:
    if not isinstance(record, dict):
        return False
    return (
        record.get("filename") == resource.filename
        and record.get("url") == resource.url
        and record.get("license") == resource.license
        and record.get("category") == resource.category
        and record.get("sha256_local") == local_sha256
    )


def _utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def download_to_partial(url: str, partial_path: Path) -> str:
    """Stream an HTTPS resource to a .partial file and return its local SHA-256."""
    if not url.lower().startswith("https://"):
        raise ValueError(f"refusing non-HTTPS download URL: {url}")
    if partial_path.is_symlink():
        raise OSError(f"symbolic link download temporary file rejected: {partial_path}")

    last_error: Exception | None = None
    for attempt in range(1, MAX_DOWNLOAD_ATTEMPTS + 1):
        digest = hashlib.sha256()
        print(f"  Download attempt {attempt}/{MAX_DOWNLOAD_ATTEMPTS}: {url}")
        try:
            request = Request(url, headers={"User-Agent": "Game04-avatar-resource-setup/1.0"})
            with urlopen(request, timeout=NETWORK_TIMEOUT_SECONDS) as response:
                if not response.geturl().lower().startswith("https://"):
                    raise URLError("refusing a redirect that leaves HTTPS")
                status = getattr(response, "status", 200)
                if status is not None and not 200 <= status < 300:
                    raise URLError(f"unexpected HTTP status {status}")
                with partial_path.open("wb") as output:
                    while True:
                        chunk = response.read(CHUNK_SIZE)
                        if not chunk:
                            break
                        output.write(chunk)
                        digest.update(chunk)
                    output.flush()
                    os.fsync(output.fileno())
            return digest.hexdigest()
        except (URLError, TimeoutError, http.client.HTTPException, OSError) as exc:
            last_error = exc
            print(f"  Attempt failed: {exc}", file=sys.stderr)
            if attempt < MAX_DOWNLOAD_ATTEMPTS:
                time.sleep(attempt)

    raise OSError(
        f"download failed after {MAX_DOWNLOAD_ATTEMPTS} attempts; "
        f"partial file retained at {partial_path}: {last_error}"
    )


def _print_resource(resource: Resource, extraction_path: Path) -> None:
    print(f"{resource.title} [{resource.resource_id}]")
    print(f"  Source URL: {resource.url}")
    print(f"  License: {resource.license}")
    print(f"  Category: {resource.category}")
    print(f"  Extraction directory: {extraction_path}")
    candidates = list_asset_files(extraction_path)
    print("  .mhmat / .mhclo / .mhm files for parent selection:")
    if candidates:
        for candidate in candidates:
            print(f"    - {candidate}")
    else:
        print("    (none currently extracted)")


def _list_catalog(resource_root: Path) -> None:
    print("Avatar resources for the Game04 prototype (listing does not use the network).")
    print(f"Manifest hash scope: {MANIFEST_HASH_SCOPE}")
    for resource in RESOURCES:
        print()
        _print_resource(resource, resource_root / resource.extraction_dir)


def _parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Download selected official avatar resources into the Game04 prototype's "
            "ArtSource/_download/avatar-resources directory."
        )
    )
    parser.add_argument(
        "--only",
        nargs="+",
        choices=tuple(RESOURCE_BY_ID),
        metavar="RESOURCE",
        help="download one or more resource IDs (default: list resources; no network)",
    )
    parser.add_argument(
        "--list",
        action="store_true",
        help="list resource metadata and locally extracted candidates without networking",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = _parse_args(argv)
    if args.list or not args.only:
        _list_catalog(RESOURCE_ROOT)
        return 0

    try:
        _ensure_resource_root(RESOURCE_ROOT)
        manifest_path = RESOURCE_ROOT / MANIFEST_NAME
        manifest = load_manifest(manifest_path)
    except (OSError, ManifestError) as exc:
        print(f"Setup failed: {exc}", file=sys.stderr)
        return 1

    manifest_resources = manifest["resources"]
    assert isinstance(manifest_resources, dict)
    failures = 0

    for resource_id in args.only:
        resource = RESOURCE_BY_ID[resource_id]
        archive_path = RESOURCE_ROOT / resource.filename
        partial_path = archive_path.with_name(archive_path.name + ".partial")
        extraction_path = RESOURCE_ROOT / resource.extraction_dir
        print()
        print(f"Preparing {resource.title}")
        print(f"  Source URL: {resource.url}")
        print(f"  License: {resource.license}")
        print(f"  Category: {resource.category}")

        try:
            if archive_path.is_symlink():
                raise OSError(f"symbolic link archive rejected: {archive_path}")
            old_record = manifest_resources.get(resource.resource_id)
            reused = False

            if archive_path.exists() and not archive_path.is_file():
                raise OSError(f"archive path is not a regular file: {archive_path}")

            if archive_path.is_file():
                current_sha256 = sha256_file(archive_path)
                if _record_matches_resource(old_record, resource, current_sha256):
                    local_sha256 = current_sha256
                    infos = validate_archive(archive_path)
                    reused = True
                    print("  Reusing ZIP: its bytes match the local manifest record.")
                else:
                    print(
                        "  Existing ZIP is unrecorded or its local hash does not match; "
                        "downloading a fresh copy."
                    )
                    local_sha256 = download_to_partial(resource.url, partial_path)
                    infos = validate_archive(partial_path)
                    os.replace(partial_path, archive_path)
            else:
                local_sha256 = download_to_partial(resource.url, partial_path)
                infos = validate_archive(partial_path)
                os.replace(partial_path, archive_path)

            asset_files = extract_validated_archive(archive_path, extraction_path, infos)
            previous_record = old_record if isinstance(old_record, dict) else {}
            manifest_resources[resource.resource_id] = {
                "filename": resource.filename,
                "url": resource.url,
                "license": resource.license,
                "category": resource.category,
                "sha256_local": local_sha256,
                "size_bytes": archive_path.stat().st_size,
                "downloaded_at_utc": (
                    previous_record.get("downloaded_at_utc", _utc_now())
                    if reused
                    else _utc_now()
                ),
                "asset_files": asset_files,
            }
            write_manifest(manifest_path, manifest)
            print(f"  ZIP: {archive_path}")
            print("  SHA-256: recorded local download identity; not an upstream checksum.")
            _print_resource(resource, extraction_path)
        except (ArchiveSafetyError, ManifestError, OSError, URLError, ValueError) as exc:
            failures += 1
            print(f"  FAILED: {exc}", file=sys.stderr)

    if failures:
        print(f"\nCompleted with {failures} resource failure(s).", file=sys.stderr)
        return 1
    print(f"\nSelected resources are ready under {RESOURCE_ROOT}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
