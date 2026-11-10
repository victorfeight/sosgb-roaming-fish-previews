#!/usr/bin/env python3
"""Extract the five actual-fish shadow meshes into a small writable format.

This is a local personal-mod build step. It reads the installed Grand Bazaar
Addressables bundles, never modifies them, and writes derived mesh payloads next
to RoamingFishPreviews.dll. Non-fish shadow classes (trash, bones, coins) are
intentionally excluded.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler


DEFAULT_GAME = Path(
    "/mnt/c/Program Files (x86)/Steam/steamapps/common/"
    "STORY OF SEASONS Grand Bazaar"
)
DEFAULT_OUTPUT = Path("BepInEx/plugins/RoamingFishPreviews/meshes")
MODELS = {
    "anml_wld_010_00": "3a1e7acd618f683ea475308417d7a2ed.bundle",
    "anml_wld_010_01": "b08399d46bd4423329d6f6edad2e6e39.bundle",
    "anml_wld_010_02": "5eb28ef40e5f33ed0b063c8e469568a3.bundle",
    "anml_wld_020_00": "cac0b0634259d2e1b6ac204b0e63da9a.bundle",
    "anml_wld_030_00": "ef17793b36762aeff477b17d5a6764e0.bundle",
}


def load_mesh(bundle_path: Path):
    environment = UnityPy.load(str(bundle_path))
    meshes = [obj.read() for obj in environment.objects if obj.type.name == "Mesh"]
    if not meshes:
        raise RuntimeError(f"no Mesh object in {bundle_path.name}")
    mesh = next((item for item in meshes if item.m_Name.startswith("fish_body")), meshes[0])
    decoded = MeshHandler(mesh)
    decoded.process()
    positions = decoded.m_Vertices or []
    normals = decoded.m_Normals or []
    uvs = decoded.m_UV0 or []
    vertices = []
    for index, position in enumerate(positions):
        normal = normals[index] if index < len(normals) else (0.0, 1.0, 0.0)
        uv = uvs[index] if index < len(uvs) else (0.0, 0.0)
        vertices.append((*position[:3], *normal[:3], *uv[:2]))
    indices = [
        vertex_index
        for submesh in decoded.get_triangles()
        for triangle in submesh
        for vertex_index in triangle
    ]
    if not vertices or not indices:
        raise RuntimeError("decoded mesh contained no usable triangles")
    return mesh, vertices, indices


def write_payload(path: Path, vertices, indices):
    with path.open("wb") as stream:
        stream.write(b"RFM1")
        stream.write(struct.pack("<II", len(vertices), len(indices)))
        for vertex in vertices:
            stream.write(struct.pack("<8f", *vertex))
        stream.write(struct.pack(f"<{len(indices)}I", *indices))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--game", type=Path, default=DEFAULT_GAME)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    bundle_root = (
        args.game
        / "SOSGrandBazaar_Data/StreamingAssets/aa/StandaloneWindows64"
    )
    args.output.mkdir(parents=True, exist_ok=True)
    manifest = {
        "format": "RFM1",
        "source": str(args.game),
        "models": {},
    }
    for model_id, bundle_name in MODELS.items():
        bundle_path = bundle_root / bundle_name
        if not bundle_path.is_file():
            raise FileNotFoundError(bundle_path)
        mesh, vertices, indices = load_mesh(bundle_path)
        output_path = args.output / f"{model_id}.rfmesh"
        write_payload(output_path, vertices, indices)

        xs = [vertex[0] for vertex in vertices]
        ys = [vertex[1] for vertex in vertices]
        zs = [vertex[2] for vertex in vertices]
        manifest["models"][model_id] = {
            "bundle": bundle_name,
            "bundle_sha256": hashlib.sha256(bundle_path.read_bytes()).hexdigest(),
            "source_readable": bool(mesh.m_IsReadable),
            "vertices": len(vertices),
            "triangles": len(indices) // 3,
            "bounds": {
                "x": max(xs) - min(xs),
                "y": max(ys) - min(ys),
                "z": max(zs) - min(zs),
            },
            "payload_sha256": hashlib.sha256(output_path.read_bytes()).hexdigest(),
        }
        print(
            f"{model_id}: {len(vertices)} vertices, {len(indices) // 3} triangles, "
            f"source_readable={bool(mesh.m_IsReadable)}"
        )

    manifest_path = args.output / "manifest.json"
    manifest_path.write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"Wrote {len(MODELS)} writable fish meshes to {args.output}")


if __name__ == "__main__":
    main()
