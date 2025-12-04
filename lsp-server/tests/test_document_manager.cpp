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

// Tests for incremental document sync

TEST_CASE("incremental_change_single_char_insert", "[document_manager][incremental]") {
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    std::string originalText = "function main() int\n    return 42\nend 'main'";

    manager.openDocument(uri, originalText, 1);

    // Insert a character at position (1, 11) - after "return "
    lsp::Range range;
    range.start.line = 1;
    range.start.character = 11;
    range.end.line = 1;
    range.end.character = 11;

    bool success = manager.applyIncrementalChange(uri, range, "1", 2);
    REQUIRE(success);

    auto doc = manager.getDocument(uri);
    REQUIRE(doc != nullptr);
    REQUIRE(doc->text == "function main() int\n    return 142\nend 'main'");
    REQUIRE(doc->version == 2);
}

TEST_CASE("incremental_change_delete_text", "[document_manager][incremental]") {
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    std::string originalText = "function main() int\n    return 42\nend 'main'";

    manager.openDocument(uri, originalText, 1);

    // Delete "42" - positions (1, 11) to (1, 13)
    lsp::Range range;
    range.start.line = 1;
    range.start.character = 11;
    range.end.line = 1;
    range.end.character = 13;

    bool success = manager.applyIncrementalChange(uri, range, "", 2);
    REQUIRE(success);

    auto doc = manager.getDocument(uri);
    REQUIRE(doc != nullptr);
    REQUIRE(doc->text == "function main() int\n    return \nend 'main'");
}

TEST_CASE("incremental_change_replace_text", "[document_manager][incremental]") {
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    std::string originalText = "function main() int\n    return 42\nend 'main'";

    manager.openDocument(uri, originalText, 1);

    // Replace "42" with "100"
    lsp::Range range;
    range.start.line = 1;
    range.start.character = 11;
    range.end.line = 1;
    range.end.character = 13;

    bool success = manager.applyIncrementalChange(uri, range, "100", 2);
    REQUIRE(success);

    auto doc = manager.getDocument(uri);
    REQUIRE(doc != nullptr);
    REQUIRE(doc->text == "function main() int\n    return 100\nend 'main'");
}

TEST_CASE("incremental_change_multiline_insert", "[document_manager][incremental]") {
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    std::string originalText = "function main() int\nend 'main'";

    manager.openDocument(uri, originalText, 1);

    // Insert a new line at position (0, 19) - after "int"
    lsp::Range range;
    range.start.line = 0;
    range.start.character = 19;
    range.end.line = 0;
    range.end.character = 19;

    bool success = manager.applyIncrementalChange(uri, range, "\n    return 42", 2);
    REQUIRE(success);

    auto doc = manager.getDocument(uri);
    REQUIRE(doc != nullptr);
    REQUIRE(doc->text == "function main() int\n    return 42\nend 'main'");
}

TEST_CASE("incremental_change_multiline_delete", "[document_manager][incremental]") {
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    std::string originalText = "function main() int\n    var x = 1\n    var y = 2\n    return x + y\nend 'main'";

    manager.openDocument(uri, originalText, 1);

    // Delete lines 1 and 2 (var x and var y)
    lsp::Range range;
    range.start.line = 1;
    range.start.character = 0;
    range.end.line = 3;
    range.end.character = 0;

    bool success = manager.applyIncrementalChange(uri, range, "", 2);
    REQUIRE(success);

    auto doc = manager.getDocument(uri);
    REQUIRE(doc != nullptr);
    REQUIRE(doc->text == "function main() int\n    return x + y\nend 'main'");
}

TEST_CASE("incremental_change_nonexistent_document", "[document_manager][incremental]") {
    DocumentManager manager;
    std::string uri = "file:///nonexistent.maxon";

    lsp::Range range;
    range.start.line = 0;
    range.start.character = 0;
    range.end.line = 0;
    range.end.character = 0;

    bool success = manager.applyIncrementalChange(uri, range, "test", 1);
    REQUIRE(!success);
}

TEST_CASE("incremental_change_at_end_of_document", "[document_manager][incremental]") {
    DocumentManager manager;
    std::string uri = "file:///test.maxon";
    std::string originalText = "var x = 1";

    manager.openDocument(uri, originalText, 1);

    // Append at end of document
    lsp::Range range;
    range.start.line = 0;
    range.start.character = 9;
    range.end.line = 0;
    range.end.character = 9;

    bool success = manager.applyIncrementalChange(uri, range, "\nvar y = 2", 2);
    REQUIRE(success);

    auto doc = manager.getDocument(uri);
    REQUIRE(doc != nullptr);
    REQUIRE(doc->text == "var x = 1\nvar y = 2");
}

TEST_CASE("get_all_document_uris", "[document_manager]") {
    DocumentManager manager;

    manager.openDocument("file:///doc1.maxon", "content 1", 1);
    manager.openDocument("file:///doc2.maxon", "content 2", 1);
    manager.openDocument("file:///doc3.maxon", "content 3", 1);

    auto uris = manager.getAllDocumentUris();
    REQUIRE(uris.size() == 3);

    // Check that all URIs are present (order may vary)
    bool hasDoc1 = std::find(uris.begin(), uris.end(), "file:///doc1.maxon") != uris.end();
    bool hasDoc2 = std::find(uris.begin(), uris.end(), "file:///doc2.maxon") != uris.end();
    bool hasDoc3 = std::find(uris.begin(), uris.end(), "file:///doc3.maxon") != uris.end();

    REQUIRE(hasDoc1);
    REQUIRE(hasDoc2);
    REQUIRE(hasDoc3);
}

TEST_CASE("get_all_document_uris_empty", "[document_manager]") {
    DocumentManager manager;

    auto uris = manager.getAllDocumentUris();
    REQUIRE(uris.empty());
}

TEST_CASE("document_get_offset", "[document_manager]") {
    std::string text = "line 0\nline 1\nline 2";
    Document doc("file:///test.maxon", text, 1);

    // Test various positions
    REQUIRE(doc.getOffset(0, 0) == 0);   // Start of doc
    REQUIRE(doc.getOffset(0, 5) == 5);   // Middle of line 0
    REQUIRE(doc.getOffset(1, 0) == 7);   // Start of line 1 (after "line 0\n")
    REQUIRE(doc.getOffset(1, 4) == 11);  // "line" in line 1
    REQUIRE(doc.getOffset(2, 0) == 14);  // Start of line 2
    REQUIRE(doc.getOffset(2, 6) == 20);  // End of doc
}
