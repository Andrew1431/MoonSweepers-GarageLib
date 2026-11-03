# Reads a mesh model (.bin, written by export_mesh_model.py) back with plain `struct` and prints what the loader sees:
#   node <name> parent=<i> pos=(x,y,z) rot=(x,y,z,w) props={...} tris=<n>
# with, for `Port_*` nodes, `rootpos=` and `dir=`: the port position and flow direction in the model root's space,
# Unity axes (Y up). Ends with a `total` line. Runs under any Python 3 or Blender's:
#   python shared/blender/check_bin.py <file.bin>
#   "<blender.exe>" -b --factory-startup --python shared/blender/check_bin.py -- <file.bin>
# Other scripts can `import check_bin` and call read(path) -> (materials, nodes) and dump(materials, nodes).
import struct, sys


class _R:
    def __init__(self, data):
        self.d, self.i = data, 0

    def take(self, fmt):
        fmt = "<" + fmt
        v = struct.unpack_from(fmt, self.d, self.i)
        self.i += struct.calcsize(fmt)
        return v

    def string(self):
        n, shift = 0, 0
        while True:
            b = self.d[self.i]
            self.i += 1
            n |= (b & 0x7F) << shift
            shift += 7
            if b < 0x80:
                break
        s = self.d[self.i:self.i + n].decode("utf-8")
        self.i += n
        return s


def _qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def _qrot(q, v):
    x, y, z, w = q
    r = _qmul(_qmul(q, (v[0], v[1], v[2], 0.0)), (-x, -y, -z, w))
    return r[:3]


def read(path):
    with open(path, "rb") as f:
        r = _R(f.read())
    magic = bytes(r.d[:4])
    if magic != b"MH6M":
        raise ValueError(f"{path}: bad magic {magic!r}")
    r.i = 4
    (version,) = r.take("H")
    (nmat,) = r.take("H")
    mats = []
    for _ in range(nmat):
        name = r.string()
        r.take("4f")
        mats.append(name)
    (nnode,) = r.take("H")
    nodes = []
    for _ in range(nnode):
        n = {"name": r.string()}
        (n["parent"],) = r.take("h")
        n["pos"], n["rot"], n["scale"] = r.take("3f"), r.take("4f"), r.take("3f")
        (nprops,) = r.take("B")
        props = {}
        for _ in range(nprops):
            k = r.string()
            (t,) = r.take("B")
            if t == 0:
                (props[k],) = r.take("f")
            elif t == 1:
                props[k] = r.string()
            else:
                (cnt,) = r.take("B")
                props[k] = list(r.take(f"{cnt}f"))
        n["props"] = props
        (has_mesh,) = r.take("B")
        n["tris"], n["materials"] = 0, []
        if has_mesh:
            (nv,) = r.take("i")
            r.i += nv * 24
            (nsub,) = r.take("B")
            for _ in range(nsub):
                m, ni = r.take("Hi")
                r.i += ni * 4
                n["tris"] += ni // 3
                n["materials"].append(mats[m])
        # Root-space transform (the root itself is written as identity), Unity axes.
        if n["parent"] < 0:
            n["rootpos"], n["rootrot"], n["rootscale"] = (0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), (1.0, 1.0, 1.0)
        else:
            p = nodes[n["parent"]]
            scaled = tuple(a * b for a, b in zip(n["pos"], p["rootscale"]))
            n["rootpos"] = tuple(a + b for a, b in zip(p["rootpos"], _qrot(p["rootrot"], scaled)))
            n["rootrot"] = _qmul(p["rootrot"], n["rot"])
            n["rootscale"] = tuple(a * b for a, b in zip(p["rootscale"], n["scale"]))
        # Blender local +Y (a port's flow axis) is Unity local -Z under the exporter's axis map.
        n["dir"] = _qrot(n["rootrot"], (0.0, 0.0, -1.0))
        nodes.append(n)
    if r.i != len(r.d):
        raise ValueError(f"{path}: {len(r.d) - r.i} trailing bytes")
    return mats, nodes


def _f(v):
    return "(" + ",".join(f"{x:.3f}" + "" for x in v).replace("-0.000", "0.000") + ")"


def dump(mats, nodes):
    print("materials", mats)
    for n in nodes:
        line = (f"node {n['name']} parent={n['parent']} pos={_f(n['pos'])} rot={_f(n['rot'])} "
                f"props={n['props']} tris={n['tris']}")
        if n["name"].startswith("Port_"):
            line += f" rootpos={_f(n['rootpos'])} dir={_f(n['dir'])}"
        print(line)
    ports = sum(1 for n in nodes if n["name"].startswith("Port_"))
    print(f"total nodes={len(nodes)} ports={ports} tris={sum(n['tris'] for n in nodes)}")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    if not args:
        sys.exit("usage: check_bin.py <file.bin> [...]")
    for a in args:
        print("file", a)
        dump(*read(a))
