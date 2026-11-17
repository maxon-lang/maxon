
from __future__ import annotations

import copy

from capmodel import cast as C


def walk(node, visit):
    if isinstance(node, (list, tuple)):
        for item in node:
            walk(item, visit)
    elif isinstance(node, dict):
        for item in node.values():
            walk(item, visit)
    elif hasattr(node, "__dict__") and not isinstance(node, type):
        visit(node)
        for key, value in vars(node).items():
            if key != "bound":
                walk(value, visit)
    elif isinstance(node, (str, int, float, bool)) or node is None:
        return
    else:
        raise TypeError(f"walk: unexpected {type(node).__name__}")


def var_names(node):
    names = set()

    def visit(n):
        if isinstance(n, C.Var):
            names.add(n.name)

    walk(node, visit)
    return names


def unbind_unread_arms(prog):
    out = copy.deepcopy(prog)
    changed = []

    def visit(n):
        if isinstance(n, C.Match):
            for arm in n.arms:
                if arm.binds and not set(arm.binds) & var_names(arm.body):
                    arm.binds = []
                    changed.append(arm)

    walk(out, visit)
    return out, bool(changed)


def names_of(node):
    found = set()

    def visit(n):
        if isinstance(n, C.TName):
            found.add(("type", n.name))
        elif isinstance(n, (C.New, C.UnionNew, C.Spawn)):
            found.add(("type", n.tyname))
        elif isinstance(n, (C.Call, C.AsyncCall, C.FnRef)):
            found.add(("func", n.func))

    walk(node, visit)
    return found


def prune(prog):
    reached_types, reached_funcs = set(), set()
    pending = [("main", prog.main)] + [("slot", s) for s in prog.slots]
    queue = [item[1] for item in pending]
    while queue:
        node = queue.pop()
        for kind, name in names_of(node):
            if kind == "func":
                if name in prog.funcs and name not in reached_funcs:
                    reached_funcs.add(name)
                    queue.append(prog.funcs[name])
            elif name not in reached_types:
                decl = prog.type_of(name)
                if decl is not None:
                    reached_types.add(name)
                    queue.append(decl)
                    for iface in getattr(decl, "implements", []) or []:
                        queue.append(C.TName(iface))
    out = copy.deepcopy(prog)
    out.records = {k: v for k, v in out.records.items() if k in reached_types}
    out.unions = {k: v for k, v in out.unions.items() if k in reached_types}
    out.services = {k: v for k, v in out.services.items() if k in reached_types}
    out.interfaces = {k: v for k, v in out.interfaces.items() if k in reached_types}
    out.funcs = {k: v for k, v in out.funcs.items() if k in reached_funcs}
    return out
