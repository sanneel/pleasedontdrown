"""Author clean, reusable resort architecture/furniture in Blender, in Unity metres."""
from pathlib import Path
import json
import math
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/_Game/Art/Props'
SOURCE = ROOT / 'ArtSource/Resort'
SOURCE.mkdir(parents=True, exist_ok=True)
PAINT = {
    'resort_ivory': (.96, .92, .82), 'resort_teal': (.09, .46, .48),
    'resort_wood': (.61, .40, .23), 'resort_wood_dark': (.31, .20, .13),
    'resort_brass': (.79, .61, .29), 'resort_coral': (.88, .37, .29),
    'resort_leaf': (.19, .42, .24), 'resort_leaf_light': (.34, .55, .28),
    'resort_thatch': (.77, .62, .37), 'resort_thatch_light': (.86, .73, .48),
    'resort_glass': (.12, .36, .42), 'resort_lantern': (1, .65, .23),
}
REPORT = []

def linear(c): return c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4
def material(name):
    old = bpy.data.materials.get(name)
    if old: return old
    m = bpy.data.materials.new(name); m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = tuple(linear(c) for c in PAINT[name]) + (1,)
    p.inputs['Roughness'].default_value = .28 if name == 'resort_brass' else .45
    p.inputs['Metallic'].default_value = .7 if name == 'resort_brass' else 0
    if name == 'resort_lantern':
        p.inputs['Emission Color'].default_value = (1, .40, .08, 1)
        p.inputs['Emission Strength'].default_value = 2
    return m

def pos(p): return (p[0], -p[2], p[1])
def paint(o, name): o.data.materials.append(material(name)); return o
def cube(name, centre, size, colour, bevel=.035, tilt=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos(centre))
    o = bpy.context.object; o.name = name; o.dimensions = (size[0], size[2], size[1])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.rotation_euler.x = math.radians(tilt)
    paint(o, colour)
    if bevel:
        modifier = o.modifiers.new('Crisp edge highlights', 'BEVEL')
        modifier.width = min(bevel, min(size) * .20); modifier.segments = 2
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        normal = o.modifiers.new('Weighted normals', 'WEIGHTED_NORMAL'); normal.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=normal.name)
    return o
def cylinder(name, centre, radius, height, colour, vertices=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=height, location=pos(centre))
    o = bpy.context.object; o.name = name; paint(o, colour)
    modifier = o.modifiers.new('Soft rim', 'BEVEL'); modifier.width = min(.02, radius * .1, height * .15); modifier.segments = 2
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return o
def sphere(name, centre, scale, colour):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=6, location=pos(centre))
    o = bpy.context.object; o.name = name; o.scale = (scale[0], scale[2], scale[1]); paint(o, colour)
    return o
def rod(name, a, b, radius, colour, vertices=8):
    av, bv = Vector(pos(a)), Vector(pos(b)); d = bv - av
    o = cylinder(name, (0, 0, 0), radius, d.length, colour, vertices)
    o.location = (av + bv) / 2; o.rotation_euler = d.to_track_quat('Z', 'Y').to_euler(); return o
def leaf(centre, length, angle, colour):
    x, y, z = centre; a = math.radians(angle)
    end = (x + math.sin(a) * length, y + .4 * length, z + math.cos(a) * length)
    right = (.13 * math.cos(a), 0, -.13 * math.sin(a))
    middle = tuple((centre[i] + end[i]) * .5 for i in range(3))
    points = [centre, tuple(middle[i] + right[i] for i in range(3)), end, tuple(middle[i] - right[i] for i in range(3))]
    mesh = bpy.data.meshes.new('Leaf'); mesh.from_pydata([pos(p) for p in points], [], [(0, 1, 2), (0, 2, 3), (2, 1, 0), (3, 2, 0)]); mesh.update()
    o = bpy.data.objects.new('Tropical leaf', mesh); bpy.context.collection.objects.link(o); paint(o, colour)
def start(): bpy.ops.wm.read_factory_settings(use_empty=True)
def finish(name):
    # One renderer per material, with all furniture authored at real scale.
    objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects: o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]; bpy.ops.object.join()
    joined = bpy.context.object; joined.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    joined.data.validate(clean_customdata=False); joined.data.calc_loop_triangles()
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / (name + '.blend')))
    bpy.ops.export_scene.gltf(filepath=str(OUT / (name + '.glb')), export_format='GLB', use_selection=True,
                             export_animations=False, export_cameras=False, export_lights=False)
    REPORT.append({'model': name, 'triangles': len(joined.data.loop_triangles), 'materials': len(joined.data.materials)})

start()
# A broad, open beach club with a U counter and space to walk behind it.
cube('Boardwalk deck', (0, .08, 0), (26, .16, 16), 'resort_wood', .025)
for z in range(-7, 8): cube('Deck seam', (0, .165, z), (25.8, .007, .025), 'resort_wood_dark', 0)
for x in (-11.8, 11.8):
    for z in (-6.8, 6.8):
        cube('Timber post', (x, 2.35, z), (.30, 4.5, .30), 'resort_wood', .035)
        cube('Post brass foot', (x, .29, z), (.38, .26, .38), 'resort_brass', .02)
for z in (-6.8, 6.8): cube('Roof beam', (0, 4.45, z), (24.2, .32, .30), 'resort_wood_dark')
for side in (-1, 1):
    cube('Pitched thatch roof', (0, 5.70, side * 4.35), (28, .34, 9.0), 'resort_thatch', .055, side * 17)
    for i in range(6):
        z = side * (i * 1.4 + .5); y = 6.9 - abs(z) * .285
        cube('Layered thatch strip', (0, y, z), (28.1, .10, .22), 'resort_thatch_light', .018, side * 17)
cube('Roof ridge', (0, 7.04, 0), (28.2, .28, .30), 'resort_wood_dark')
for centre, size in [((0, .73, 5), (18, 1.18, .9)), ((-8.6, .73, 3.2), (.8, 1.18, 3.6)), ((8.6, .73, 3.2), (.8, 1.18, 3.6))]:
    cube('Ivory counter', centre, size, 'resort_ivory', .07)
    cube('Polished timber counter top', (centre[0], 1.35, centre[2]), (size[0] + .15, .11, size[2] + .16), 'resort_wood_dark', .045)
    cube('Counter turquoise foot trim', (centre[0], .30, centre[2]), (size[0] + .025, .22, size[2] + .025), 'resort_teal', .02)
cube('Bottle display back', (0, 1.7, -4.8), (17, 3.1, .28), 'resort_teal')
for y in (.6, 1.55, 2.50):
    cube('Bottle shelf', (0, y, -4.5), (17.1, .10, .65), 'resort_wood_dark')
    for i in range(24):
        x = -7.8 + i * .68
        cylinder('Display bottle', (x, y + .22, -4.5), .065, .30, 'resort_glass' if i % 3 else 'resort_coral', 10)
        cylinder('Bottle neck', (x, y + .41, -4.5), .031, .09, 'resort_brass', 8)
for x in (-8, -6, -4, -2, 0, 2, 4, 6, 8):
    cylinder('Stool seat', (x, .88, 6.5), .36, .16, 'resort_teal')
    cylinder('Stool stem', (x, .48, 6.5), .065, .7, 'resort_wood_dark', 10)
    cylinder('Stool base', (x, .20, 6.5), .28, .08, 'resort_brass')
for x in (-10.6, 10.6):
    cylinder('Side table', (x, .82, .1), .8, .12, 'resort_wood_dark')
    cylinder('Table pedestal', (x, .45, .1), .1, .70, 'resort_wood')
    for z in (-1.2, 1.4):
        cube('Side bench seat', (x, .55, z), (1.8, .3, .85), 'resort_ivory', .10)
        cube('Side bench back', (x, .95, z + (.32 if z > 0 else -.32)), (1.8, .72, .14), 'resort_teal', .055)
for x in range(-10, 11, 4):
    rod('Lantern cable', (x, 4.45, 5.7), (x, 3.7, 5.7), .012, 'resort_wood_dark')
    cube('Warm lantern', (x, 3.6, 5.7), (.25, .35, .25), 'resort_lantern', .02)
    cube('Lantern cap', (x, 3.8, 5.7), (.30, .06, .30), 'resort_brass', .02)
finish('resort_beach_bar')

start()
cube('Cabana deck', (0, .08, 0), (8, .16, 7), 'resort_wood')
for x in (-3.5, 3.5):
    for z in (-3, 3): cube('Cabana post', (x, 1.85, z), (.20, 3.5, .20), 'resort_wood')
cube('Cabana canopy', (0, 3.64, 0), (8.5, .18, 7.5), 'resort_teal', .045)
for x in range(-4, 5): cube('Cabana rafter', (x * .88, 3.44, 0), (.12, .18, 7.1), 'resort_wood_dark')
for x in (-3.38, 3.38): cube('Side curtain', (x, 1.96, -.9), (.06, 2.65, 3.8), 'resort_ivory', .012)
cube('Rear curtain', (0, 1.96, -2.88), (6.6, 2.65, .06), 'resort_ivory', .012)
cube('Daybed base', (0, .38, -1.2), (3.4, .48, 2.6), 'resort_wood_dark', .07)
cube('Daybed cushion', (0, .71, -1.2), (3.35, .23, 2.55), 'resort_ivory', .10)
for x in (-1.05, 0, 1.05): cube('Cabana pillow', (x, .91, -2.0), (.76, .24, .54), 'resort_coral' if x == 0 else 'resort_teal', .09, -12)
cylinder('Cabana drinks table', (2.55, .62, 1.3), .6, .12, 'resort_wood_dark')
cylinder('Cabana table leg', (2.55, .37, 1.3), .1, .45, 'resort_wood')
finish('resort_cabana')

start()
for x in (-1.1, 1.1):
    cube('Lounge base', (x, .36, 0), (.9, .32, 2.3), 'resort_wood', .06)
    cube('Lounge cushion', (x, .58, .25), (.86, .17, 1.65), 'resort_ivory', .06)
    cube('Reclining back cushion', (x, .86, -.78), (.86, .14, .85), 'resort_teal', .055, -35)
    cube('Lounge pillow', (x, 1.08, -1.04), (.62, .16, .33), 'resort_coral', .055, -35)
cylinder('Cocktail table', (0, .62, .4), .40, .10, 'resort_wood_dark')
cylinder('Cocktail pedestal', (0, .35, .4), .075, .5, 'resort_brass')
finish('resort_lounge_set')

start()
cylinder('Parasol pole', (0, 1.5, 0), .045, 3, 'resort_wood', 12)
cylinder('Parasol base', (0, .08, 0), .38, .15, 'resort_brass')
for i in range(12):
    a = 2 * math.pi * i / 12; b = 2 * math.pi * (i + 1) / 12
    vertices = [(0, 3.0, 0), (1.15 * math.cos(a), 2.86, 1.15 * math.sin(a)),
                (1.15 * math.cos(b), 2.86, 1.15 * math.sin(b)),
                (2 * math.cos(a), 2.60, 2 * math.sin(a)), (2 * math.cos(b), 2.60, 2 * math.sin(b))]
    mesh = bpy.data.meshes.new('Parasol panel'); mesh.from_pydata([pos(p) for p in vertices], [], [(0, 1, 2), (1, 3, 4, 2), (2, 1, 0), (2, 4, 3, 1)]); mesh.update()
    o = bpy.data.objects.new('Parasol stripe', mesh); bpy.context.collection.objects.link(o); paint(o, 'resort_ivory' if i % 2 else 'resort_teal')
    rod('Parasol rib', (0, 2.96, 0), (2 * math.cos(a), 2.57, 2 * math.sin(a)), .013, 'resort_brass', 6)
finish('resort_parasol')

start()
cube('Plant box', (0, .35, 0), (1.9, .7, 1.05), 'resort_ivory', .06)
cube('Plant box rim', (0, .72, 0), (2.02, .12, 1.15), 'resort_teal', .03)
cube('Soil', (0, .765, 0), (1.75, .045, .92), 'resort_wood_dark', .015)
for n, x in enumerate((-.55, 0, .55)):
    for i in range(7): leaf((x, .79, 0), .75 + (i % 3) * .13, i * 51 + n * 13, 'resort_leaf' if i % 2 else 'resort_leaf_light')
    for i in range(3):
        a = i * 2.1 + n; sphere('Tropical flower', (x + math.sin(a) * .21, 1.1 + i * .1, math.cos(a) * .23), (.09, .07, .09), 'resort_coral')
finish('resort_planter')

start()
# Thin overlays sit just in front of the original lobby walls, keeping the 2.6m entry clear.
for x in (-11.6, 11.6): cube('Lobby corner trim', (x, 1.93, 6.20), (.55, 3.3, .13), 'resort_ivory', .025)
for x in (-1.43, 1.43): cube('Reception door jamb', (x, 1.60, 6.20), (.15, 2.60, .13), 'resort_brass', .018)
cube('Reception lintel', (0, 2.94, 6.20), (3.05, .15, .13), 'resort_brass', .018)
for x in (-9, -5, 5, 9):
    cube('Recessed blue reception glazing', (x, 1.9, 6.19), (2, 1.3, .055), 'resort_glass', .015)
    for dx in (-1.05, 1.05): cube('Window jamb', (x + dx, 1.9, 6.24), (.075, 1.44, .085), 'resort_brass', .012)
    for y in (1.21, 2.59): cube('Window frame', (x, y, 6.24), (2.17, .075, .085), 'resort_brass', .012)
cube('Reception fascia', (0, 3.74, 0), (24.5, .20, 12.5), 'resort_teal', .04)
cube('Reception floor edge', (0, .29, 6.17), (24, .13, .18), 'resort_ivory', .025)
# Open arrival canopy with refined roof edges and timber soffit.
cube('Arrival canopy soffit', (0, 4.72, 10), (22, .12, 11), 'resort_wood', .04)
cube('Arrival canopy top', (0, 5.06, 10), (22.6, .30, 11.6), 'resort_ivory', .065)
cube('Arrival brass edge', (0, 4.89, 15.80), (22.6, .07, .08), 'resort_brass', .012)
for x in (-9, 9):
    cube('Arrival column sleeve', (x, 2.55, 14), (.71, 5, .71), 'resort_teal', .025)
    cube('Arrival column base', (x, .26, 14), (.82, .46, .82), 'resort_brass', .025)
finish('resort_reception_trim')
(ROOT / 'Logs/resort-beach-blender.json').write_text(json.dumps(REPORT, indent=2), encoding='utf-8')
print('[ResortBeachBlender] ' + json.dumps(REPORT))
