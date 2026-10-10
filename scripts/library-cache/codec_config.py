import os

from maxon_source import FIELD_BOOL_TYPE, FIELD_STRING_TYPE, ROOT

COMPILER_DIRECTORY = os.path.join(ROOT, "maxon-bin", "Compiler")

GENERATED_HOST = os.path.join(COMPILER_DIRECTORY, "LibraryCacheGenerated.maxon")

ENTRY_FILES = [os.path.join(COMPILER_DIRECTORY, "LibraryCacheCodecs.maxon")]

PROTECTED_FILES = {"ErrorCodeRegistry.maxon"}

CUSTOM_TYPES = {
    "MaxonModule",
    "TypeNameInterner",
    "FixedElementCountColumn",
    "SourceParseError",
    "Token",
    "SharedLibraryParse",
}

REFUSING_TYPES = {
    "DeferredParseError",
    "ConditionalRefusal",
    "MisclosedBlockSite",
}

SKIPPED_FIELDS = {
    ("ProgramSignatures", "baseline"): "IndexBaseline.unsettled()",
    ("ProgramSignatures", "journal"): "IndexJournal.create()",
    ("ProgramSignatures", "rowSetLog"): "RowSetLog.closed()",
    ("ProgramSignatures", "mintedTupleLayouts"): "StructLayoutArray.create()",
    ("ProgramSignatures", "published"): "SettledCellArray.create()",
    ("SettledDeclarations", "genericInstanceNameMarker"): "genericInstanceNameMarker()",
    ("SettledDeclarations", "declTokenStreams"): "DeclTokenStreamMap.create()",
}

ROOTS = [
    "FileParseArtifact",
    "ParseFootprint",
    "IrFunction",
    "IrBlock",
    "MaxonOp",
    "DebugSpanColumn",
    ("alias", "IrFunctionArray"),
    ("alias", "IrBlockArray"),
    ("alias", "TokenArray"),
    ("alias", "FilePathArray"),
    ("alias", "ByteArrayArray"),
    "ProgramSignatures",
]

HAND_CODEC_ROOTS = ["TokenKind"]

SIGNED_LOWER_BOUNDS = ("i64.min", "i32.min", "i16.min", "i8.min")

UNSIGNED_FLOOR = "0"
UNSIGNED_CEILING = "u64.max"
SIGNED_FLOOR = "i64.min"
SIGNED_CEILING = "i64.max"
WORD_BITS = 64

HAND_CODEC_FILES = [
    os.path.join(COMPILER_DIRECTORY, "ContentHash.maxon"),
    os.path.join(COMPILER_DIRECTORY, "LibraryCacheCodecs.maxon"),
    os.path.join(COMPILER_DIRECTORY, "LibraryCacheFormat.maxon"),
]

CODEC_SCAN_DIRECTORY = os.path.join(ROOT, "maxon-bin")

HAND_READ_ALIASES = ["ProducerMask", "FixedElementCount"]

FINGERPRINT_HEX_DIGITS = 16

SCHEMA_HOST_TYPES = {"IrModule"}

SLOT_BYTES = 8

SPACED_PAYLOAD_CASES = {"await"}

BUILTIN_STRING = FIELD_STRING_TYPE
BUILTIN_PATH = "FilePath"
BUILTIN_BYTES = "ByteArray"
BUILTIN_BOOL = FIELD_BOOL_TYPE
