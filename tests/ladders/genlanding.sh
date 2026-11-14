#!/usr/bin/env bash
# Ladder generator for CALLS THAT LAND ON A SET OF BODIES — a call through a function-typed parameter
# or field, and a call through an interface-typed value. `SemanticCheck`, the Provenance tracer and the
# closure-cycle checker each judge such a call against every body it may land on, so the cost of N
# same-shape closures (or N conformers) read by N call sites is N x N unless the bodies are shared
# between the sites. `ScaleCorpus` generates a closure only as a local called by name and no interface
# value, so every column of a default run reads a flat delta for this path.
#
# Usage: genlanding.sh <N> <param|paramactive|paramkeep|files|filesdirect|shapes|shapesdirect|witness|witnessdirect|generic|genericfns> <out>
#
# `<out>` is a file, except `files`, `filesdirect`, `shapes` and `shapesdirect`, which write a directory
# of N+2 sources that build as one program.
#
#   param          N units, each with a capturing closure of ONE shape `function(Tally) returns Tally`
#                  handed to its own `apply<k>(f Op, x Tally)`, so N call sites through a parameter land
#                  by shape on all N closure bodies
#   paramactive    the same, with a non-empty summary in every body: each takes and returns a `Box` record
#                  that holds a function-typed field and captures `scale`, so the Provenance tracer and
#                  the closure-cycle checker both have a row to apply per body
#   paramkeep      the same, routed through a parameter that hands the callee an `Array with Box` (a
#                  generic instance), and the callee keeps that array by pushing it into a `Shelf`, so the
#                  Provenance tracer's row for every body is a keeping row at a generic-instance receiver
#   files          `param` with each unit in its own file: one closure of the shape and one `apply<k>`
#   filesdirect    the CONTROL for `files`: the same N files and N functions, each unit calling a plain
#                  function by name, so no function value exists
#   shapes         N files, each with its OWN record type, a function-typed alias over it, one closure of that
#                  shape and one call through the alias: N distinct shapes, N readers, one body each
#   shapesdirect   the CONTROL for `shapes`: the same N files and record types, each unit calling a plain
#                  function by name, so no function value exists
#   witness        N conformers of one interface, and N functions that each call a method of an
#                  interface-typed field, so N witness dispatch sites land on N conformers
#   witnessdirect  the CONTROL for `witness`: the same N conformers and N call sites, each site calling
#                  the method on its own concrete type, so nothing dispatches
#   generic        N call sites of a method of one generic type whose callback is typed over the type
#                  parameter (`typealias Step = function(T) returns T`), each handed a function value
#                  loaded from a record field
#   genericfns     N methods of one generic type, each taking a callback typed over the type parameter,
#                  and N call sites, one per method, each handed a function value loaded from a record
#                  field: N type-parameter callbacks over one set of function values
#
# Every mode returns 0 at every size, so a rung that miscompiles shows up as a wrong exit code.
#
# ⚠ Names are written without padding because the counts are compared across a doubling ladder, where a
# digit the name gains moves the byte columns by the same amount at every rung.
set -euo pipefail
N="$1"; SHAPE="$2"; OUT="$3"

case "$SHAPE" in
  param|paramactive|paramkeep|files|filesdirect|shapes|shapesdirect|witness|witnessdirect|generic|genericfns) ;;
  *) echo "genlanding.sh: shape must be param, paramactive, paramkeep, files, filesdirect, shapes, shapesdirect, witness, witnessdirect, generic or genericfns, got '$SHAPE'" >&2; exit 2 ;;
esac

if [ "$N" -lt 1 ]; then
  echo "genlanding.sh: <N> must be at least 1" >&2
  exit 2
fi

emit_box() {
  printf 'typealias Hook = function(Tally) returns Tally\ntypealias BoxArray = Array with Box\ntypealias Pass = function(Box) returns Box\n\ntype Box\n\tvar weight as Tally\n\tvar hook as Hook\n\n\tstatic function create(weight Tally, hook Hook) returns Box\n\t\treturn Box{weight: weight, hook: hook}\n\tend '\''create'\''\n\n\tfunction heavy() returns Tally\n\t\treturn self.hook(self.weight)\n\tend '\''heavy'\''\nend '\''Box'\''\n\nfunction identityHook(n Tally) returns Tally\n\treturn n\nend '\''identityHook'\''\n\n'
}

emit_shelf() {
  printf 'typealias BoxArray = Array with Box\ntypealias ShelfArray = Array with BoxArray\ntypealias Keep = function(mutable BoxArray, mutable Shelf) returns Tally\n\ntype Box\n\texport var weight as Tally\n\n\tstatic function create(weight Tally) returns Box\n\t\treturn Box{weight: weight}\n\tend '\''create'\''\nend '\''Box'\''\n\ntype Shelf\n\tvar rows as ShelfArray\n\n\tstatic function create() returns Shelf\n\t\treturn Shelf{rows: ShelfArray.create(0)}\n\tend '\''create'\''\n\n\tfunction keep(items mutable BoxArray) returns Tally\n\t\tself.rows.push(items)\n\t\treturn self.rows.count() as Tally\n\tend '\''keep'\''\nend '\''Shelf'\''\n\n'
}

emit_conformer() {
  local k="$1"
  printf 'type Sq%d implements Shape\n\tvar edge as Tally\n\n\tstatic function create(edge Tally) returns Self\n\t\treturn Self{edge: edge}\n\tend '\''create'\''\n\n\tfunction side() returns Tally\n\t\treturn self.edge\n\tend '\''side'\''\nend '\''Sq%d'\''\n\n' "$k" "$k"
}

emit_total() {
  local k="$1" body="$2"
  printf 'function total%d(scale Tally = 2) returns Tally\n' "$k"
  printf '%s' "${body//@K@/$k}"
  printf 'end '\''total%d'\''\n\n' "$k"
}

emit_unit() {
  local k="$1" kind="$2"
  case "$kind" in
    param)
      printf 'function apply%d(f Op, x Tally) returns Tally\n\treturn f(x)\nend '\''apply%d'\''\n\n' "$k" "$k"
      emit_total "$k" $'\tlet bump = function(n Tally) gives n + scale\n\n\treturn apply@K@(bump, x: scale)\n'
      ;;
    direct)
      printf 'function apply%d(x Tally) returns Tally\n\treturn x\nend '\''apply%d'\''\n\n' "$k" "$k"
      emit_total "$k" $'\treturn apply@K@(scale)\n'
      ;;
    shape)
      printf 'type Item%d\n\tvar weight as Tally\n\n\tstatic function create(weight Tally) returns Self\n\t\treturn Self{weight: weight}\n\tend '\''create'\''\n\n\tfunction value() returns Tally\n\t\treturn self.weight\n\tend '\''value'\''\nend '\''Item%d'\''\n\n' "$k" "$k"
      printf 'typealias Op%d = function(Item%d) returns Tally\n\n' "$k" "$k"
      printf 'function apply%d(f Op%d, item Item%d) returns Tally\n\treturn f(item)\nend '\''apply%d'\''\n\n' "$k" "$k" "$k" "$k"
      emit_total "$k" $'\tlet bump = function(i Item@K@) gives i.value() + scale\n\n\treturn apply@K@(bump, item: Item@K@.create(scale))\n'
      ;;
    shapedirect)
      printf 'type Item%d\n\tvar weight as Tally\n\n\tstatic function create(weight Tally) returns Self\n\t\treturn Self{weight: weight}\n\tend '\''create'\''\n\n\tfunction value() returns Tally\n\t\treturn self.weight\n\tend '\''value'\''\nend '\''Item%d'\''\n\n' "$k" "$k"
      printf 'function apply%d(item Item%d) returns Tally\n\treturn item.value()\nend '\''apply%d'\''\n\n' "$k" "$k" "$k"
      emit_total "$k" $'\treturn apply@K@(Item@K@.create(scale))\n'
      ;;
    paramactive)
      printf 'function apply%d(f Pass, b Box) returns Box\n\treturn f(b)\nend '\''apply%d'\''\n\n' "$k" "$k"
      emit_total "$k" $'\tvar kept = BoxArray.create(0)\n\tlet echo = function(b Box) gives b if scale > 1 else Box.create(scale, hook: identityHook)\n\tvar got = apply@K@(echo, b: Box.create(scale, hook: identityHook))\n\tkept.push(got)\n\n\treturn got.heavy() + (kept.count() as Tally)\n'
      ;;
    paramkeep)
      printf 'function keep%d(items mutable BoxArray, home mutable Shelf) returns Tally\n\treturn home.keep(items)\nend '\''keep%d'\''\n\n' "$k" "$k"
      printf 'function apply%d(f Keep, items mutable BoxArray, shelf mutable Shelf) returns Tally\n\treturn f(items, shelf)\nend '\''apply%d'\''\n\n' "$k" "$k"
      emit_total "$k" $'\tvar shelf = Shelf.create()\n\tvar boxes = BoxArray.create(0)\n\tboxes.push(Box.create(scale))\n\n\treturn apply@K@(keep@K@, items: boxes, shelf: shelf)\n'
      ;;
    witness)
      emit_conformer "$k"
      printf 'function look%d(h Holder) returns Tally\n\treturn h.shape.side()\nend '\''look%d'\''\n\n' "$k" "$k"
      ;;
    witnessdirect)
      emit_conformer "$k"
      printf 'function look%d(s Sq%d) returns Tally\n\treturn s.side()\nend '\''look%d'\''\n\n' "$k" "$k" "$k"
      ;;
    generic)
      emit_total "$k" $'\tlet h = Holder.create(function(n Tally) gives n + scale)\n\tlet r = Runner.create(scale)\n\n\treturn r.apply(h.step)\n'
      ;;
    genericfns)
      emit_total "$k" $'\tlet h = Holder.create(function(n Tally) gives n + scale)\n\tlet r = Runner.create(scale)\n\n\treturn r.apply@K@(h.step)\n'
      ;;
  esac
}

emit_generic_runner_methods() {
  local k=0
  while [ "$k" -lt "$N" ]; do
    printf '\n\tfunction apply%d(f Step) returns T\n\t\treturn f(self.seed)\n\tend '\''apply%d'\''\n' "$k" "$k"
    k=$(( k + 1 ))
  done
}

emit_prelude() {
  local kind="$1"
  echo "typealias Tally = int(0 to 1000000)"

  case "$kind" in
    param)
      echo "typealias Op = function(Tally) returns Tally"
      ;;
    paramactive)
      emit_box
      ;;
    paramkeep)
      emit_shelf
      ;;
    witness)
      printf 'interface Shape\n\tfunction side() returns Tally\nend '\''Shape'\''\n\ntype Holder\n\texport var shape as Shape\n\n\tstatic function create(shape Shape) returns Self\n\t\treturn Self{shape: shape}\n\tend '\''create'\''\nend '\''Holder'\''\n'
      ;;
    witnessdirect)
      printf 'interface Shape\n\tfunction side() returns Tally\nend '\''Shape'\''\n'
      ;;
    generic)
      printf 'typealias Pass = function(Tally) returns Tally\n\ntype Holder\n\texport var step as Pass\n\n\tstatic function create(step Pass) returns Self\n\t\treturn Self{step: step}\n\tend '\''create'\''\nend '\''Holder'\''\n\ntype GenericRunner uses T\n\ttypealias Step = function(T) returns T\n\n\tvar seed as T\n\n\tstatic function create(seed T) returns Self\n\t\treturn Self{seed: seed}\n\tend '\''create'\''\n\n\tfunction apply(f Step) returns T\n\t\treturn f(self.seed)\n\tend '\''apply'\''\nend '\''GenericRunner'\''\n\ntypealias Runner = GenericRunner with Tally\n'
      ;;
    genericfns)
      printf 'typealias Pass = function(Tally) returns Tally\n\ntype Holder\n\texport var step as Pass\n\n\tstatic function create(step Pass) returns Self\n\t\treturn Self{step: step}\n\tend '\''create'\''\nend '\''Holder'\''\n\ntype GenericRunner uses T\n\ttypealias Step = function(T) returns T\n\n\tvar seed as T\n\n\tstatic function create(seed T) returns Self\n\t\treturn Self{seed: seed}\n\tend '\''create'\''\n'
      emit_generic_runner_methods
      printf 'end '\''GenericRunner'\''\n\ntypealias Runner = GenericRunner with Tally\n'
      ;;
  esac

  echo ""
}

emit_main() {
  local kind="$1" k=0
  echo "function main() returns ExitCode"
  echo -e "\tvar acc = 0 as Tally"

  while [ "$k" -lt "$N" ]; do
    case "$kind" in
      witness) printf '\tacc = (acc + look%d(Holder.create(Sq%d.create(%d)))) and 7\n' "$k" "$k" "$(( k % 100 ))" ;;
      witnessdirect) printf '\tacc = (acc + look%d(Sq%d.create(%d))) and 7\n' "$k" "$k" "$(( k % 100 ))" ;;
      *) printf '\tacc = (acc + total%d()) and 7\n' "$k" ;;
    esac

    k=$(( k + 1 ))
  done

  echo -e "\treturn 0"
  echo "end 'main'"
}

if [ "$SHAPE" = "files" ] || [ "$SHAPE" = "filesdirect" ] || [ "$SHAPE" = "shapes" ] || [ "$SHAPE" = "shapesdirect" ]; then
  mkdir -p "$OUT"
  unit_kind="param"
  {
    echo "// ladder: $N file(s), shape $SHAPE"
    echo "typealias Tally = int(0 to 1000000)"

    case "$SHAPE" in
      files) echo "typealias Op = function(Tally) returns Tally" ;;
      filesdirect) unit_kind="direct" ;;
      shapes) unit_kind="shape" ;;
      shapesdirect) unit_kind="shapedirect" ;;
    esac
  } > "$OUT/shared.maxon"

  k=0
  while [ "$k" -lt "$N" ]; do
    emit_unit "$k" "$unit_kind" > "$OUT/unit$k.maxon"
    k=$(( k + 1 ))
  done

  emit_main "$unit_kind" > "$OUT/main.maxon"
  exit 0
fi

{
  echo "// ladder: $N unit(s), shape $SHAPE"
  emit_prelude "$SHAPE"

  k=0
  while [ "$k" -lt "$N" ]; do
    emit_unit "$k" "$SHAPE"
    k=$(( k + 1 ))
  done

  emit_main "$SHAPE"
} > "$OUT"
