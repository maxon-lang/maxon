// Stub implementations for library functions that LLVM's intrinsic lowering may require
// These are linked with user programs along with runtime.obj

extern "C" {

// Math function stubs for LLVM intrinsic lowering
// These provide basic implementations when LLVM lowers intrinsics to library calls
double trunc(double x) {
    return (double)(long long)x;
}

double fmod(double x, double y) {
    long long quotient = (long long)(x / y);
    return x - quotient * y;
}

}
