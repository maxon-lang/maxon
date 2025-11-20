cmake_minimum_required(VERSION 3.15)

if(NOT DEFINED OUTPUT_PATH)
    message(FATAL_ERROR "OUTPUT_PATH not provided to rename script")
endif()

if(NOT DEFINED BACKUP_PATH)
    message(FATAL_ERROR "BACKUP_PATH not provided to rename script")
endif()

if(EXISTS "${OUTPUT_PATH}")
    if(EXISTS "${BACKUP_PATH}")
        file(REMOVE "${BACKUP_PATH}")
    endif()
    file(RENAME "${OUTPUT_PATH}" "${BACKUP_PATH}")
endif()
