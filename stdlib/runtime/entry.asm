; stdlib/runtime/entry.asm
; Minimal entry point for Maxon programs
; This is the ONLY assembly code in the entire stdlib

; External symbols
extern main                    ; User's main function (Maxon)
extern "runtime::exit"         ; Our exit function (Maxon)

; Entry point
global _start

section .text
_start:
    ; Windows x64 calling convention requires:
    ; - 16-byte stack alignment
    ; - 32 bytes "shadow space" for callees to spill register args
    sub rsp, 40                ; Align stack (8) + shadow space (32) = 40
    
    ; Call user's main function
    ; main() -> int (return value in RAX)
    call main
    
    ; RAX contains the return code from main
    ; Call runtime::exit(exit_code)
    ; First argument goes in RCX (Windows x64 calling convention)
    mov rcx, rax
    call "runtime::exit"
    
    ; Should never reach here (exit doesn't return)
    ; But add a breakpoint just in case
    int3
