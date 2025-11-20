cmake_minimum_required(VERSION 3.15)

if(NOT DEFINED OUTPUT_PATH)
    message(FATAL_ERROR "OUTPUT_PATH not provided to finalize script")
endif()

if(NOT DEFINED BACKUP_PATH)
    message(FATAL_ERROR "BACKUP_PATH not provided to finalize script")
endif()

# Kill any running maxon-lsp-server process
execute_process(
    COMMAND taskkill /IM maxon-lsp-server.exe /F
    RESULT_VARIABLE TASKKILL_RESULT
    OUTPUT_QUIET
    ERROR_QUIET
)

# Delete the backup file if it exists
if(EXISTS "${BACKUP_PATH}")
    file(REMOVE "${BACKUP_PATH}")
endif()
