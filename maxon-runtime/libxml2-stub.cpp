// Stub implementations for LibXml2 functions that LLVM's WindowsManifest requires
// We don't actually use manifest merging, so these stubs are never called
//
// Also includes math function stubs that LLVM's intrinsic lowering may require

extern "C" {

// LibXml2 stubs
void xmlSetGenericErrorFunc(void *ctx, void *handler) {}
void* xmlReadMemory(const char *buffer, int size, const char *URL, const char *encoding, int options) { return 0; }
void* xmlDocGetRootElement(void *doc) { return 0; }
void xmlFreeDoc(void *doc) {}
void xmlUnlinkNode(void *node) {}
void xmlFreeNode(void *node) {}
void* xmlNewProp(void *node, const unsigned char *name, const unsigned char *value) { return 0; }
unsigned char* xmlStrdup(const unsigned char *str) { return 0; }
void* xmlCopyNamespace(void *ns) { return 0; }
void xmlFree(void *ptr) {}
void* xmlAddChild(void *parent, void *cur) { return 0; }
void* xmlNewDoc(const unsigned char *version) { return 0; }
void xmlDocSetRootElement(void *doc, void *root) {}
void xmlDocDumpFormatMemoryEnc(void *doc, unsigned char **mem, int *size, const char *encoding, int format) {}
void xmlFreeNs(void *ns) {}
void* xmlNewNs(void *node, const unsigned char *href, const unsigned char *prefix) { return 0; }

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
