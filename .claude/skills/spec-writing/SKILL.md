---
name: spec-writing
description: How to write and maintain spec files for Maxon features. Apply when creating new specs, adding tests to specs, or modifying existing spec files.
---

# Spec File Writing Guide

Spec files are the **source of truth** for Maxon language features. They live in `maxon-bin/specs/`.

## Spec File Format

```markdown
---
id: feature-name
status: implemented | in-progress | planned
---

# Feature Name

## Documentation

User-facing documentation explaining:
- What the feature does
- Syntax and usage
- Examples

## Tests

### Test: descriptive-test-name
<!-- expect: expected output -->
\`\`\`maxon
// minimal test code
\`\`\`

### Test: another-test
<!-- expect: error -->
\`\`\`maxon
// code that should produce an error
\`\`\`
```

## Test Fragment Syntax

### Expected Output
```markdown
### Test: add-numbers
<!-- expect: 42 -->
\`\`\`maxon
print(40 + 2)
\`\`\`
```

### Expected Error
```markdown
### Test: type-mismatch
<!-- expect: error -->
\`\`\`maxon
var x int = "hello"  // type error
\`\`\`
```

## Important Rules

1. **DO NOT edit fragments** - Files in `maxon-bin/specs/fragments/` are auto-generated
2. **Edit the spec file** - Make changes in `maxon-bin/specs/*.md`
3. **Regenerate fragments** - Run `./bin/maxon regen-fragments` after spec changes
4. **Run tests** - Use `cd maxon-bin && zig build spec-test` to verify

## Test Case Guidelines

- One behavior per test
- Use descriptive test names
- Include both success and error cases
- Test edge cases (empty, null, boundaries)
- Keep test code minimal

## Reference

See `docs/SPECS.md` for complete specification format documentation.
