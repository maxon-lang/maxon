@echo off
REM Build script for FFI Test Library (Windows)
REM Usage: build.bat [debug|release]

setlocal EnableDelayedExpansion

set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=debug

echo Building FFI Test Library (%CONFIG%)...

REM Try MSVC first (cl.exe)
where cl.exe >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo Using MSVC compiler...
    
    if "%CONFIG%"=="debug" (
        set CFLAGS=/Od /Zi /MDd /W4
    ) else (
        set CFLAGS=/O2 /MD /W4
    )
    
    cl.exe /nologo !CFLAGS! /DFFI_TEST_LIB_EXPORTS /LD ffi_test_lib.c /Fe:ffi_test_lib.dll /link /DLL
    
    if %ERRORLEVEL% NEQ 0 (
        echo MSVC build failed!
        exit /b 1
    )
    
    echo Built: ffi_test_lib.dll, ffi_test_lib.lib
    goto :done
)

REM Try MinGW (gcc)
where gcc.exe >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo Using MinGW GCC compiler...
    
    if "%CONFIG%"=="debug" (
        set CFLAGS=-g -O0 -Wall -Wextra
    ) else (
        set CFLAGS=-O2 -Wall -Wextra
    )
    
    gcc.exe !CFLAGS! -shared -DFFI_TEST_LIB_EXPORTS -o ffi_test_lib.dll ffi_test_lib.c -Wl,--out-implib,libffi_test_lib.a
    
    if %ERRORLEVEL% NEQ 0 (
        echo MinGW build failed!
        exit /b 1
    )
    
    echo Built: ffi_test_lib.dll, libffi_test_lib.a
    goto :done
)

REM Try Clang
where clang.exe >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo Using Clang compiler...
    
    if "%CONFIG%"=="debug" (
        set CFLAGS=-g -O0 -Wall -Wextra
    ) else (
        set CFLAGS=-O2 -Wall -Wextra
    )
    
    clang.exe !CFLAGS! -shared -DFFI_TEST_LIB_EXPORTS -o ffi_test_lib.dll ffi_test_lib.c
    
    if %ERRORLEVEL% NEQ 0 (
        echo Clang build failed!
        exit /b 1
    )
    
    echo Built: ffi_test_lib.dll
    goto :done
)

echo ERROR: No C compiler found! Please install MSVC, MinGW, or Clang.
exit /b 1

:done
echo Build complete!
