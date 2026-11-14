"""Export the open Three Roads Blender scene to the map's Unity Mesh assets.

blender -b SourceArt/Map_01_ThreeRoads/Map_01_ThreeRoads.blend \
    --python SourceArt/Map_01_ThreeRoads/export_meshes.py

The exporter replaces mesh contents only. Keep the corresponding Unity .meta
files and update the scene's spawn transforms if you change the spawn shelves.
"""

from array import array
from pathlib import Path
import sys

import bpy

ROOT = Path(bpy.data.filepath).resolve().parents[2]
OUTPUT = ROOT / "Assets/MadMachines/Maps/Map_01_ThreeRoads"
TERRAIN_PARTS = ("Meadow", "Ridge", "Pass", "Lowland", "Crossings")
DECOR_PARTS = ("Boulders", "Trunks", "Foliage", "Ridge pinnacles", "Lowland cliffs",
               "Fortress masonry", "Fortress accents")


def unity_vector(vector):
    return vector.x, vector.z, vector.y


def little_endian_hex(values):
    if sys.byteorder == "big":
        values = values[:]
        values.byteswap()
    return values.tobytes().hex()


def export(name, objects, material_index=None, collision_only=False):
    positions = []
    vertices = array("f")
    indices = []
    with_uv = material_index is not None
    for obj in objects:
        mesh = obj.data
        mesh.calc_loop_triangles()
        if with_uv:
            mesh.calc_tangents()
        uv = mesh.uv_layers.active.data if with_uv else None
        normal_transform = obj.matrix_world.to_3x3().inverted().transposed()
        remap = {}
        for tri in mesh.loop_triangles:
            if material_index is not None and tri.material_index != material_index:
                continue
            # The X,Z,Y coordinate conversion flips triangle winding.
            for loop_id in (tri.loops[0], tri.loops[2], tri.loops[1]):
                loop = mesh.loops[loop_id]
                attributes = ()
                if not collision_only:
                    normal = (normal_transform @ mesh.corner_normals[loop_id].vector).normalized()
                    attributes = unity_vector(normal)
                    if with_uv:
                        tangent = (obj.matrix_world.to_3x3() @ loop.tangent).normalized()
                        attributes += (*unity_vector(tangent), -loop.bitangent_sign, *uv[loop_id].uv)
                key = (loop.vertex_index, attributes)
                index = remap.get(key)
                if index is None:
                    index = len(positions)
                    remap[key] = index
                    position = unity_vector(obj.matrix_world @ mesh.vertices[loop.vertex_index].co)
                    positions.append(position)
                    vertices.extend(position + attributes)
                indices.append(index)
        if with_uv:
            mesh.free_tangents()
    write_mesh(name, positions, vertices, indices, with_uv, collision_only)


def yaml_vector(values):
    return "{" + ", ".join(f"{axis}: {value:.8g}" for axis, value in zip("xyz", values)) + "}"


def write_mesh(name, positions, vertices, indices, with_uv, collision_only):
    index_format = int(len(positions) > 65535)
    index_buffer = array("I" if index_format else "H", indices)
    low = [min(axis) for axis in zip(*positions)]
    high = [max(axis) for axis in zip(*positions)]
    center = yaml_vector((a + b) / 2 for a, b in zip(low, high))
    extent = yaml_vector((b - a) / 2 for a, b in zip(low, high))
    channels = [(0, 3), (12, 3) if not collision_only else (0, 0)]
    channels += [(24, 4), (0, 0), (40, 2)] if with_uv else [(0, 0)] * 3
    channels += [(0, 0)] * 9
    channel_lines = "".join(
        f"    - stream: 0\n      offset: {offset}\n      format: 0\n      dimension: {dimension}\n"
        for offset, dimension in channels
    )
    packed = ("Vertices", "UV", "Normals", "Tangents", "Weights", "NormalSigns",
              "TangentSigns", "FloatColors", "BoneIndices", "Triangles")
    packed_lines = "".join(
        f"    m_{part}:\n      m_NumItems: 0\n      m_Range: 0\n      m_Start: 0\n      m_Data:\n      m_BitSize: 0\n"
        for part in packed
    )
    (OUTPUT / f"{name}.asset").write_text(f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!43 &4300000
Mesh:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  serializedVersion: 12
  m_SubMeshes:
  - serializedVersion: 2
    firstByte: 0
    indexCount: {len(indices)}
    topology: 0
    baseVertex: 0
    firstVertex: 0
    vertexCount: {len(positions)}
    localAABB:
      m_Center: {center}
      m_Extent: {extent}
  m_Shapes:
    vertices: []
    shapes: []
    channels: []
    fullWeights: []
  m_BindPose: []
  m_BoneNameHashes:
  m_RootBoneNameHash: 0
  m_BonesAABB: []
  m_VariableBoneCountWeights:
    m_Data:
  m_MeshCompression: 0
  m_IsReadable: 1
  m_KeepVertices: 0
  m_KeepIndices: 0
  m_IndexFormat: {index_format}
  m_IndexBuffer: {little_endian_hex(index_buffer)}
  m_VertexData:
    serializedVersion: 3
    m_VertexCount: {len(positions)}
    m_Channels:
{channel_lines}    m_DataSize: {len(vertices) * vertices.itemsize}
    _typelessdata: {little_endian_hex(vertices)}
  m_CompressedMesh:
{packed_lines}    m_UVInfo: 0
  m_LocalAABB:
    m_Center: {center}
    m_Extent: {extent}
  m_CookingOptions: 30
  m_PreBakeConvexCollisionMesh: 0
  m_PreBakeTriangleCollisionMesh: 0
  m_BakedConvexCollisionMesh:
  m_BakedTriangleCollisionMesh:
  'm_MeshMetrics[0]': 1
  'm_MeshMetrics[1]': 1
  m_MeshOptimizationFlags: 1
  m_StreamData:
    serializedVersion: 2
    offset: 0
    size: 0
    path:
  m_MeshLodInfo:
    serializedVersion: 2
    m_LodSelectionCurve:
      serializedVersion: 1
      m_LodSlope: 0
      m_LodBias: 0
    m_NumLevels: 1
    m_SubMeshes:
    - serializedVersion: 2
      m_Levels:
      - serializedVersion: 1
        m_IndexStart: 0
        m_IndexCount: 0
""", encoding="utf-8")


def main():
    bpy.context.view_layer.update()
    terrain = bpy.data.objects["Terrain"]
    export("Ground collision", [terrain], collision_only=True)
    for index, name in enumerate(TERRAIN_PARTS):
        export(name, [terrain], material_index=index)
    for name in DECOR_PARTS:
        export(name, bpy.data.collections[name].objects)
    export("Fortress collision", bpy.data.collections["Fortress collision"].objects, collision_only=True)


if __name__ == "__main__":
    main()
