#!/usr/bin/env python3
"""Build SecondCursor.unitypackage from Assets/SecondCursor.

The result imports into any Unity 6 project by double-clicking it while the project is open
(or Assets > Import Package > Custom Package...).

GUIDs are derived from each asset path, so importing a newer package over an older one updates the
same assets instead of creating duplicates. The archive is byte-for-byte reproducible (fixed
timestamps, sorted entries), so unchanged content produces an unchanged file.

Usage: python3 make_unitypackage.py [output path]
"""
import gzip
import hashlib
import io
import os
import sys
import tarfile

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))
ASSET_ROOT = os.path.join(PROJECT, "Assets", "SecondCursor")
DEFAULT_OUT = os.path.join(PROJECT, "SecondCursor.unitypackage")
GUID_SALT = "second-cursor/"


def guid_for(asset_path: str) -> str:
    return hashlib.md5((GUID_SALT + asset_path).encode("utf-8")).hexdigest()


def importer_block(asset_path: str, is_dir: bool) -> str:
    if is_dir:
        return ("folderAsset: yes\n"
                "DefaultImporter:\n"
                "  externalObjects: {}\n"
                "  userData: \n"
                "  assetBundleName: \n"
                "  assetBundleVariant: \n")
    ext = os.path.splitext(asset_path)[1].lower()
    if ext == ".cs":
        return ("MonoImporter:\n"
                "  externalObjects: {}\n"
                "  serializedVersion: 2\n"
                "  defaultReferences: []\n"
                "  executionOrder: 0\n"
                "  icon: {instanceID: 0}\n"
                "  userData: \n"
                "  assetBundleName: \n"
                "  assetBundleVariant: \n")
    if ext == ".asmdef":
        return ("AssemblyDefinitionImporter:\n"
                "  externalObjects: {}\n"
                "  userData: \n"
                "  assetBundleName: \n"
                "  assetBundleVariant: \n")
    if ext == ".shader":
        return ("ShaderImporter:\n"
                "  externalObjects: {}\n"
                "  defaultTextures: []\n"
                "  nonModifiableTextures: []\n"
                "  userData: \n"
                "  assetBundleName: \n"
                "  assetBundleVariant: \n")
    if ext in (".json", ".md", ".txt", ".bytes", ".csv", ".xml", ".yaml"):
        return ("TextScriptImporter:\n"
                "  externalObjects: {}\n"
                "  userData: \n"
                "  assetBundleName: \n"
                "  assetBundleVariant: \n")
    return ("DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n")


def meta_for(asset_path: str, is_dir: bool) -> bytes:
    text = "fileFormatVersion: 2\nguid: " + guid_for(asset_path) + "\n" + importer_block(asset_path, is_dir)
    return text.encode("utf-8")


def collect():
    """Yield (asset_path, absolute_path, is_dir) for the root folder and everything under it."""
    base = os.path.dirname(os.path.dirname(ASSET_ROOT))  # the project folder containing Assets/
    entries = [("Assets/SecondCursor", ASSET_ROOT, True)]
    for dirpath, dirnames, filenames in os.walk(ASSET_ROOT):
        dirnames[:] = sorted(d for d in dirnames if not d.startswith("."))
        for d in dirnames:
            full = os.path.join(dirpath, d)
            entries.append((os.path.relpath(full, base).replace(os.sep, "/"), full, True))
        for f in sorted(filenames):
            if f.startswith(".") or f.endswith(".meta"):
                continue
            full = os.path.join(dirpath, f)
            entries.append((os.path.relpath(full, base).replace(os.sep, "/"), full, False))
    return sorted(entries, key=lambda e: e[0])


def add_bytes(tar: tarfile.TarFile, name: str, data: bytes):
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mtime = 0
    info.mode = 0o644
    info.uid = info.gid = 0
    info.uname = info.gname = ""
    tar.addfile(info, io.BytesIO(data))


def add_dir(tar: tarfile.TarFile, name: str):
    info = tarfile.TarInfo(name)
    info.type = tarfile.DIRTYPE
    info.mtime = 0
    info.mode = 0o755
    info.uid = info.gid = 0
    info.uname = info.gname = ""
    tar.addfile(info)


def build(out_path: str) -> int:
    entries = collect()
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.USTAR_FORMAT) as tar:
        for asset_path, full, is_dir in entries:
            guid = guid_for(asset_path)
            add_dir(tar, guid)
            if not is_dir:
                with open(full, "rb") as fh:
                    add_bytes(tar, guid + "/asset", fh.read())
            add_bytes(tar, guid + "/asset.meta", meta_for(asset_path, is_dir))
            add_bytes(tar, guid + "/pathname", asset_path.encode("utf-8"))
    with open(out_path, "wb") as out:
        with gzip.GzipFile(filename="", mode="wb", fileobj=out, mtime=0, compresslevel=9) as gz:
            gz.write(raw.getvalue())
    return len(entries)


def main():
    out_path = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_OUT
    if not os.path.isdir(ASSET_ROOT):
        sys.exit("Asset folder not found: " + ASSET_ROOT)
    count = build(out_path)
    print("Wrote %s (%d assets, %.1f KB)" % (out_path, count, os.path.getsize(out_path) / 1024.0))


if __name__ == "__main__":
    main()
