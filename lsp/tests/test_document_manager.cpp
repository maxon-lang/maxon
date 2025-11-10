#include "../include/document_manager.h"
#include <cassert>
#include <iostream>

void test_open_document() {
    std::cout << "Testing open document..." << std::endl;
    
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    std::string text = "function main() end";
    
    manager.openDocument(uri, text, 0);
    auto doc = manager.getDocument(uri);
    
    assert(doc != nullptr);
    assert(doc->uri == uri);
    assert(doc->text == text);
    assert(doc->version == 0);
    
    std::cout << "✓ Open document works" << std::endl;
}

void test_update_document() {
    std::cout << "Testing update document..." << std::endl;
    
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    
    manager.openDocument(uri, "original text", 0);
    manager.updateDocument(uri, "updated text", 1);
    
    auto doc = manager.getDocument(uri);
    assert(doc->text == "updated text");
    assert(doc->version == 1);
    
    std::cout << "✓ Update document works" << std::endl;
}

void test_close_document() {
    std::cout << "Testing close document..." << std::endl;
    
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    
    manager.openDocument(uri, "some text", 0);
    assert(manager.getDocument(uri) != nullptr);
    
    manager.closeDocument(uri);
    assert(manager.getDocument(uri) == nullptr);
    
    std::cout << "✓ Close document works" << std::endl;
}

void test_get_nonexistent_document() {
    std::cout << "Testing get nonexistent document..." << std::endl;
    
    DocumentManager manager;
    auto doc = manager.getDocument("file:///nonexistent.maxon");
    
    assert(doc == nullptr);
    
    std::cout << "✓ Get nonexistent document returns nullptr" << std::endl;
}

void test_multiple_documents() {
    std::cout << "Testing multiple documents..." << std::endl;
    
    DocumentManager manager;
    
    manager.openDocument("file:///doc1.maxon", "content 1", 0);
    manager.openDocument("file:///doc2.maxon", "content 2", 0);
    manager.openDocument("file:///doc3.maxon", "content 3", 0);
    
    assert(manager.getDocument("file:///doc1.maxon")->text == "content 1");
    assert(manager.getDocument("file:///doc2.maxon")->text == "content 2");
    assert(manager.getDocument("file:///doc3.maxon")->text == "content 3");
    
    std::cout << "✓ Multiple documents work" << std::endl;
}

void test_document_versioning() {
    std::cout << "Testing document versioning..." << std::endl;
    
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    
    manager.openDocument(uri, "v0", 0);
    assert(manager.getDocument(uri)->version == 0);
    
    manager.updateDocument(uri, "v1", 1);
    assert(manager.getDocument(uri)->version == 1);
    
    manager.updateDocument(uri, "v2", 2);
    assert(manager.getDocument(uri)->version == 2);
    
    std::cout << "✓ Document versioning works" << std::endl;
}

int main() {
    std::cout << "Running Document Manager Tests...\n" << std::endl;
    
    try {
        test_open_document();
        test_update_document();
        test_close_document();
        test_get_nonexistent_document();
        test_multiple_documents();
        test_document_versioning();
        
        std::cout << "\n✓ All Document Manager tests passed!" << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "\n✗ Test failed: " << e.what() << std::endl;
        return 1;
    }
}
