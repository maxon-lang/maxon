#include "../include/document_manager.h"
#include "catch_amalgamated.hpp"
#include <iostream>

#define CATCH_CONFIG_MAIN

TEST_CASE("open_document", "[document_manager]") {

    
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    std::string text = "function main() end";
    
    manager.openDocument(uri, text, 0);
    auto doc = manager.getDocument(uri);
    
    REQUIRE(doc != nullptr);
    REQUIRE(doc->uri == uri);
    REQUIRE(doc->text == text);
    REQUIRE(doc->version == 0);
    

}

TEST_CASE("update_document", "[document_manager]") {

    
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    
    manager.openDocument(uri, "original text", 0);
    manager.updateDocument(uri, "updated text", 1);
    
    auto doc = manager.getDocument(uri);
    REQUIRE(doc->text == "updated text");
    REQUIRE(doc->version == 1);
    

}

TEST_CASE("close_document", "[document_manager]") {

    
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    
    manager.openDocument(uri, "some text", 0);
    REQUIRE(manager.getDocument(uri) != nullptr);
    
    manager.closeDocument(uri);
    REQUIRE(manager.getDocument(uri) == nullptr);
    

}

TEST_CASE("get_nonexistent_document", "[document_manager]") {

    
    DocumentManager manager;
    auto doc = manager.getDocument("file:///nonexistent.maxon");
    
    REQUIRE(doc == nullptr);
    

}

TEST_CASE("multiple_documents", "[document_manager]") {

    
    DocumentManager manager;
    
    manager.openDocument("file:///doc1.maxon", "content 1", 0);
    manager.openDocument("file:///doc2.maxon", "content 2", 0);
    manager.openDocument("file:///doc3.maxon", "content 3", 0);
    
    REQUIRE(manager.getDocument("file:///doc1.maxon")->text == "content 1");
    REQUIRE(manager.getDocument("file:///doc2.maxon")->text == "content 2");
    REQUIRE(manager.getDocument("file:///doc3.maxon")->text == "content 3");
    

}

TEST_CASE("document_versioning", "[document_manager]") {


    DocumentManager manager;
    std::string uri = "file:///test.maxon";

    manager.openDocument(uri, "v0", 0);
    REQUIRE(manager.getDocument(uri)->version == 0);

    manager.updateDocument(uri, "v1", 1);
    REQUIRE(manager.getDocument(uri)->version == 1);

    manager.updateDocument(uri, "v2", 2);
    REQUIRE(manager.getDocument(uri)->version == 2);


}

TEST_CASE("test_fragment_strips_after_separator", "[document_manager][test_fragments]") {
    DocumentManager manager;
    std::string uri = "file:///c:/Users/Eric/Dev/maxon/language-tests/fragments/test.test";
    std::string fullText =
        "// Test: test\n"
        "function main() int\n"
        "    return 42\n"
        "end 'main'\n"
        "---\n"
        "; Module: test.maxon\n"
        "; Instructions: 10\n";

    std::string expectedText =
        "// Test: test\n"
        "function main() int\n"
        "    return 42\n"
        "end 'main'";

    manager.openDocument(uri, fullText, 1);

    auto doc = manager.getDocument(uri);
    REQUIRE(doc != nullptr);
    REQUIRE(doc->text == expectedText);
}

TEST_CASE("test_fragment_update_strips_content", "[document_manager][test_fragments]") {
    DocumentManager manager;
    std::string uri = "file:///c:/Users/Eric/Dev/maxon/language-tests/fragments/test.test";
    std::string text1 = "var x: i32 = 5\n---\nIR output";
    std::string text2 = "var x: i32 = 10\n---\nDifferent IR";

    manager.openDocument(uri, text1, 1);
    manager.updateDocument(uri, text2, 2);

    auto doc = manager.getDocument(uri);
    REQUIRE(doc != nullptr);
    REQUIRE(doc->text == "var x: i32 = 10");
    REQUIRE(doc->version == 2);
}

TEST_CASE("regular_test_file_not_stripped", "[document_manager][test_fragments]") {
    DocumentManager manager;
    std::string uri = "file:///c:/Users/Eric/Dev/maxon/examples/test.test";
    std::string fullText =
        "function main() int\n"
        "    var separator = \"---\"\n"
        "    return 42\n"
        "end 'main'";

    manager.openDocument(uri, fullText, 1);

    auto doc = manager.getDocument(uri);
    REQUIRE(doc != nullptr);
    REQUIRE(doc->text == fullText);  // Should not be stripped
}

TEST_CASE("test_fragment_without_separator_unchanged", "[document_manager][test_fragments]") {
    DocumentManager manager;
    std::string uri = "file:///c:/Users/Eric/Dev/maxon/language-tests/fragments/test.test";
    std::string text = "function main() int\n    return 42\nend 'main'";

    manager.openDocument(uri, text, 1);

    auto doc = manager.getDocument(uri);
    REQUIRE(doc != nullptr);
    REQUIRE(doc->text == text);  // No separator, so unchanged
}

TEST_CASE("unix_path_test_fragment", "[document_manager][test_fragments]") {
    DocumentManager manager;
    std::string uri = "file:///home/user/maxon/language-tests/fragments/test.test";
    std::string fullText = "var x: i32 = 5\n---\nIR output";

    manager.openDocument(uri, fullText, 1);

    auto doc = manager.getDocument(uri);
    REQUIRE(doc != nullptr);
    REQUIRE(doc->text == "var x: i32 = 5");
}
