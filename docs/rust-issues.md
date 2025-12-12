🧩 1. Unsafe code

Rust allows unsafe blocks to do things outside the borrow checker’s rules:

Dereferencing raw pointers

Calling FFI (foreign functions)

Accessing mutable statics

Implementing unsafe traits incorrectly

Rust assumes the developer upholds the “unsafe contract.” If not, you can have undefined behavior — the same kind of memory corruption as in C/C++.
→ Example: aliasing two mutable pointers to the same memory.

🪲 2. Logic and algorithmic bugs

The borrow checker doesn’t protect against wrong logic:

Off-by-one errors

Incorrect arithmetic or comparisons

Forgetting to handle certain input cases

Deadlocks from improper lock usage

Rust ensures memory safety, not correctness.

⚖️ 3. Concurrency issues (still possible)

While Rust eliminates data races, it can’t prevent logical concurrency errors:

Deadlocks (e.g. acquiring locks in inconsistent order)

Starvation or priority inversion

Incorrect atomic operations

Sending/receiving messages in the wrong order

🧮 4. Integer overflows

By default:

In debug builds, overflows panic.

In release builds, they wrap around silently (unless you use checked/saturating operations).

So math bugs can still sneak in under optimization.

🌍 5. FFI (Foreign Function Interface) vulnerabilities

When interfacing with C, C++, or OS APIs, Rust’s safety guarantees stop at the boundary:

Passing invalid pointers or type layouts

Mismatched ownership or lifetimes

C libraries doing unsafe things under the hood

💾 6. Unsafe resource handling

Rust enforces memory cleanup via RAII, but not all resources are memory:

Files left open too long

Network sockets not properly closed

Database transactions left hanging

These are semantic leaks, not memory leaks.

🔐 7. Security bugs (logic-level)

Rust prevents memory vulnerabilities, but not:

SQL injection

Logic flaws in authentication

Timing side channels

Insecure cryptographic practices

Safety ≠ Security.

📦 8. Build & dependency issues

The safety of your code depends on:

unsafe in dependencies (common in performance-sensitive crates)

Semver-breaking changes

Supply-chain attacks in the Rust ecosystem

🌀 9. Performance pitfalls

Even though Rust gives control, you can still:

Cause unnecessary allocations

Overuse clone()

Use Arc<Mutex<T>> where it’s not needed

Cause cache-unfriendly data layouts

Rust won’t optimize for you automatically.

🧱 10. Misuse of lifetimes and ownership semantics

Sometimes developers fight the borrow checker until they “make it compile” by:

Using Rc<RefCell<T>> or Arc<Mutex<T>> everywhere

Overusing clone() to sidestep ownership
→ This can result in bloated, slow, or conceptually unsafe code — though still memory-safe.
