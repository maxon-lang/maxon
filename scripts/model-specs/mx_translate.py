
from __future__ import annotations

from capmodel import cast as C

from model_prune import walk

KEYWORDS = {
    "and", "as", "async", "await", "bool", "break", "continue", "countof", "cstring", "default", "else", "end",
    "enum", "export", "extends", "extension", "fallthrough", "false", "float", "for", "from", "function", "gives",
    "if", "ignore", "implements", "in", "int", "interface", "is", "let", "match", "mod", "not", "or", "otherwise",
    "panic", "public", "return", "returns", "self", "Self", "shl", "shr", "sizeof", "static", "then", "throw",
    "throws", "to", "true", "try", "type", "typealias", "union", "upto", "uses", "var", "where", "while", "with",
    "xor", "main", "spawn", "swap", "merge", "freeze", "modify", "acquire", "reply", "destruct", "consuming",
    "isolated", "immutable", "mutable", "unique", "sendable", "create", "clone", "handle", "print", "sleep",
    "mailboxFull", "serviceStopped",
}
RESERVED_TYPES = {
    "Array", "Map", "Set", "List", "String", "Reply", "Range", "Log", "Error", "Self", "Promise", "Character",
    "Duration", "Slot", "Test", "Tuple", "Task", "Iterator", "Hasher", "Random", "Clock", "Path", "File", "Url",
}
NUM_ALIAS = "Num"
NUM_DEFINITION = "int(i64.min to i64.max)"
IF_LABEL, ELSE_LABEL = "i", "e"


class Skip(Exception):
    pass


def skip_unless(condition, reason):
    if not condition:
        raise Skip(reason)


def strip_cap(ty):
    if isinstance(ty, C.TCap):
        return ty.cap, ty.ty
    return None, ty


def var_names(node):
    names = set()

    def visit(n):
        if isinstance(n, C.Var):
            names.add(n.name)

    walk(node, visit)
    return names


def assigned_names(node):
    names = set()

    def visit(n):
        if isinstance(n, C.AssignVar):
            names.add(n.name)
        elif isinstance(n, C.Swap) and isinstance(n.target, C.Var):
            names.add(n.target.name)

    walk(node, visit)
    return names


def pushes_in(stmts):
    found = []

    def visit(n):
        if isinstance(n, C.Push):
            found.append(n)

    walk(stmts, visit)
    return found


def is_state_store(st):
    return isinstance(st, C.AssignField) and isinstance(st.obj, C.Var) and st.obj.name == "self"


def assigned_state_fields(stmts):
    found = []

    def visit(n):
        if is_state_store(n):
            found.append(n.field)

    walk(stmts, visit)
    return found


class Translator:
    def __init__(self, prog):
        self.prog = prog
        self.label_count = 0
        self.num_used = False
        self.array_aliases = {}
        self.env = {}
        self.self_type = None
        self.field_names = {f.name for t in self.record_like() for f in getattr(t, "fields", [])}
        self.loops = []

    def record_like(self):
        return list(self.prog.records.values()) + list(self.prog.services.values())

    def check_supported(self):
        prog = self.prog
        skip_unless(not prog.interfaces, "interface")
        skip_unless(not prog.slots, "module slot")
        skip_unless(not prog.dups, "duplicate declaration")
        for r in prog.records.values():
            skip_unless(not r.tparams, "generic type")
            skip_unless(not r.implements, "interface conformance")
            skip_unless(r.name != "Reply", "Reply")
        for s in prog.services.values():
            skip_unless(not s.tparams, "generic type")
            skip_unless(not s.replaced_factory, "duplicate declaration")
        for name in list(prog.records) + list(prog.unions) + list(prog.services) + list(prog.funcs):
            skip_unless(name not in RESERVED_TYPES, "name collides with a library name")

    def translate(self):
        self.check_supported()
        body = []
        for u in self.prog.unions.values():
            body += self.union_decl(u)
        for r in self.prog.records.values():
            body += self.record_decl(r)
        for s in self.prog.services.values():
            body += self.service_decl(s)
        for f in self.prog.funcs.values():
            body += self.func_decl(f)
        body += self.main_decl()
        head = []
        if self.num_used:
            head.append(f"typealias {NUM_ALIAS} = {NUM_DEFINITION}")
        head += [f"typealias {name} = Array with {elem}" for name, elem in self.array_aliases.items()]
        if head:
            head.append("")
        return "\n".join(head + body).rstrip("\n") + "\n"

    def union_decl(self, u):
        out = [f"union {u.name}"]
        for case in u.cases:
            if case.payloads:
                ps = ", ".join(self.payload_text(p) for p in case.payloads)
                out.append(f"\t{case.name}({ps})")
            else:
                out.append(f"\t{case.name}")
        for m in u.methods:
            out += self.indented(self.method_decl(m, owner=u))
        out += [f"end '{u.name}'", ""]
        return out

    def payload_text(self, p):
        return f"{p.name} {self.mtype(p.ty)}"

    def record_decl(self, r):
        header = f"type {r.name}" + (" is unique" if r.unique else "")
        out = [header]
        for f in r.fields:
            out.append(f"\t{f.kw} {f.name} as {self.mtype(f.ty)}")
        if r.fields:
            out.append("")
        out += self.indented(self.create_decl(r.name, r.fields))
        for m in r.methods:
            out.append("")
            out += self.indented(self.method_decl(m, owner=r))
        out += [f"end '{r.name}'", ""]
        return out

    def create_decl(self, name, fields):
        params = ", ".join(f"{f.name} {self.mtype(f.ty)}" for f in fields)
        inits = ", ".join(f"{f.name}: {f.name}" for f in fields)
        return [
            f"static function create({params}) returns Self",
            f"\treturn Self{{{inits}}}",
            "end 'create'",
        ]

    def service_decl(self, s):
        out = [f"type {s.name}"]
        for f in s.fields:
            out.append(f"\t{f.kw} {f.name} as {self.mtype(f.ty)}")
        if s.fields:
            out.append("")
        out += self.indented(self.factory_decl(s))
        for m in s.methods:
            out.append("")
            out += self.indented(self.method_decl(m, owner=s))
        for b in s.behaviours:
            out.append("")
            out += self.indented(self.method_decl(b, owner=s, exported=True))
        out += [f"end '{s.name}'", ""]
        return out

    def is_region_type(self, ty):
        cap, inner = strip_cap(ty)
        if isinstance(inner, (C.TInt, C.TStr)):
            return False
        if isinstance(inner, C.TName):
            if inner.name in self.prog.services:
                return False
            decl = self.prog.type_of(inner.name)
            if isinstance(decl, C.UnionDecl) and all(not c.payloads for c in decl.cases):
                return False
            return True
        if isinstance(inner, C.TArray):
            return True
        raise Skip(f"state field of type {type(inner).__name__}")

    def factory_decl(self, s):
        if s.factory is not None:
            return self.explicit_factory(s)
        params, inits = [], []
        for f in s.fields:
            cap, inner = strip_cap(f.ty)
            if cap is None and self.is_region_type(f.ty):
                params.append(f"{f.name} isolated {self.mtype(f.ty)}")
                inits.append(f"{f.name}: merge {f.name}")
            else:
                params.append(f"{f.name} {self.mtype(f.ty)}")
                inits.append(f"{f.name}: {f.name}")
        return [
            f"static function create({', '.join(params)}) returns Self",
            f"\treturn Self{{{', '.join(inits)}}}",
            "end 'create'",
        ]

    def explicit_factory(self, s):
        fac = s.factory
        body = list(fac.body)
        stores = []
        while body and is_state_store(body[-1]):
            stores.insert(0, body.pop())
        skip_unless(not any(assigned_state_fields(body)), "factory assigns state before its last statements")
        skip_unless(sorted(st.field for st in stores) == sorted(f.name for f in s.fields), "factory assigns state conditionally")
        skip_unless(all("self" not in var_names(st.expr) for st in stores), "factory reads its own state")
        self.begin_body(fac.params, self_type=C.TName(s.name), body=fac.body)
        out = [f"static function create({self.params_text(fac.params)}) returns Self"]
        prefix = self.block(body, extra_unused=self.unused_params(fac.params, fac.body))
        inits = ", ".join(f"{st.field}: {self.expr(st.expr)}" for st in stores)
        out += self.indented(prefix + [f"return Self{{{inits}}}"])
        out.append("end 'create'")
        return out

    def method_decl(self, m, owner, exported=False):
        self.begin_body(m.params, self_type=C.TName(owner.name), body=m.body, result=m.ret_ty)
        recv = f"{m.recv_cap} self" if m.recv_cap else None
        params = ([recv] if recv else []) + [self.param_text(p) for p in m.params]
        ret = f" returns {self.mtype(m.ret_ty)}" if m.ret_ty is not None else ""
        prefix = "export " if exported else ""
        body = self.nested(m.body, extra_unused=self.unused_params(m.params, m.body))
        out = [f"{prefix}function {m.name}({', '.join(params)}){ret}"]
        out += self.indented(body)
        out.append(f"end '{m.name}'")
        return out

    def func_decl(self, f):
        skip_unless(not f.throws, "throws")
        self.begin_body(f.params, self_type=None, body=f.body, result=f.ret_ty)
        ret = f" returns {self.mtype(f.ret_ty)}" if f.ret_ty is not None else ""
        out = [f"function {f.name}({self.params_text(f.params)}){ret}"]
        out += self.indented(self.nested(f.body, extra_unused=self.unused_params(f.params, f.body)))
        out += [f"end '{f.name}'", ""]
        return out

    def main_decl(self):
        self.begin_body([], self_type=None, result=C.INT)
        out = ["function main() returns ExitCode"]
        out += self.indented(self.block(self.prog.main) + ["return 0"])
        out += ["end 'main'"]
        return out

    def begin_body(self, params, self_type, body=(), result=None):
        self.result = result
        skip_unless(not (assigned_names(body) & {p.name for p in params}), "reassigned parameter")
        self.label_count = 0
        self.env = {p.name: p.ty for p in params}
        self.self_type = self_type
        self.loops = []

    def params_text(self, params):
        return ", ".join(self.param_text(p) for p in params)

    def param_text(self, p):
        return f"{self.pname(p.name)} {self.mtype(p.ty)}"

    def unused_params(self, params, body):
        used = var_names(body)
        return [p for p in params if p.name not in used]

    def pname(self, name):
        if name in KEYWORDS or name in self.field_names or name in self.prog.funcs:
            return name + "V"
        return name

    def label(self, prefix):
        self.label_count += 1
        return f"{prefix}{self.label_count}"

    def indented(self, lines):
        return [("\t" + line) if line else line for line in lines]

    def block(self, stmts, extra_unused=()):
        out = []
        for p in extra_unused:
            out.append(f"let _ = {self.touch_text(p)}")
        for index, st in enumerate(stmts):
            skip_unless(index == len(stmts) - 1 or not isinstance(st, (C.Return, C.Break, C.Continue)), "statements after a jump")
            out += self.stmt(st, stmts[index + 1:])
        return out

    def loop_body(self, label, stmts):
        self.loops.append([label, 0])
        try:
            return self.nested(stmts)
        finally:
            self.loops.pop()

    def pushes_to(self, push, array_type):
        try:
            return strip_cap(self.type_of(push.array))[1] == array_type
        except Skip:
            return False

    def touch_text(self, p):
        name = self.pname(p.name)
        _, inner = strip_cap(p.ty)
        decl = self.prog.records.get(inner.name) if isinstance(inner, C.TName) else None
        if decl is not None and (decl.unique or self.is_linear(p.ty)):
            number = next((f.name for f in decl.fields if isinstance(f.ty, C.TInt)), None)
            if number is not None:
                return f"{name}.{number}"
        return name

    def nested(self, stmts, extra_unused=()):
        lines = self.block(stmts, extra_unused)
        if lines:
            return lines
        self.num_used = True
        return [f"let _ = 0 as {NUM_ALIAS}"]

    def mtype(self, ty):
        cap, inner = strip_cap(ty)
        text = self.mtype_inner(inner)
        return f"{cap} {text}" if cap else text

    def mtype_inner(self, ty):
        if isinstance(ty, C.TInt):
            self.num_used = True
            return NUM_ALIAS
        if isinstance(ty, C.TStr):
            return "String"
        if isinstance(ty, C.TName):
            skip_unless(not ty.args, "generic type")
            if ty.name in self.prog.services:
                return f"{ty.name}.handle"
            skip_unless(self.prog.type_of(ty.name) is not None, "unknown type name")
            skip_unless(not isinstance(self.prog.type_of(ty.name), C.InterfaceDecl), "interface")
            return ty.name
        if isinstance(ty, C.TArray):
            skip_unless(not isinstance(ty.elem, C.TCap) or ty.elem.cap != "isolated", "array of isolated elements")
            elem = self.mtype(ty.elem)
            name = elem.replace(" ", "_").replace(".", "_") + "Array"
            name = name[0].upper() + name[1:]
            self.array_aliases[name] = elem
            return name
        raise Skip(f"type {type(ty).__name__}")

    def type_of(self, e):
        if isinstance(e, C.IntLit):
            return C.INT
        if isinstance(e, C.Var):
            if e.name == "self":
                skip_unless(self.self_type is not None, "self outside a method")
                return self.self_type
            skip_unless(e.name in self.env, f"untyped name")
            return self.env[e.name]
        if isinstance(e, C.FieldRead):
            _, obj_type = strip_cap(self.type_of(e.obj))
            skip_unless(isinstance(obj_type, C.TName), "field of a non-record")
            decl = self.prog.field_decl(obj_type.name, e.field)
            skip_unless(decl is not None, "unknown field")
            return decl.ty
        if isinstance(e, (C.New,)):
            return C.TName(e.tyname)
        if isinstance(e, C.UnionNew):
            return C.TName(e.tyname)
        if isinstance(e, C.Spawn):
            return C.TName(e.tyname)
        if isinstance(e, C.NewArray):
            return C.TArray("var", e.elem)
        if isinstance(e, C.Call):
            f = self.prog.funcs.get(e.func)
            skip_unless(f is not None and f.ret_ty is not None, "call of an unknown or valueless function")
            return f.ret_ty
        if isinstance(e, C.MethodCall):
            m = self.method_of(e)
            skip_unless(m.ret_ty is not None, "valueless method used as a value")
            return m.ret_ty
        if isinstance(e, (C.Freeze, C.Merge, C.Clone)):
            cap, inner = strip_cap(self.type_of(e.expr))
            return inner
        if isinstance(e, C.Swap):
            return self.type_of(e.target)
        if isinstance(e, C.Cond):
            return self.type_of(e.then)
        if isinstance(e, C.Send):
            beh = self.behaviour_of(e)
            skip_unless(beh.ret_ty is not None, "promise of a valueless message")
            return C.TPromise(None, beh.ret_ty)
        if isinstance(e, C.Await):
            promise = self.type_of(e.promise)
            skip_unless(isinstance(promise, C.TPromise), "await of a promise")
            return promise.res
        raise Skip(f"type of {type(e).__name__}")

    def type_decl_of(self, ty):
        cap, inner = strip_cap(ty)
        skip_unless(isinstance(inner, C.TName), "receiver is not a named type")
        decl = self.prog.type_of(inner.name)
        skip_unless(decl is not None, "unknown receiver type")
        return decl

    def method_of(self, call):
        decl = self.type_decl_of(self.type_of(call.recv))
        methods = [m for m in getattr(decl, "methods", []) if m.name == call.method]
        if not methods and isinstance(decl, C.ServiceDecl):
            methods = [b for b in decl.behaviours if b.name == call.method]
        skip_unless(len(methods) == 1, "method not uniquely resolved")
        return methods[0]

    def behaviour_of(self, send):
        decl = self.type_decl_of(self.type_of(send.recv))
        skip_unless(isinstance(decl, C.ServiceDecl), "send to a non-service")
        found = [b for b in decl.behaviours if b.name == send.method]
        skip_unless(len(found) == 1, "behaviour not uniquely resolved")
        return found[0]

    def is_linear(self, ty, seen=()):
        cap, inner = strip_cap(ty)
        if not isinstance(inner, C.TName) or inner.name in seen:
            return False
        decl = self.prog.type_of(inner.name)
        if isinstance(decl, C.RecordDecl):
            return any(m.recv_cap == "consuming" for m in decl.methods)
        if isinstance(decl, C.UnionDecl):
            return any(self.is_linear(p.ty, seen + (inner.name,)) for c in decl.cases for p in c.payloads)
        return False

    def stmt(self, st, rest):
        if isinstance(st, C.Bind):
            return self.bind(st, rest)
        if isinstance(st, C.AssignVar):
            skip_unless(not (isinstance(st.expr, C.Var) and st.expr.name == st.name), "self assignment")
            return [f"{self.pname(st.name)} = {self.expr(st.expr)}"]
        if isinstance(st, C.AssignField):
            target = f"{self.expr(st.obj)}.{st.field}"
            value = self.expr(st.expr)
            skip_unless(target != value, "self assignment")
            return [f"{target} = {value}"]
        if isinstance(st, C.ExprStmt):
            return self.expr_stmt(st.expr)
        if isinstance(st, C.Return):
            skip_unless((st.expr is None) == (self.result is None), "return does not match the declared result")
            return ["return"] if st.expr is None else [f"return {self.expr(st.expr)}"]
        if isinstance(st, C.If):
            return self.if_stmt(st)
        if isinstance(st, C.Push):
            return [f"{self.expr(st.array)}.push({self.expr(st.value)})"]
        if isinstance(st, C.While):
            label = self.label("w")
            return [f"while {self.cond(st.cond)} '{label}'"] + self.indented(self.loop_body(label, st.body)) + [f"end '{label}'"]
        if isinstance(st, C.ForEach):
            _, arr = strip_cap(self.type_of(st.array))
            skip_unless(isinstance(arr, C.TArray), "for over a non-array")
            skip_unless(not any(self.pushes_to(p, arr) for p in pushes_in(st.body)), "loop pushes to an array of the type it iterates")
            self.env[st.name] = arr.elem
            label = self.label("f")
            used = st.name in var_names(st.body)
            name = self.pname(st.name) if used else "_"
            return [f"for {name} in {self.expr(st.array)} '{label}'"] + self.indented(self.loop_body(label, st.body)) + [f"end '{label}'"]
        if isinstance(st, C.Break):
            skip_unless(self.loops, "break outside a loop")
            label, matches = self.loops[-1]
            return ["break"] if matches == 0 else [f"break '{label}'"]
        if isinstance(st, C.Continue):
            return ["continue"]
        if isinstance(st, C.Match):
            return self.match_stmt(st)
        if isinstance(st, C.Modify):
            label = self.label("m")
            return [f"modify {self.expr(st.target)} '{label}'"] + self.indented(self.nested(st.body)) + [f"end '{label}'"]
        if isinstance(st, C.Acquire):
            return self.acquire_stmt(st)
        if isinstance(st, C.Try):
            return self.try_stmt(st)
        if isinstance(st, C.Destruct):
            return ["destruct self"]
        raise Skip(f"statement {type(st).__name__}")

    def cond(self, e):
        return f"{self.expr(e)} != 0"

    def bind(self, st, rest):
        used = st.name in var_names(rest) or st.name in assigned_names(rest)
        value = self.send_promise_text(st.expr) if isinstance(st.expr, C.Send) else self.expr(st.expr)
        if isinstance(st.expr, C.IntLit):
            self.num_used = True
            value = f"{value} as {NUM_ALIAS}"
        if st.cap:
            skip_unless(st.cap in ("isolated", "immutable"), f"binding capability {st.cap}")
            _, inner = strip_cap(self.type_of(st.expr))
            value = f"{value} as {st.cap} {self.mtype(inner)}"
        self.env[st.name] = self.bound_type(st)
        if not used:
            return [f"let _ = {value}"]
        reassigned = st.name in assigned_names(rest) or st.name in self.by_reference_names(rest)
        kw = "var" if (st.kw == "var" and reassigned) else "let"
        return [f"{kw} {self.pname(st.name)} = {value}"]

    def by_reference_names(self, stmts):
        names = set()

        def visit(n):
            if isinstance(n, C.Call):
                params = self.prog.funcs[n.func].params if n.func in self.prog.funcs else []
            elif isinstance(n, C.MethodCall):
                try:
                    params = self.method_of(n).params
                except Skip:
                    return
            else:
                return
            for param, arg in zip(params, n.args):
                if isinstance(arg, C.Var) and isinstance(param.ty, C.TCap) and param.ty.cap == "mutable":
                    names.add(arg.name)

        walk(stmts, visit)
        return names

    def bound_type(self, st):
        ty = self.type_of(st.expr)
        if st.cap:
            _, inner = strip_cap(ty)
            return C.TCap(st.cap, inner)
        return ty

    def expr_stmt(self, e):
        if isinstance(e, C.Send):
            return [self.send_text(e, handler=None)]
        text = self.expr(e)
        if isinstance(e, C.Call):
            f = self.prog.funcs.get(e.func)
            skip_unless(f is not None, "call of an unknown function")
            valued = f.ret_ty is not None
        elif isinstance(e, C.MethodCall):
            valued = self.method_of(e).ret_ty is not None
        elif isinstance(e, C.Await):
            valued = True
        else:
            valued = True
        return [f"let _ = {text}"] if valued else [text]

    def if_stmt(self, st):
        skip_unless(st.then or st.els, "if with two empty branches")
        label = self.label(IF_LABEL)
        cond = self.cond(st.cond)
        if not st.then:
            cond = f"{self.expr(st.cond)} == 0"
            branches = [(cond, st.els)]
        else:
            branches = [(cond, st.then)]
            if st.els:
                branches.append((None, st.els))
        first_cond, first_body = branches[0]
        out = [f"if {first_cond} '{label}'"] + self.indented(self.nested(first_body))
        if len(branches) == 2:
            else_label = self.label(ELSE_LABEL)
            out += [f"end '{label}' else '{else_label}'"] + self.indented(self.nested(branches[1][1])) + [f"end '{else_label}'"]
        else:
            out.append(f"end '{label}'")
        return out

    def match_stmt(self, st):
        label = self.label("x")
        scrut_type = self.type_of(st.scrutinee)
        _, inner = strip_cap(scrut_type)
        skip_unless(isinstance(inner, C.TName) and inner.name in self.prog.unions, "match on a non-union")
        union = self.prog.unions[inner.name]
        out = [f"match {self.expr(st.scrutinee)} '{label}'"]
        if self.loops:
            self.loops[-1][1] += 1
        covered = set()
        for arm in st.arms:
            case = next((c for c in union.cases if c.name == arm.case), None)
            skip_unless(case is not None, "match arm of an unknown case")
            skip_unless(arm.case not in covered, "match arm repeats a case")
            skip_unless(len(set(arm.binds)) == len(arm.binds), "match arm binds one name twice")
            covered.add(arm.case)
            out.append("\t" + self.arm_text(arm, case))
        for case in union.cases:
            if case.name not in covered:
                out.append(f"\t{case.name} then break")
        if self.loops:
            self.loops[-1][1] -= 1
        out.append(f"end '{label}'")
        return out

    def arm_text(self, arm, case):
        skip_unless(len(arm.body) <= 1, "match arm with several statements")
        skip_unless(len(arm.binds) in (0, len(case.payloads)), "match arm binding count")
        used = var_names(arm.body)
        names = [(b if b in used else "_") for b in arm.binds]
        saved = dict(self.env)
        for bind, payload in zip(arm.binds, case.payloads):
            self.env[bind] = payload.ty
        names = [self.pname(n) if n != "_" else n for n in names]
        pattern = arm.case if all(n == "_" for n in names) else f"{arm.case}({', '.join(names)})"
        if not arm.body:
            body = "break"
        else:
            lines = self.stmt(arm.body[0], [])
            skip_unless(len(lines) == 1, "block statement in a match arm")
            body = lines[0]
        self.env = saved
        return f"{pattern} then {body}"

    def acquire_stmt(self, st):
        skip_unless(not self.states_used_as_values(st), "an acquired state used as a value")
        skip_unless(not ({v.name for v in st.names} & assigned_names(st.body)), "an acquired name is reassigned")
        label = self.label("a")
        stopped = self.label("s")
        saved_env = dict(self.env)
        out = [f"acquire {', '.join(self.pname(v.name) for v in st.names)} '{label}'"]
        out += self.indented(self.nested(st.body))
        self.env = saved_env
        out.append(f"end '{label}' otherwise '{stopped}'")
        out += self.indented(self.nested(st.handler))
        out.append(f"end '{stopped}'")
        return out

    def states_used_as_values(self, st):
        names = {v.name for v in st.names}
        reached = set()

        def mark(n):
            for slot in ("obj", "recv", "target"):
                holder = getattr(n, slot, None)
                if isinstance(holder, C.Var):
                    reached.add(id(holder))

        walk(st.body, mark)
        bare = []

        def visit(n):
            if isinstance(n, C.Var) and n.name in names and id(n) not in reached:
                bare.append(n)

        walk(st.body, visit)
        return bool(bare)

    def try_stmt(self, st):
        skip_unless(len(st.body) == 1 and isinstance(st.body[0], C.ExprStmt) and isinstance(st.body[0].expr, C.Send), "try over a non-send")
        return self.send_with_handler(st.body[0].expr, st)

    def send_with_handler(self, send, st):
        beh = self.behaviour_of(send)
        skip_unless(beh.ret_ty is None, "hand-back of a send with a reply")
        skip_unless(len(st.handler) <= 1, "hand-back handler with several statements")
        label = self.label("h")
        match_label = self.label("x")
        binds = list(st.binds)
        skip_unless(len(set(binds)) == len(binds), "hand-back names repeat")
        linear = [i for i, p in enumerate(beh.params) if self.is_linear(p.ty)]
        skip_unless(len(binds) == len(linear), "hand-back names do not match the linear arguments")
        saved = dict(self.env)
        names = []
        for i, p in enumerate(beh.params):
            if i in linear:
                bind = binds[linear.index(i)]
                self.env[bind] = p.ty
                names.append(bind)
            else:
                names.append("_")
        used = var_names(st.handler)
        names = [(self.pname(n) if (n in used) else "_") for n in names]
        arms = []
        for case in ("mailboxFull", "serviceStopped"):
            pattern = case if all(n == "_" for n in names) else f"{case}({', '.join(names)})"
            if st.handler:
                lines = self.stmt(st.handler[0], [])
                skip_unless(len(lines) == 1, "block statement in a hand-back arm")
                arms.append(f"\t\t{pattern} then {lines[0]}")
            else:
                arms.append(f"\t\t{pattern} then break")
        self.env = saved
        head = self.send_text(send, handler=f"(sendFailure) '{label}'")
        return [head, f"\tmatch sendFailure '{match_label}'"] + arms + [f"\tend '{match_label}'", f"end '{label}'"]

    def send_call(self, send):
        skip_unless(not send.wait, "sendwait")
        beh = self.behaviour_of(send)
        return beh, f"{self.expr(send.recv)}.{send.method}({self.args_text(beh.params, send.args, moves=True)})"

    def send_text(self, send, handler):
        beh, call = self.send_call(send)
        if beh.ret_ty is not None:
            skip_unless(handler is None, "hand-back of a send with a reply")
            return f"let _ = try {call} otherwise panic(\"send failed\")"
        return f"try {call} otherwise {handler if handler else 'ignore'}"

    def send_promise_text(self, send):
        beh, call = self.send_call(send)
        skip_unless(beh.ret_ty is not None, "promise of a valueless message")
        return f"try {call} otherwise panic(\"send failed\")"

    def is_handle(self, e):
        _, ty = strip_cap(self.type_of(e))
        return isinstance(ty, C.TName) and ty.name in self.prog.services

    def args_text(self, params, args, declared_raw=False, moves=False):
        skip_unless(len(params) == len(args), "argument count")
        parts = []
        for index, (param, arg) in enumerate(zip(params, args)):
            skip_unless(not (isinstance(param.ty, C.TCap) and param.ty.cap == "mutable" and not isinstance(arg, C.Var)), "a temporary passed by reference")
            skip_unless(not (isinstance(arg, C.IntLit) and isinstance(strip_cap(param.ty)[1], C.TName)), "a number where a record or handle is expected")
            text = self.expr(arg)
            if moves and isinstance(arg, C.Var) and self.is_handle(arg):
                text += ".clone()"
            label = param.name if declared_raw else self.pname(param.name)
            parts.append(text if index == 0 else f"{label}: {text}")
        return ", ".join(parts)

    def expr(self, e):
        if isinstance(e, C.IntLit):
            return str(e.value)
        if isinstance(e, C.Var):
            return "self" if e.name == "self" else self.pname(e.name)
        if isinstance(e, C.FieldRead):
            return f"{self.expr(e.obj)}.{e.field}"
        if isinstance(e, C.New):
            decl = self.prog.records.get(e.tyname)
            skip_unless(decl is not None, "new of a non-record")
            skip_unless(len(decl.fields) == len(e.args), "constructor argument count")
            return f"{e.tyname}.create({self.args_text(decl.fields, e.args, declared_raw=True)})"
        if isinstance(e, C.UnionNew):
            case = self.prog.union_case(e.tyname, e.case)
            skip_unless(case is not None, "union case")
            if not case.payloads:
                return f"{e.tyname}.{e.case}"
            return f"{e.tyname}.{e.case}({self.args_text(case.payloads, e.args, declared_raw=True)})"
        if isinstance(e, C.NewArray):
            name = self.mtype(C.TArray("var", e.elem))
            return f"{name}.create(0)"
        if isinstance(e, C.Call):
            f = self.prog.funcs.get(e.func)
            skip_unless(f is not None, "call of an unknown function")
            return f"{e.func}({self.args_text(f.params, e.args)})"
        if isinstance(e, C.MethodCall):
            m = self.method_of(e)
            return f"{self.expr(e.recv)}.{e.method}({self.args_text(m.params, e.args)})"
        if isinstance(e, C.Spawn):
            return self.spawn_text(e)
        if isinstance(e, C.Swap):
            skip_unless(isinstance(e.target, C.FieldRead), "swap of a local")
            return f"swap {self.expr(e.target)} with {self.expr(e.value)}"
        if isinstance(e, C.Freeze):
            return f"freeze {self.operand(e.expr)}"
        if isinstance(e, C.Merge):
            return f"merge {self.operand(e.expr)}"
        if isinstance(e, C.Clone):
            inner = self.expr(e.expr)
            return f"{inner}.clone()"
        if isinstance(e, C.Cond):
            return f"({self.expr(e.then)} if {self.cond(e.cond)} else {self.expr(e.els)})"
        if isinstance(e, C.Await):
            return self.await_text(e)
        raise Skip(f"expression {type(e).__name__}")

    def operand(self, e):
        if isinstance(e, C.Var):
            return self.expr(e)
        return f"({self.expr(e)})"

    def spawn_text(self, e):
        skip_unless(not e.targs, "generic type")
        decl = self.prog.services.get(e.tyname)
        skip_unless(decl is not None, "spawn of a non-service")
        params = decl.factory.params if decl.factory is not None else decl.fields
        capacity = f"(capacity: {e.capacity}) " if e.capacity is not None else " "
        return f"spawn{capacity}{e.tyname}.create({self.args_text(params, e.args, declared_raw=decl.factory is None, moves=True)})"

    def await_text(self, e):
        if isinstance(e.promise, C.Var):
            return f"await {self.expr(e.promise)}"
        skip_unless(isinstance(e.promise, C.Send), "await of a promise")
        return f"try await {self.send_promise_text(e.promise)[len('try '):]}"


def translate(prog):
    return Translator(prog).translate()
