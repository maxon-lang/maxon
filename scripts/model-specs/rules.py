# The model names the first rule that fails and the compiler reports every independent error, so a refusal
# outside RULE_CODES but inside CAPABILITY_CODES still agrees; any other code is the translation failing.

from __future__ import annotations

CAPABILITY_CODES = {f"E{n}" for n in range(3196, 3218)} | {
    "E3102", "E3186", "E3216", "E3217", "E3221", "E3222", "E3223", "E3224", "E4014",
}

RULE_CODES = {
    "E3102": {"E3102"},
    "R-LIN-DROP": {"E3223", "E3224"},
    "R-LIN-WHERE": {"E3224"},
    "R-LIN-SELF": {"E3224"},
    "R-LIN-SEND": {"E3223", "E3224"},
    "R-LIN-COND": {"E3223"},
    "R-LIN-ASYNC": {"E3224"},
    "R18-UNIQUE": {"E3214"},
    "C-STORE": {"E3196", "E3201"},
    "C-HOLDER": {"E3199", "E3201", "E3207"},
    "C-WRITE": {"E3197", "E3199"},
    "C-ONECALL": {"E3201"},
    "C-EXIT": {"E3202"},
    "C-ISOREAD": {"E3198"},
    "C-SLOT": {"E3203"},
    "C-OUTER": {"E3204"},
    "C-RECOVER": {"E3200"},
    "C-LEND": {"E3198", "E3102"},
    "C-STATE": {"E3202"},
    "R4-ARG": {"E3207"},
    "R4-RESULT": {"E3209"},
    "R4-PICK": {"E3208"},
    "R4-BOTH": {"E3206"},
    "R3-SEND": {"E3217"},
    "R2-BLOCKWAIT": {"E3205"},
    "R13-NESTED": {"E3205", "E3221"},
    "R5-HOLDWAIT": {"E3221"},
    "R-HANDLE-CYCLE": {"E4014"},
    "R1-LET": {"E2013"},
    "R7-TIED": {"E3212"},
    "R-DEFER": {"E3222"},
    "R-DEFER-HOLD": {"E3222"},
}


def codes_of(rule):
    return RULE_CODES.get(rule, set())
