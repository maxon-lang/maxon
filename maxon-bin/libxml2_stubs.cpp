// Stub implementations for LibXml2 functions required by LLVMWindowsManifest
// These are never actually called, but LLVM requires them at link time on Windows
// when linking with lldCOFF.

#ifdef _WIN32

extern "C" {

void xmlSetGenericErrorFunc(void *ctx, void *handler) {}

void *xmlReadMemory(const char *buffer, int size, const char *URL,
					const char *encoding, int options) {
	return nullptr;
}

void *xmlDocGetRootElement(void *doc) {
	return nullptr;
}

void xmlFreeDoc(void *doc) {}

void xmlUnlinkNode(void *node) {}

void xmlFreeNode(void *node) {}

void *xmlNewProp(void *node, const unsigned char *name, const unsigned char *value) {
	return nullptr;
}

unsigned char *xmlStrdup(const unsigned char *str) {
	return nullptr;
}

void *xmlCopyNamespace(void *ns) {
	return nullptr;
}

void xmlFree(void *ptr) {}

void *xmlAddChild(void *parent, void *cur) {
	return nullptr;
}

void *xmlNewDoc(const unsigned char *version) {
	return nullptr;
}

void xmlDocSetRootElement(void *doc, void *root) {}

void xmlDocDumpFormatMemoryEnc(void *doc, unsigned char **mem, int *size,
							   const char *encoding, int format) {}

void xmlFreeNs(void *ns) {}

void *xmlNewNs(void *node, const unsigned char *href, const unsigned char *prefix) {
	return nullptr;
}

} // extern "C"

#endif // _WIN32
