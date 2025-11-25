; Windows-specific implementations using Windows API

target triple = "x86_64-pc-windows-msvc"

; _fltused - Windows floating-point support symbol
; Required when using floating-point operations on Windows
@_fltused = constant i32 39029

; __chkstk - stack probe for large stack allocations
; This is called by LLVM when allocating large stack arrays
; Windows requires stack probing to ensure stack pages are committed
define void @__chkstk() {
entry:
  ret void
}

; Declare Windows API functions
declare ptr @GetProcessHeap() #0
declare ptr @HeapAlloc(ptr, i32, i64) #0
declare i1 @HeapFree(ptr, i32, ptr) #0
declare void @ExitProcess(i32) #0
declare ptr @GetStdHandle(i32) #0
declare i32 @WriteFile(ptr, ptr, i32, ptr, ptr) #0

; malloc - Allocate memory using Windows HeapAlloc
; ptr malloc(i64 size)
define ptr @malloc(i64 %size) {
entry:
  %heap = call ptr @GetProcessHeap()
  %flags = add i32 0, 0  ; flags = 0
  %ptr = call ptr @HeapAlloc(ptr %heap, i32 %flags, i64 %size)
  ret ptr %ptr
}

; free - Free memory using Windows HeapFree
; void free(ptr ptr)
define void @free(ptr %ptr) {
entry:
  %heap = call ptr @GetProcessHeap()
  %flags = add i32 0, 0  ; flags = 0
  call i1 @HeapFree(ptr %heap, i32 %flags, ptr %ptr)
  ret void
}

; exit - Exit the process
; void exit(i32 code)
define void @exit(i32 %code) {
entry:
  call void @ExitProcess(i32 %code)
  unreachable
}

; write_stdout - Write to stdout using Windows API
; i32 write_stdout(ptr buf, i32 count)
define i32 @write_stdout(ptr %buf, i32 %count) {
entry:
  ; Allocate space for bytes written
  %bytesWritten = alloca i32
  
  ; Get stdout handle (STD_OUTPUT_HANDLE = -11)
  %handle = call ptr @GetStdHandle(i32 -11)
  
  ; Call WriteFile
  %success = call i32 @WriteFile(ptr %handle, ptr %buf, i32 %count, ptr %bytesWritten, ptr null)
  
  ; Check if WriteFile succeeded
  %failed = icmp eq i32 %success, 0
  br i1 %failed, label %error, label %success_case
  
success_case:
  ; Load and return bytes written
  %written = load i32, ptr %bytesWritten
  ret i32 %written

error:
  ; Return -1 on error
  ret i32 -1
}

attributes #0 = { "no-trapping-math"="true" }
