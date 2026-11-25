"""Compile ARTIST/UI/Vector SVG fills to the uGUI silhouette catalog."""

import json
import math
import struct
from pathlib import Path
from tempfile import TemporaryDirectory

from shapely import constrained_delaunay_triangles, make_valid, union_all
from shapely.geometry import GeometryCollection, Polygon
from svgelements import Close, Line, Move, Path as SvgPath, SVG, Shape


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ARTIST/UI/Vector"
OUTPUT = ROOT / "Assets/Resources/UI/VectorUiCatalog.asset"
CATALOG_SCRIPT = "308808d678af4ce4af20fa8d338cd7fe"
BUILTIN = "0000000000000000f000000000000000"


def polygons(geometry):
    if geometry.is_empty:
        return
    if geometry.geom_type == "Polygon":
        yield geometry
    elif hasattr(geometry, "geoms"):
        for part in geometry.geoms:
            yield from polygons(part)


def shape_geometry(element):
    rings = []
    for subpath in SvgPath(element).as_subpaths():
        points = []
        for segment in subpath:
            if isinstance(segment, Move):
                points.append((segment.end.x, segment.end.y))
                continue
            if isinstance(segment, (Line, Close)):
                count = 1
            else:
                count = max(4, math.ceil(segment.length() / 1.5))
            for index in range(1, count + 1):
                point = segment.point(index / count)
                points.append((point.x, point.y))

        if len(set(points)) >= 3:
            ring = Polygon(points)
            rings.append((make_valid(ring), 1 if ring.exterior.is_ccw else -1))

    if element.values.get("fill-rule") == "evenodd":
        filled = GeometryCollection()
        for ring, _ in rings:
            filled = filled.symmetric_difference(ring)
        return filled

    # SVG nonzero winding. Partition overlapping loops before choosing their fill.
    from shapely.ops import polygonize

    parts = polygonize(union_all([polygon.boundary for ring, _ in rings for polygon in polygons(ring)]))
    visible = []
    for part in parts:
        sample = part.representative_point()
        if sum(sign for ring, sign in rings if ring.covers(sample)) != 0:
            visible.append(part)
    return union_all(visible)


def read_svg(path):
    svg = SVG.parse(str(path), reify=True)
    if svg.width <= 0 or svg.height <= 0:
        raise ValueError(f"{path.name} must define a positive SVG size")

    layers = {}
    for element in svg.elements():
        if not isinstance(element, Shape) or element.fill is None or element.fill.value is None:
            continue
        color = (element.fill.red, element.fill.green, element.fill.blue, element.fill.alpha)
        if color[3] == 0:
            continue
        filled = shape_geometry(element)
        # Flatten SVG paint order so two translucent pieces cannot cover the same pixel.
        layers = {key: prior.difference(filled) for key, prior in layers.items()}
        layers[color] = layers.get(color, GeometryCollection()).union(filled)

    layers = {color: shape for color, shape in layers.items() if shape.area > 0}
    if not 1 <= len(layers) <= 2:
        raise ValueError(f"{path.name} must have one or two visible flat fills")
    return float(svg.width), float(svg.height), layers


def triangulate(shape, width, height):
    vertices = {}
    indices = []
    for polygon in polygons(shape):
        for triangle in constrained_delaunay_triangles(polygon).geoms:
            for x, y in list(triangle.exterior.coords)[:3]:
                point = (round(x / width, 6), round(1.0 - y / height, 6))
                if point not in vertices:
                    vertices[point] = len(vertices)
                indices.append(vertices[point])
    return vertices.keys(), indices


def build():
    assets = json.loads((SOURCE / "manifest.json").read_text(encoding="utf-8"))
    lines = ["%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:", "--- !u!114 &11400000",
             "MonoBehaviour:", "  m_ObjectHideFlags: 0", "  m_CorrespondingSourceObject: {fileID: 0}",
             "  m_PrefabInstance: {fileID: 0}", "  m_PrefabAsset: {fileID: 0}", "  m_GameObject: {fileID: 0}",
             "  m_Enabled: 1", "  m_EditorHideFlags: 0",
             f"  m_Script: {{fileID: 11500000, guid: {CATALOG_SCRIPT}, type: 3}}",
             "  m_Name: VectorUiCatalog", "  m_EditorClassIdentifier:", "  entries:"]
    keys = set()
    for entry in assets:
        guid, local_id = entry["guid"], entry["fileId"]
        if (guid, local_id) in keys:
            raise ValueError(f"Duplicate sprite reference for {entry['svg']}")
        keys.add((guid, local_id))
        width, height, layers = read_svg(SOURCE / entry["svg"])
        lines.extend((f"  - sprite: {{fileID: {local_id}, guid: {guid}, type: {0 if guid == BUILTIN else 3}}}",
                      f"    artSize: {{x: {width:g}, y: {height:g}}}",
                      f"    slice: {entry.get('slice', 0)}", "    layers:"))
        for color, shape in layers.items():
            vertices, indices = triangulate(shape, width, height)
            if not indices:
                raise ValueError(f"{entry['svg']} contains an empty mesh")
            rgba = int.from_bytes(bytes(color), "little")
            lines.extend(("    - color:", "        serializedVersion: 2", f"        rgba: {rgba}", "      vertices:"))
            lines.extend(f"      - {{x: {x}, y: {y}}}" for x, y in vertices)
            lines.append("      triangles: " + struct.pack(f"<{len(indices)}i", *indices).hex())

    library = ROOT / "Library"
    library.mkdir(exist_ok=True)
    with TemporaryDirectory(prefix="vector-ui-", dir=library) as stage:
        ready = Path(stage) / OUTPUT.name
        ready.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
        ready.replace(OUTPUT)
    print(f"{len(keys)} SVG assets compiled.")


if __name__ == "__main__":
    build()
