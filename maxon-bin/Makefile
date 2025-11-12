BUILD_DIR = build
BIN_DIR = $(BUILD_DIR)/bin
COMPILER = $(BIN_DIR)/maxon.exe

.PHONY: all
all: build

.PHONY: build
build:
	cmake -B $(BUILD_DIR) -G "Unix Makefiles" -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_COMPILER=clang -DCMAKE_CXX_COMPILER=clang++
	cmake --build $(BUILD_DIR)

.PHONY: test  
test: build
	$(COMPILER) sample.maxon -o output.exe
	./output.exe

.PHONY: clean
clean:
	rm -rf $(BUILD_DIR) output.exe temp.o
