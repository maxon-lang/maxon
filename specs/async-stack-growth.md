---
feature: async-stack-growth
status: stable
keywords: [async, green-threads, morestack, stack-growth, relocation, recursion, concurrency]
category: concurrency
---

# Async stack growth — the relocating morestack (P1.5-B1a′)

## Documentation

A green thread starts on a tiny **2 KB** stack and grows it ON DEMAND: every function's prologue projects its
frame against a per-thread stack guard, and when the frame would overflow, it calls the runtime grower
`__gt_morestack`, which allocates a stack twice the size, COPIES the old stack onto it, and **walks the
saved-frame-pointer chain** fixing every interior pointer by the relocation offset — so the thread continues on
the larger stack with every live reference intact. Deep recursion inside an `async` body therefore just works:
the stack grows (and relocates) as many times as the depth needs.

```text
function deepRecurse(n int) returns int
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function main() returns ExitCode
	let p = async deepRecurse(200)   // ~200 frames — far past a 2 KB stack, so the runtime grows + relocates it
	let r = await p
	return r as ExitCode             // 200
end 'main'
```

Growth is transparent: a value live across the growth (a parameter, a partial sum) reads the same after
relocation as before, because the copy preserves the bytes and the chain walk fixes the pointers. Growth also
composes with the mid-body yield — a thread that `sleep`s, resumes, and THEN recurses deep grows on its resumed
stack — and with completion — a thread whose grown stack is released on completion leaves the next spawn a fresh
2 KB seed.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the one
statement of it.** `__gt_morestack` is hand-written x64 assembly and relocates a stack obtained from
`VirtualAlloc`, so these cases have no substrate to run on off x64-windows.

## Tests

<!-- test: async-stack-growth.deep-recursion -->
`async deepRecurse(200)` recurses ~200 frames deep — far past the 2 KB seed stack, forcing several
grow-and-relocate rounds — and the awaited sum is exact, proving the relocated stack carried every frame's
partial result correctly.
```maxon

function deepRecurse(n Integer) returns Integer
	Runtime.yield()
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function main() returns ExitCode
	let p = async deepRecurse(200)
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
200
```

<!-- test: async-stack-growth.multi-growth -->
A deeper recursion (~400 frames) forces MORE grow-and-relocate rounds and a longer saved-rbp chain to walk at
the deepest growth — the stress case for the chain walk across multiple relocations. The awaited sum (400) is
exact; `main` returns it less 250 to fit the exit code.
```maxon

function deepRecurse(n Integer) returns Integer
	Runtime.yield()
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function main() returns ExitCode
	let p = async deepRecurse(400)
	let r = await p
	return (r - 250) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
150
```

<!-- test: async-stack-growth.grow-across-yield -->
A thread `sleep`s (parks on the timer, context-switches back to its machine's scheduler context), RESUMES, and only THEN recurses
deep — so the growth happens on a stack the scheduler switched out and back in. The awaited sum is exact,
proving `gt.sp`/`gt.fp` and the saved-rbp chain are consistent across a yield followed by a relocation.
```maxon

function deepRecurse(n Integer) returns Integer
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function yieldThenRecurse() returns Integer
	sleep(1)
	return deepRecurse(200)
end 'yieldThenRecurse'

function main() returns ExitCode
	let p = async yieldThenRecurse()
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
200
```

<!-- test: async-stack-growth.grow-then-complete-then-respawn -->
A thread grows, completes (its grown stack is released), then a SECOND thread spawns on a fresh 2 KB seed and
grows in turn — proving free-on-complete leaves no corruption for the next spawn. Both awaited sums are exact.
```maxon

function deepRecurse(n Integer) returns Integer
	Runtime.yield()
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function main() returns ExitCode
	let p1 = async deepRecurse(60)
	let r1 = await p1
	let p2 = async deepRecurse(90)
	let r2 = await p2
	return (r1 + r2) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
150
```

<!-- test: async-stack-growth.a-frame-larger-than-two-doublings-grows-until-it-fits -->
**ONE FRAME CAN NEED MORE THAN ONE DOUBLING, AND THE GROWER KEEPS DOUBLING UNTIL IT FITS.** `wide` holds a
thousand call results live across one more call, so its frame is about 8 KB — four times the 2 KB seed. Go's
`newstack` doubles until the requested frame fits (`vendor/go/src/runtime/stack.go`); a grower that doubles
once and returns leaves the frame hanging below the new stack's base, and the first spill below it lands in
memory the thread does not own. The answer is a checksum over every value, so a spill written anywhere but
its slot changes it.
```maxon
typealias Integer = int(i64.min to i64.max)

function mix(seed Integer, k Integer) returns Integer
	return (seed * 31 + k) mod 1000003
end 'mix'

function touch(seed Integer) returns Integer
	return seed + 1
end 'touch'

function wide(seed Integer) returns Integer
	let v0 = mix(seed, k: 0)
	let v1 = mix(seed, k: 1)
	let v2 = mix(seed, k: 2)
	let v3 = mix(seed, k: 3)
	let v4 = mix(seed, k: 4)
	let v5 = mix(seed, k: 5)
	let v6 = mix(seed, k: 6)
	let v7 = mix(seed, k: 7)
	let v8 = mix(seed, k: 8)
	let v9 = mix(seed, k: 9)
	let v10 = mix(seed, k: 10)
	let v11 = mix(seed, k: 11)
	let v12 = mix(seed, k: 12)
	let v13 = mix(seed, k: 13)
	let v14 = mix(seed, k: 14)
	let v15 = mix(seed, k: 15)
	let v16 = mix(seed, k: 16)
	let v17 = mix(seed, k: 17)
	let v18 = mix(seed, k: 18)
	let v19 = mix(seed, k: 19)
	let v20 = mix(seed, k: 20)
	let v21 = mix(seed, k: 21)
	let v22 = mix(seed, k: 22)
	let v23 = mix(seed, k: 23)
	let v24 = mix(seed, k: 24)
	let v25 = mix(seed, k: 25)
	let v26 = mix(seed, k: 26)
	let v27 = mix(seed, k: 27)
	let v28 = mix(seed, k: 28)
	let v29 = mix(seed, k: 29)
	let v30 = mix(seed, k: 30)
	let v31 = mix(seed, k: 31)
	let v32 = mix(seed, k: 32)
	let v33 = mix(seed, k: 33)
	let v34 = mix(seed, k: 34)
	let v35 = mix(seed, k: 35)
	let v36 = mix(seed, k: 36)
	let v37 = mix(seed, k: 37)
	let v38 = mix(seed, k: 38)
	let v39 = mix(seed, k: 39)
	let v40 = mix(seed, k: 40)
	let v41 = mix(seed, k: 41)
	let v42 = mix(seed, k: 42)
	let v43 = mix(seed, k: 43)
	let v44 = mix(seed, k: 44)
	let v45 = mix(seed, k: 45)
	let v46 = mix(seed, k: 46)
	let v47 = mix(seed, k: 47)
	let v48 = mix(seed, k: 48)
	let v49 = mix(seed, k: 49)
	let v50 = mix(seed, k: 50)
	let v51 = mix(seed, k: 51)
	let v52 = mix(seed, k: 52)
	let v53 = mix(seed, k: 53)
	let v54 = mix(seed, k: 54)
	let v55 = mix(seed, k: 55)
	let v56 = mix(seed, k: 56)
	let v57 = mix(seed, k: 57)
	let v58 = mix(seed, k: 58)
	let v59 = mix(seed, k: 59)
	let v60 = mix(seed, k: 60)
	let v61 = mix(seed, k: 61)
	let v62 = mix(seed, k: 62)
	let v63 = mix(seed, k: 63)
	let v64 = mix(seed, k: 64)
	let v65 = mix(seed, k: 65)
	let v66 = mix(seed, k: 66)
	let v67 = mix(seed, k: 67)
	let v68 = mix(seed, k: 68)
	let v69 = mix(seed, k: 69)
	let v70 = mix(seed, k: 70)
	let v71 = mix(seed, k: 71)
	let v72 = mix(seed, k: 72)
	let v73 = mix(seed, k: 73)
	let v74 = mix(seed, k: 74)
	let v75 = mix(seed, k: 75)
	let v76 = mix(seed, k: 76)
	let v77 = mix(seed, k: 77)
	let v78 = mix(seed, k: 78)
	let v79 = mix(seed, k: 79)
	let v80 = mix(seed, k: 80)
	let v81 = mix(seed, k: 81)
	let v82 = mix(seed, k: 82)
	let v83 = mix(seed, k: 83)
	let v84 = mix(seed, k: 84)
	let v85 = mix(seed, k: 85)
	let v86 = mix(seed, k: 86)
	let v87 = mix(seed, k: 87)
	let v88 = mix(seed, k: 88)
	let v89 = mix(seed, k: 89)
	let v90 = mix(seed, k: 90)
	let v91 = mix(seed, k: 91)
	let v92 = mix(seed, k: 92)
	let v93 = mix(seed, k: 93)
	let v94 = mix(seed, k: 94)
	let v95 = mix(seed, k: 95)
	let v96 = mix(seed, k: 96)
	let v97 = mix(seed, k: 97)
	let v98 = mix(seed, k: 98)
	let v99 = mix(seed, k: 99)
	let v100 = mix(seed, k: 100)
	let v101 = mix(seed, k: 101)
	let v102 = mix(seed, k: 102)
	let v103 = mix(seed, k: 103)
	let v104 = mix(seed, k: 104)
	let v105 = mix(seed, k: 105)
	let v106 = mix(seed, k: 106)
	let v107 = mix(seed, k: 107)
	let v108 = mix(seed, k: 108)
	let v109 = mix(seed, k: 109)
	let v110 = mix(seed, k: 110)
	let v111 = mix(seed, k: 111)
	let v112 = mix(seed, k: 112)
	let v113 = mix(seed, k: 113)
	let v114 = mix(seed, k: 114)
	let v115 = mix(seed, k: 115)
	let v116 = mix(seed, k: 116)
	let v117 = mix(seed, k: 117)
	let v118 = mix(seed, k: 118)
	let v119 = mix(seed, k: 119)
	let v120 = mix(seed, k: 120)
	let v121 = mix(seed, k: 121)
	let v122 = mix(seed, k: 122)
	let v123 = mix(seed, k: 123)
	let v124 = mix(seed, k: 124)
	let v125 = mix(seed, k: 125)
	let v126 = mix(seed, k: 126)
	let v127 = mix(seed, k: 127)
	let v128 = mix(seed, k: 128)
	let v129 = mix(seed, k: 129)
	let v130 = mix(seed, k: 130)
	let v131 = mix(seed, k: 131)
	let v132 = mix(seed, k: 132)
	let v133 = mix(seed, k: 133)
	let v134 = mix(seed, k: 134)
	let v135 = mix(seed, k: 135)
	let v136 = mix(seed, k: 136)
	let v137 = mix(seed, k: 137)
	let v138 = mix(seed, k: 138)
	let v139 = mix(seed, k: 139)
	let v140 = mix(seed, k: 140)
	let v141 = mix(seed, k: 141)
	let v142 = mix(seed, k: 142)
	let v143 = mix(seed, k: 143)
	let v144 = mix(seed, k: 144)
	let v145 = mix(seed, k: 145)
	let v146 = mix(seed, k: 146)
	let v147 = mix(seed, k: 147)
	let v148 = mix(seed, k: 148)
	let v149 = mix(seed, k: 149)
	let v150 = mix(seed, k: 150)
	let v151 = mix(seed, k: 151)
	let v152 = mix(seed, k: 152)
	let v153 = mix(seed, k: 153)
	let v154 = mix(seed, k: 154)
	let v155 = mix(seed, k: 155)
	let v156 = mix(seed, k: 156)
	let v157 = mix(seed, k: 157)
	let v158 = mix(seed, k: 158)
	let v159 = mix(seed, k: 159)
	let v160 = mix(seed, k: 160)
	let v161 = mix(seed, k: 161)
	let v162 = mix(seed, k: 162)
	let v163 = mix(seed, k: 163)
	let v164 = mix(seed, k: 164)
	let v165 = mix(seed, k: 165)
	let v166 = mix(seed, k: 166)
	let v167 = mix(seed, k: 167)
	let v168 = mix(seed, k: 168)
	let v169 = mix(seed, k: 169)
	let v170 = mix(seed, k: 170)
	let v171 = mix(seed, k: 171)
	let v172 = mix(seed, k: 172)
	let v173 = mix(seed, k: 173)
	let v174 = mix(seed, k: 174)
	let v175 = mix(seed, k: 175)
	let v176 = mix(seed, k: 176)
	let v177 = mix(seed, k: 177)
	let v178 = mix(seed, k: 178)
	let v179 = mix(seed, k: 179)
	let v180 = mix(seed, k: 180)
	let v181 = mix(seed, k: 181)
	let v182 = mix(seed, k: 182)
	let v183 = mix(seed, k: 183)
	let v184 = mix(seed, k: 184)
	let v185 = mix(seed, k: 185)
	let v186 = mix(seed, k: 186)
	let v187 = mix(seed, k: 187)
	let v188 = mix(seed, k: 188)
	let v189 = mix(seed, k: 189)
	let v190 = mix(seed, k: 190)
	let v191 = mix(seed, k: 191)
	let v192 = mix(seed, k: 192)
	let v193 = mix(seed, k: 193)
	let v194 = mix(seed, k: 194)
	let v195 = mix(seed, k: 195)
	let v196 = mix(seed, k: 196)
	let v197 = mix(seed, k: 197)
	let v198 = mix(seed, k: 198)
	let v199 = mix(seed, k: 199)
	let v200 = mix(seed, k: 200)
	let v201 = mix(seed, k: 201)
	let v202 = mix(seed, k: 202)
	let v203 = mix(seed, k: 203)
	let v204 = mix(seed, k: 204)
	let v205 = mix(seed, k: 205)
	let v206 = mix(seed, k: 206)
	let v207 = mix(seed, k: 207)
	let v208 = mix(seed, k: 208)
	let v209 = mix(seed, k: 209)
	let v210 = mix(seed, k: 210)
	let v211 = mix(seed, k: 211)
	let v212 = mix(seed, k: 212)
	let v213 = mix(seed, k: 213)
	let v214 = mix(seed, k: 214)
	let v215 = mix(seed, k: 215)
	let v216 = mix(seed, k: 216)
	let v217 = mix(seed, k: 217)
	let v218 = mix(seed, k: 218)
	let v219 = mix(seed, k: 219)
	let v220 = mix(seed, k: 220)
	let v221 = mix(seed, k: 221)
	let v222 = mix(seed, k: 222)
	let v223 = mix(seed, k: 223)
	let v224 = mix(seed, k: 224)
	let v225 = mix(seed, k: 225)
	let v226 = mix(seed, k: 226)
	let v227 = mix(seed, k: 227)
	let v228 = mix(seed, k: 228)
	let v229 = mix(seed, k: 229)
	let v230 = mix(seed, k: 230)
	let v231 = mix(seed, k: 231)
	let v232 = mix(seed, k: 232)
	let v233 = mix(seed, k: 233)
	let v234 = mix(seed, k: 234)
	let v235 = mix(seed, k: 235)
	let v236 = mix(seed, k: 236)
	let v237 = mix(seed, k: 237)
	let v238 = mix(seed, k: 238)
	let v239 = mix(seed, k: 239)
	let v240 = mix(seed, k: 240)
	let v241 = mix(seed, k: 241)
	let v242 = mix(seed, k: 242)
	let v243 = mix(seed, k: 243)
	let v244 = mix(seed, k: 244)
	let v245 = mix(seed, k: 245)
	let v246 = mix(seed, k: 246)
	let v247 = mix(seed, k: 247)
	let v248 = mix(seed, k: 248)
	let v249 = mix(seed, k: 249)
	let v250 = mix(seed, k: 250)
	let v251 = mix(seed, k: 251)
	let v252 = mix(seed, k: 252)
	let v253 = mix(seed, k: 253)
	let v254 = mix(seed, k: 254)
	let v255 = mix(seed, k: 255)
	let v256 = mix(seed, k: 256)
	let v257 = mix(seed, k: 257)
	let v258 = mix(seed, k: 258)
	let v259 = mix(seed, k: 259)
	let v260 = mix(seed, k: 260)
	let v261 = mix(seed, k: 261)
	let v262 = mix(seed, k: 262)
	let v263 = mix(seed, k: 263)
	let v264 = mix(seed, k: 264)
	let v265 = mix(seed, k: 265)
	let v266 = mix(seed, k: 266)
	let v267 = mix(seed, k: 267)
	let v268 = mix(seed, k: 268)
	let v269 = mix(seed, k: 269)
	let v270 = mix(seed, k: 270)
	let v271 = mix(seed, k: 271)
	let v272 = mix(seed, k: 272)
	let v273 = mix(seed, k: 273)
	let v274 = mix(seed, k: 274)
	let v275 = mix(seed, k: 275)
	let v276 = mix(seed, k: 276)
	let v277 = mix(seed, k: 277)
	let v278 = mix(seed, k: 278)
	let v279 = mix(seed, k: 279)
	let v280 = mix(seed, k: 280)
	let v281 = mix(seed, k: 281)
	let v282 = mix(seed, k: 282)
	let v283 = mix(seed, k: 283)
	let v284 = mix(seed, k: 284)
	let v285 = mix(seed, k: 285)
	let v286 = mix(seed, k: 286)
	let v287 = mix(seed, k: 287)
	let v288 = mix(seed, k: 288)
	let v289 = mix(seed, k: 289)
	let v290 = mix(seed, k: 290)
	let v291 = mix(seed, k: 291)
	let v292 = mix(seed, k: 292)
	let v293 = mix(seed, k: 293)
	let v294 = mix(seed, k: 294)
	let v295 = mix(seed, k: 295)
	let v296 = mix(seed, k: 296)
	let v297 = mix(seed, k: 297)
	let v298 = mix(seed, k: 298)
	let v299 = mix(seed, k: 299)
	let v300 = mix(seed, k: 300)
	let v301 = mix(seed, k: 301)
	let v302 = mix(seed, k: 302)
	let v303 = mix(seed, k: 303)
	let v304 = mix(seed, k: 304)
	let v305 = mix(seed, k: 305)
	let v306 = mix(seed, k: 306)
	let v307 = mix(seed, k: 307)
	let v308 = mix(seed, k: 308)
	let v309 = mix(seed, k: 309)
	let v310 = mix(seed, k: 310)
	let v311 = mix(seed, k: 311)
	let v312 = mix(seed, k: 312)
	let v313 = mix(seed, k: 313)
	let v314 = mix(seed, k: 314)
	let v315 = mix(seed, k: 315)
	let v316 = mix(seed, k: 316)
	let v317 = mix(seed, k: 317)
	let v318 = mix(seed, k: 318)
	let v319 = mix(seed, k: 319)
	let v320 = mix(seed, k: 320)
	let v321 = mix(seed, k: 321)
	let v322 = mix(seed, k: 322)
	let v323 = mix(seed, k: 323)
	let v324 = mix(seed, k: 324)
	let v325 = mix(seed, k: 325)
	let v326 = mix(seed, k: 326)
	let v327 = mix(seed, k: 327)
	let v328 = mix(seed, k: 328)
	let v329 = mix(seed, k: 329)
	let v330 = mix(seed, k: 330)
	let v331 = mix(seed, k: 331)
	let v332 = mix(seed, k: 332)
	let v333 = mix(seed, k: 333)
	let v334 = mix(seed, k: 334)
	let v335 = mix(seed, k: 335)
	let v336 = mix(seed, k: 336)
	let v337 = mix(seed, k: 337)
	let v338 = mix(seed, k: 338)
	let v339 = mix(seed, k: 339)
	let v340 = mix(seed, k: 340)
	let v341 = mix(seed, k: 341)
	let v342 = mix(seed, k: 342)
	let v343 = mix(seed, k: 343)
	let v344 = mix(seed, k: 344)
	let v345 = mix(seed, k: 345)
	let v346 = mix(seed, k: 346)
	let v347 = mix(seed, k: 347)
	let v348 = mix(seed, k: 348)
	let v349 = mix(seed, k: 349)
	let v350 = mix(seed, k: 350)
	let v351 = mix(seed, k: 351)
	let v352 = mix(seed, k: 352)
	let v353 = mix(seed, k: 353)
	let v354 = mix(seed, k: 354)
	let v355 = mix(seed, k: 355)
	let v356 = mix(seed, k: 356)
	let v357 = mix(seed, k: 357)
	let v358 = mix(seed, k: 358)
	let v359 = mix(seed, k: 359)
	let v360 = mix(seed, k: 360)
	let v361 = mix(seed, k: 361)
	let v362 = mix(seed, k: 362)
	let v363 = mix(seed, k: 363)
	let v364 = mix(seed, k: 364)
	let v365 = mix(seed, k: 365)
	let v366 = mix(seed, k: 366)
	let v367 = mix(seed, k: 367)
	let v368 = mix(seed, k: 368)
	let v369 = mix(seed, k: 369)
	let v370 = mix(seed, k: 370)
	let v371 = mix(seed, k: 371)
	let v372 = mix(seed, k: 372)
	let v373 = mix(seed, k: 373)
	let v374 = mix(seed, k: 374)
	let v375 = mix(seed, k: 375)
	let v376 = mix(seed, k: 376)
	let v377 = mix(seed, k: 377)
	let v378 = mix(seed, k: 378)
	let v379 = mix(seed, k: 379)
	let v380 = mix(seed, k: 380)
	let v381 = mix(seed, k: 381)
	let v382 = mix(seed, k: 382)
	let v383 = mix(seed, k: 383)
	let v384 = mix(seed, k: 384)
	let v385 = mix(seed, k: 385)
	let v386 = mix(seed, k: 386)
	let v387 = mix(seed, k: 387)
	let v388 = mix(seed, k: 388)
	let v389 = mix(seed, k: 389)
	let v390 = mix(seed, k: 390)
	let v391 = mix(seed, k: 391)
	let v392 = mix(seed, k: 392)
	let v393 = mix(seed, k: 393)
	let v394 = mix(seed, k: 394)
	let v395 = mix(seed, k: 395)
	let v396 = mix(seed, k: 396)
	let v397 = mix(seed, k: 397)
	let v398 = mix(seed, k: 398)
	let v399 = mix(seed, k: 399)
	let v400 = mix(seed, k: 400)
	let v401 = mix(seed, k: 401)
	let v402 = mix(seed, k: 402)
	let v403 = mix(seed, k: 403)
	let v404 = mix(seed, k: 404)
	let v405 = mix(seed, k: 405)
	let v406 = mix(seed, k: 406)
	let v407 = mix(seed, k: 407)
	let v408 = mix(seed, k: 408)
	let v409 = mix(seed, k: 409)
	let v410 = mix(seed, k: 410)
	let v411 = mix(seed, k: 411)
	let v412 = mix(seed, k: 412)
	let v413 = mix(seed, k: 413)
	let v414 = mix(seed, k: 414)
	let v415 = mix(seed, k: 415)
	let v416 = mix(seed, k: 416)
	let v417 = mix(seed, k: 417)
	let v418 = mix(seed, k: 418)
	let v419 = mix(seed, k: 419)
	let v420 = mix(seed, k: 420)
	let v421 = mix(seed, k: 421)
	let v422 = mix(seed, k: 422)
	let v423 = mix(seed, k: 423)
	let v424 = mix(seed, k: 424)
	let v425 = mix(seed, k: 425)
	let v426 = mix(seed, k: 426)
	let v427 = mix(seed, k: 427)
	let v428 = mix(seed, k: 428)
	let v429 = mix(seed, k: 429)
	let v430 = mix(seed, k: 430)
	let v431 = mix(seed, k: 431)
	let v432 = mix(seed, k: 432)
	let v433 = mix(seed, k: 433)
	let v434 = mix(seed, k: 434)
	let v435 = mix(seed, k: 435)
	let v436 = mix(seed, k: 436)
	let v437 = mix(seed, k: 437)
	let v438 = mix(seed, k: 438)
	let v439 = mix(seed, k: 439)
	let v440 = mix(seed, k: 440)
	let v441 = mix(seed, k: 441)
	let v442 = mix(seed, k: 442)
	let v443 = mix(seed, k: 443)
	let v444 = mix(seed, k: 444)
	let v445 = mix(seed, k: 445)
	let v446 = mix(seed, k: 446)
	let v447 = mix(seed, k: 447)
	let v448 = mix(seed, k: 448)
	let v449 = mix(seed, k: 449)
	let v450 = mix(seed, k: 450)
	let v451 = mix(seed, k: 451)
	let v452 = mix(seed, k: 452)
	let v453 = mix(seed, k: 453)
	let v454 = mix(seed, k: 454)
	let v455 = mix(seed, k: 455)
	let v456 = mix(seed, k: 456)
	let v457 = mix(seed, k: 457)
	let v458 = mix(seed, k: 458)
	let v459 = mix(seed, k: 459)
	let v460 = mix(seed, k: 460)
	let v461 = mix(seed, k: 461)
	let v462 = mix(seed, k: 462)
	let v463 = mix(seed, k: 463)
	let v464 = mix(seed, k: 464)
	let v465 = mix(seed, k: 465)
	let v466 = mix(seed, k: 466)
	let v467 = mix(seed, k: 467)
	let v468 = mix(seed, k: 468)
	let v469 = mix(seed, k: 469)
	let v470 = mix(seed, k: 470)
	let v471 = mix(seed, k: 471)
	let v472 = mix(seed, k: 472)
	let v473 = mix(seed, k: 473)
	let v474 = mix(seed, k: 474)
	let v475 = mix(seed, k: 475)
	let v476 = mix(seed, k: 476)
	let v477 = mix(seed, k: 477)
	let v478 = mix(seed, k: 478)
	let v479 = mix(seed, k: 479)
	let v480 = mix(seed, k: 480)
	let v481 = mix(seed, k: 481)
	let v482 = mix(seed, k: 482)
	let v483 = mix(seed, k: 483)
	let v484 = mix(seed, k: 484)
	let v485 = mix(seed, k: 485)
	let v486 = mix(seed, k: 486)
	let v487 = mix(seed, k: 487)
	let v488 = mix(seed, k: 488)
	let v489 = mix(seed, k: 489)
	let v490 = mix(seed, k: 490)
	let v491 = mix(seed, k: 491)
	let v492 = mix(seed, k: 492)
	let v493 = mix(seed, k: 493)
	let v494 = mix(seed, k: 494)
	let v495 = mix(seed, k: 495)
	let v496 = mix(seed, k: 496)
	let v497 = mix(seed, k: 497)
	let v498 = mix(seed, k: 498)
	let v499 = mix(seed, k: 499)
	let v500 = mix(seed, k: 500)
	let v501 = mix(seed, k: 501)
	let v502 = mix(seed, k: 502)
	let v503 = mix(seed, k: 503)
	let v504 = mix(seed, k: 504)
	let v505 = mix(seed, k: 505)
	let v506 = mix(seed, k: 506)
	let v507 = mix(seed, k: 507)
	let v508 = mix(seed, k: 508)
	let v509 = mix(seed, k: 509)
	let v510 = mix(seed, k: 510)
	let v511 = mix(seed, k: 511)
	let v512 = mix(seed, k: 512)
	let v513 = mix(seed, k: 513)
	let v514 = mix(seed, k: 514)
	let v515 = mix(seed, k: 515)
	let v516 = mix(seed, k: 516)
	let v517 = mix(seed, k: 517)
	let v518 = mix(seed, k: 518)
	let v519 = mix(seed, k: 519)
	let v520 = mix(seed, k: 520)
	let v521 = mix(seed, k: 521)
	let v522 = mix(seed, k: 522)
	let v523 = mix(seed, k: 523)
	let v524 = mix(seed, k: 524)
	let v525 = mix(seed, k: 525)
	let v526 = mix(seed, k: 526)
	let v527 = mix(seed, k: 527)
	let v528 = mix(seed, k: 528)
	let v529 = mix(seed, k: 529)
	let v530 = mix(seed, k: 530)
	let v531 = mix(seed, k: 531)
	let v532 = mix(seed, k: 532)
	let v533 = mix(seed, k: 533)
	let v534 = mix(seed, k: 534)
	let v535 = mix(seed, k: 535)
	let v536 = mix(seed, k: 536)
	let v537 = mix(seed, k: 537)
	let v538 = mix(seed, k: 538)
	let v539 = mix(seed, k: 539)
	let v540 = mix(seed, k: 540)
	let v541 = mix(seed, k: 541)
	let v542 = mix(seed, k: 542)
	let v543 = mix(seed, k: 543)
	let v544 = mix(seed, k: 544)
	let v545 = mix(seed, k: 545)
	let v546 = mix(seed, k: 546)
	let v547 = mix(seed, k: 547)
	let v548 = mix(seed, k: 548)
	let v549 = mix(seed, k: 549)
	let v550 = mix(seed, k: 550)
	let v551 = mix(seed, k: 551)
	let v552 = mix(seed, k: 552)
	let v553 = mix(seed, k: 553)
	let v554 = mix(seed, k: 554)
	let v555 = mix(seed, k: 555)
	let v556 = mix(seed, k: 556)
	let v557 = mix(seed, k: 557)
	let v558 = mix(seed, k: 558)
	let v559 = mix(seed, k: 559)
	let v560 = mix(seed, k: 560)
	let v561 = mix(seed, k: 561)
	let v562 = mix(seed, k: 562)
	let v563 = mix(seed, k: 563)
	let v564 = mix(seed, k: 564)
	let v565 = mix(seed, k: 565)
	let v566 = mix(seed, k: 566)
	let v567 = mix(seed, k: 567)
	let v568 = mix(seed, k: 568)
	let v569 = mix(seed, k: 569)
	let v570 = mix(seed, k: 570)
	let v571 = mix(seed, k: 571)
	let v572 = mix(seed, k: 572)
	let v573 = mix(seed, k: 573)
	let v574 = mix(seed, k: 574)
	let v575 = mix(seed, k: 575)
	let v576 = mix(seed, k: 576)
	let v577 = mix(seed, k: 577)
	let v578 = mix(seed, k: 578)
	let v579 = mix(seed, k: 579)
	let v580 = mix(seed, k: 580)
	let v581 = mix(seed, k: 581)
	let v582 = mix(seed, k: 582)
	let v583 = mix(seed, k: 583)
	let v584 = mix(seed, k: 584)
	let v585 = mix(seed, k: 585)
	let v586 = mix(seed, k: 586)
	let v587 = mix(seed, k: 587)
	let v588 = mix(seed, k: 588)
	let v589 = mix(seed, k: 589)
	let v590 = mix(seed, k: 590)
	let v591 = mix(seed, k: 591)
	let v592 = mix(seed, k: 592)
	let v593 = mix(seed, k: 593)
	let v594 = mix(seed, k: 594)
	let v595 = mix(seed, k: 595)
	let v596 = mix(seed, k: 596)
	let v597 = mix(seed, k: 597)
	let v598 = mix(seed, k: 598)
	let v599 = mix(seed, k: 599)
	let v600 = mix(seed, k: 600)
	let v601 = mix(seed, k: 601)
	let v602 = mix(seed, k: 602)
	let v603 = mix(seed, k: 603)
	let v604 = mix(seed, k: 604)
	let v605 = mix(seed, k: 605)
	let v606 = mix(seed, k: 606)
	let v607 = mix(seed, k: 607)
	let v608 = mix(seed, k: 608)
	let v609 = mix(seed, k: 609)
	let v610 = mix(seed, k: 610)
	let v611 = mix(seed, k: 611)
	let v612 = mix(seed, k: 612)
	let v613 = mix(seed, k: 613)
	let v614 = mix(seed, k: 614)
	let v615 = mix(seed, k: 615)
	let v616 = mix(seed, k: 616)
	let v617 = mix(seed, k: 617)
	let v618 = mix(seed, k: 618)
	let v619 = mix(seed, k: 619)
	let v620 = mix(seed, k: 620)
	let v621 = mix(seed, k: 621)
	let v622 = mix(seed, k: 622)
	let v623 = mix(seed, k: 623)
	let v624 = mix(seed, k: 624)
	let v625 = mix(seed, k: 625)
	let v626 = mix(seed, k: 626)
	let v627 = mix(seed, k: 627)
	let v628 = mix(seed, k: 628)
	let v629 = mix(seed, k: 629)
	let v630 = mix(seed, k: 630)
	let v631 = mix(seed, k: 631)
	let v632 = mix(seed, k: 632)
	let v633 = mix(seed, k: 633)
	let v634 = mix(seed, k: 634)
	let v635 = mix(seed, k: 635)
	let v636 = mix(seed, k: 636)
	let v637 = mix(seed, k: 637)
	let v638 = mix(seed, k: 638)
	let v639 = mix(seed, k: 639)
	let v640 = mix(seed, k: 640)
	let v641 = mix(seed, k: 641)
	let v642 = mix(seed, k: 642)
	let v643 = mix(seed, k: 643)
	let v644 = mix(seed, k: 644)
	let v645 = mix(seed, k: 645)
	let v646 = mix(seed, k: 646)
	let v647 = mix(seed, k: 647)
	let v648 = mix(seed, k: 648)
	let v649 = mix(seed, k: 649)
	let v650 = mix(seed, k: 650)
	let v651 = mix(seed, k: 651)
	let v652 = mix(seed, k: 652)
	let v653 = mix(seed, k: 653)
	let v654 = mix(seed, k: 654)
	let v655 = mix(seed, k: 655)
	let v656 = mix(seed, k: 656)
	let v657 = mix(seed, k: 657)
	let v658 = mix(seed, k: 658)
	let v659 = mix(seed, k: 659)
	let v660 = mix(seed, k: 660)
	let v661 = mix(seed, k: 661)
	let v662 = mix(seed, k: 662)
	let v663 = mix(seed, k: 663)
	let v664 = mix(seed, k: 664)
	let v665 = mix(seed, k: 665)
	let v666 = mix(seed, k: 666)
	let v667 = mix(seed, k: 667)
	let v668 = mix(seed, k: 668)
	let v669 = mix(seed, k: 669)
	let v670 = mix(seed, k: 670)
	let v671 = mix(seed, k: 671)
	let v672 = mix(seed, k: 672)
	let v673 = mix(seed, k: 673)
	let v674 = mix(seed, k: 674)
	let v675 = mix(seed, k: 675)
	let v676 = mix(seed, k: 676)
	let v677 = mix(seed, k: 677)
	let v678 = mix(seed, k: 678)
	let v679 = mix(seed, k: 679)
	let v680 = mix(seed, k: 680)
	let v681 = mix(seed, k: 681)
	let v682 = mix(seed, k: 682)
	let v683 = mix(seed, k: 683)
	let v684 = mix(seed, k: 684)
	let v685 = mix(seed, k: 685)
	let v686 = mix(seed, k: 686)
	let v687 = mix(seed, k: 687)
	let v688 = mix(seed, k: 688)
	let v689 = mix(seed, k: 689)
	let v690 = mix(seed, k: 690)
	let v691 = mix(seed, k: 691)
	let v692 = mix(seed, k: 692)
	let v693 = mix(seed, k: 693)
	let v694 = mix(seed, k: 694)
	let v695 = mix(seed, k: 695)
	let v696 = mix(seed, k: 696)
	let v697 = mix(seed, k: 697)
	let v698 = mix(seed, k: 698)
	let v699 = mix(seed, k: 699)
	let v700 = mix(seed, k: 700)
	let v701 = mix(seed, k: 701)
	let v702 = mix(seed, k: 702)
	let v703 = mix(seed, k: 703)
	let v704 = mix(seed, k: 704)
	let v705 = mix(seed, k: 705)
	let v706 = mix(seed, k: 706)
	let v707 = mix(seed, k: 707)
	let v708 = mix(seed, k: 708)
	let v709 = mix(seed, k: 709)
	let v710 = mix(seed, k: 710)
	let v711 = mix(seed, k: 711)
	let v712 = mix(seed, k: 712)
	let v713 = mix(seed, k: 713)
	let v714 = mix(seed, k: 714)
	let v715 = mix(seed, k: 715)
	let v716 = mix(seed, k: 716)
	let v717 = mix(seed, k: 717)
	let v718 = mix(seed, k: 718)
	let v719 = mix(seed, k: 719)
	let v720 = mix(seed, k: 720)
	let v721 = mix(seed, k: 721)
	let v722 = mix(seed, k: 722)
	let v723 = mix(seed, k: 723)
	let v724 = mix(seed, k: 724)
	let v725 = mix(seed, k: 725)
	let v726 = mix(seed, k: 726)
	let v727 = mix(seed, k: 727)
	let v728 = mix(seed, k: 728)
	let v729 = mix(seed, k: 729)
	let v730 = mix(seed, k: 730)
	let v731 = mix(seed, k: 731)
	let v732 = mix(seed, k: 732)
	let v733 = mix(seed, k: 733)
	let v734 = mix(seed, k: 734)
	let v735 = mix(seed, k: 735)
	let v736 = mix(seed, k: 736)
	let v737 = mix(seed, k: 737)
	let v738 = mix(seed, k: 738)
	let v739 = mix(seed, k: 739)
	let v740 = mix(seed, k: 740)
	let v741 = mix(seed, k: 741)
	let v742 = mix(seed, k: 742)
	let v743 = mix(seed, k: 743)
	let v744 = mix(seed, k: 744)
	let v745 = mix(seed, k: 745)
	let v746 = mix(seed, k: 746)
	let v747 = mix(seed, k: 747)
	let v748 = mix(seed, k: 748)
	let v749 = mix(seed, k: 749)
	let v750 = mix(seed, k: 750)
	let v751 = mix(seed, k: 751)
	let v752 = mix(seed, k: 752)
	let v753 = mix(seed, k: 753)
	let v754 = mix(seed, k: 754)
	let v755 = mix(seed, k: 755)
	let v756 = mix(seed, k: 756)
	let v757 = mix(seed, k: 757)
	let v758 = mix(seed, k: 758)
	let v759 = mix(seed, k: 759)
	let v760 = mix(seed, k: 760)
	let v761 = mix(seed, k: 761)
	let v762 = mix(seed, k: 762)
	let v763 = mix(seed, k: 763)
	let v764 = mix(seed, k: 764)
	let v765 = mix(seed, k: 765)
	let v766 = mix(seed, k: 766)
	let v767 = mix(seed, k: 767)
	let v768 = mix(seed, k: 768)
	let v769 = mix(seed, k: 769)
	let v770 = mix(seed, k: 770)
	let v771 = mix(seed, k: 771)
	let v772 = mix(seed, k: 772)
	let v773 = mix(seed, k: 773)
	let v774 = mix(seed, k: 774)
	let v775 = mix(seed, k: 775)
	let v776 = mix(seed, k: 776)
	let v777 = mix(seed, k: 777)
	let v778 = mix(seed, k: 778)
	let v779 = mix(seed, k: 779)
	let v780 = mix(seed, k: 780)
	let v781 = mix(seed, k: 781)
	let v782 = mix(seed, k: 782)
	let v783 = mix(seed, k: 783)
	let v784 = mix(seed, k: 784)
	let v785 = mix(seed, k: 785)
	let v786 = mix(seed, k: 786)
	let v787 = mix(seed, k: 787)
	let v788 = mix(seed, k: 788)
	let v789 = mix(seed, k: 789)
	let v790 = mix(seed, k: 790)
	let v791 = mix(seed, k: 791)
	let v792 = mix(seed, k: 792)
	let v793 = mix(seed, k: 793)
	let v794 = mix(seed, k: 794)
	let v795 = mix(seed, k: 795)
	let v796 = mix(seed, k: 796)
	let v797 = mix(seed, k: 797)
	let v798 = mix(seed, k: 798)
	let v799 = mix(seed, k: 799)
	let v800 = mix(seed, k: 800)
	let v801 = mix(seed, k: 801)
	let v802 = mix(seed, k: 802)
	let v803 = mix(seed, k: 803)
	let v804 = mix(seed, k: 804)
	let v805 = mix(seed, k: 805)
	let v806 = mix(seed, k: 806)
	let v807 = mix(seed, k: 807)
	let v808 = mix(seed, k: 808)
	let v809 = mix(seed, k: 809)
	let v810 = mix(seed, k: 810)
	let v811 = mix(seed, k: 811)
	let v812 = mix(seed, k: 812)
	let v813 = mix(seed, k: 813)
	let v814 = mix(seed, k: 814)
	let v815 = mix(seed, k: 815)
	let v816 = mix(seed, k: 816)
	let v817 = mix(seed, k: 817)
	let v818 = mix(seed, k: 818)
	let v819 = mix(seed, k: 819)
	let v820 = mix(seed, k: 820)
	let v821 = mix(seed, k: 821)
	let v822 = mix(seed, k: 822)
	let v823 = mix(seed, k: 823)
	let v824 = mix(seed, k: 824)
	let v825 = mix(seed, k: 825)
	let v826 = mix(seed, k: 826)
	let v827 = mix(seed, k: 827)
	let v828 = mix(seed, k: 828)
	let v829 = mix(seed, k: 829)
	let v830 = mix(seed, k: 830)
	let v831 = mix(seed, k: 831)
	let v832 = mix(seed, k: 832)
	let v833 = mix(seed, k: 833)
	let v834 = mix(seed, k: 834)
	let v835 = mix(seed, k: 835)
	let v836 = mix(seed, k: 836)
	let v837 = mix(seed, k: 837)
	let v838 = mix(seed, k: 838)
	let v839 = mix(seed, k: 839)
	let v840 = mix(seed, k: 840)
	let v841 = mix(seed, k: 841)
	let v842 = mix(seed, k: 842)
	let v843 = mix(seed, k: 843)
	let v844 = mix(seed, k: 844)
	let v845 = mix(seed, k: 845)
	let v846 = mix(seed, k: 846)
	let v847 = mix(seed, k: 847)
	let v848 = mix(seed, k: 848)
	let v849 = mix(seed, k: 849)
	let v850 = mix(seed, k: 850)
	let v851 = mix(seed, k: 851)
	let v852 = mix(seed, k: 852)
	let v853 = mix(seed, k: 853)
	let v854 = mix(seed, k: 854)
	let v855 = mix(seed, k: 855)
	let v856 = mix(seed, k: 856)
	let v857 = mix(seed, k: 857)
	let v858 = mix(seed, k: 858)
	let v859 = mix(seed, k: 859)
	let v860 = mix(seed, k: 860)
	let v861 = mix(seed, k: 861)
	let v862 = mix(seed, k: 862)
	let v863 = mix(seed, k: 863)
	let v864 = mix(seed, k: 864)
	let v865 = mix(seed, k: 865)
	let v866 = mix(seed, k: 866)
	let v867 = mix(seed, k: 867)
	let v868 = mix(seed, k: 868)
	let v869 = mix(seed, k: 869)
	let v870 = mix(seed, k: 870)
	let v871 = mix(seed, k: 871)
	let v872 = mix(seed, k: 872)
	let v873 = mix(seed, k: 873)
	let v874 = mix(seed, k: 874)
	let v875 = mix(seed, k: 875)
	let v876 = mix(seed, k: 876)
	let v877 = mix(seed, k: 877)
	let v878 = mix(seed, k: 878)
	let v879 = mix(seed, k: 879)
	let v880 = mix(seed, k: 880)
	let v881 = mix(seed, k: 881)
	let v882 = mix(seed, k: 882)
	let v883 = mix(seed, k: 883)
	let v884 = mix(seed, k: 884)
	let v885 = mix(seed, k: 885)
	let v886 = mix(seed, k: 886)
	let v887 = mix(seed, k: 887)
	let v888 = mix(seed, k: 888)
	let v889 = mix(seed, k: 889)
	let v890 = mix(seed, k: 890)
	let v891 = mix(seed, k: 891)
	let v892 = mix(seed, k: 892)
	let v893 = mix(seed, k: 893)
	let v894 = mix(seed, k: 894)
	let v895 = mix(seed, k: 895)
	let v896 = mix(seed, k: 896)
	let v897 = mix(seed, k: 897)
	let v898 = mix(seed, k: 898)
	let v899 = mix(seed, k: 899)
	let v900 = mix(seed, k: 900)
	let v901 = mix(seed, k: 901)
	let v902 = mix(seed, k: 902)
	let v903 = mix(seed, k: 903)
	let v904 = mix(seed, k: 904)
	let v905 = mix(seed, k: 905)
	let v906 = mix(seed, k: 906)
	let v907 = mix(seed, k: 907)
	let v908 = mix(seed, k: 908)
	let v909 = mix(seed, k: 909)
	let v910 = mix(seed, k: 910)
	let v911 = mix(seed, k: 911)
	let v912 = mix(seed, k: 912)
	let v913 = mix(seed, k: 913)
	let v914 = mix(seed, k: 914)
	let v915 = mix(seed, k: 915)
	let v916 = mix(seed, k: 916)
	let v917 = mix(seed, k: 917)
	let v918 = mix(seed, k: 918)
	let v919 = mix(seed, k: 919)
	let v920 = mix(seed, k: 920)
	let v921 = mix(seed, k: 921)
	let v922 = mix(seed, k: 922)
	let v923 = mix(seed, k: 923)
	let v924 = mix(seed, k: 924)
	let v925 = mix(seed, k: 925)
	let v926 = mix(seed, k: 926)
	let v927 = mix(seed, k: 927)
	let v928 = mix(seed, k: 928)
	let v929 = mix(seed, k: 929)
	let v930 = mix(seed, k: 930)
	let v931 = mix(seed, k: 931)
	let v932 = mix(seed, k: 932)
	let v933 = mix(seed, k: 933)
	let v934 = mix(seed, k: 934)
	let v935 = mix(seed, k: 935)
	let v936 = mix(seed, k: 936)
	let v937 = mix(seed, k: 937)
	let v938 = mix(seed, k: 938)
	let v939 = mix(seed, k: 939)
	let v940 = mix(seed, k: 940)
	let v941 = mix(seed, k: 941)
	let v942 = mix(seed, k: 942)
	let v943 = mix(seed, k: 943)
	let v944 = mix(seed, k: 944)
	let v945 = mix(seed, k: 945)
	let v946 = mix(seed, k: 946)
	let v947 = mix(seed, k: 947)
	let v948 = mix(seed, k: 948)
	let v949 = mix(seed, k: 949)
	let v950 = mix(seed, k: 950)
	let v951 = mix(seed, k: 951)
	let v952 = mix(seed, k: 952)
	let v953 = mix(seed, k: 953)
	let v954 = mix(seed, k: 954)
	let v955 = mix(seed, k: 955)
	let v956 = mix(seed, k: 956)
	let v957 = mix(seed, k: 957)
	let v958 = mix(seed, k: 958)
	let v959 = mix(seed, k: 959)
	let v960 = mix(seed, k: 960)
	let v961 = mix(seed, k: 961)
	let v962 = mix(seed, k: 962)
	let v963 = mix(seed, k: 963)
	let v964 = mix(seed, k: 964)
	let v965 = mix(seed, k: 965)
	let v966 = mix(seed, k: 966)
	let v967 = mix(seed, k: 967)
	let v968 = mix(seed, k: 968)
	let v969 = mix(seed, k: 969)
	let v970 = mix(seed, k: 970)
	let v971 = mix(seed, k: 971)
	let v972 = mix(seed, k: 972)
	let v973 = mix(seed, k: 973)
	let v974 = mix(seed, k: 974)
	let v975 = mix(seed, k: 975)
	let v976 = mix(seed, k: 976)
	let v977 = mix(seed, k: 977)
	let v978 = mix(seed, k: 978)
	let v979 = mix(seed, k: 979)
	let v980 = mix(seed, k: 980)
	let v981 = mix(seed, k: 981)
	let v982 = mix(seed, k: 982)
	let v983 = mix(seed, k: 983)
	let v984 = mix(seed, k: 984)
	let v985 = mix(seed, k: 985)
	let v986 = mix(seed, k: 986)
	let v987 = mix(seed, k: 987)
	let v988 = mix(seed, k: 988)
	let v989 = mix(seed, k: 989)
	let v990 = mix(seed, k: 990)
	let v991 = mix(seed, k: 991)
	let v992 = mix(seed, k: 992)
	let v993 = mix(seed, k: 993)
	let v994 = mix(seed, k: 994)
	let v995 = mix(seed, k: 995)
	let v996 = mix(seed, k: 996)
	let v997 = mix(seed, k: 997)
	let v998 = mix(seed, k: 998)
	let v999 = mix(seed, k: 999)
	let t = touch(seed)
	var sum = t
	sum = (sum + v0 + v1 + v2 + v3 + v4 + v5 + v6 + v7 + v8 + v9 + v10 + v11 + v12 + v13 + v14 + v15 + v16 + v17 + v18 + v19) mod 1000003
	sum = (sum + v20 + v21 + v22 + v23 + v24 + v25 + v26 + v27 + v28 + v29 + v30 + v31 + v32 + v33 + v34 + v35 + v36 + v37 + v38 + v39) mod 1000003
	sum = (sum + v40 + v41 + v42 + v43 + v44 + v45 + v46 + v47 + v48 + v49 + v50 + v51 + v52 + v53 + v54 + v55 + v56 + v57 + v58 + v59) mod 1000003
	sum = (sum + v60 + v61 + v62 + v63 + v64 + v65 + v66 + v67 + v68 + v69 + v70 + v71 + v72 + v73 + v74 + v75 + v76 + v77 + v78 + v79) mod 1000003
	sum = (sum + v80 + v81 + v82 + v83 + v84 + v85 + v86 + v87 + v88 + v89 + v90 + v91 + v92 + v93 + v94 + v95 + v96 + v97 + v98 + v99) mod 1000003
	sum = (sum + v100 + v101 + v102 + v103 + v104 + v105 + v106 + v107 + v108 + v109 + v110 + v111 + v112 + v113 + v114 + v115 + v116 + v117 + v118 + v119) mod 1000003
	sum = (sum + v120 + v121 + v122 + v123 + v124 + v125 + v126 + v127 + v128 + v129 + v130 + v131 + v132 + v133 + v134 + v135 + v136 + v137 + v138 + v139) mod 1000003
	sum = (sum + v140 + v141 + v142 + v143 + v144 + v145 + v146 + v147 + v148 + v149 + v150 + v151 + v152 + v153 + v154 + v155 + v156 + v157 + v158 + v159) mod 1000003
	sum = (sum + v160 + v161 + v162 + v163 + v164 + v165 + v166 + v167 + v168 + v169 + v170 + v171 + v172 + v173 + v174 + v175 + v176 + v177 + v178 + v179) mod 1000003
	sum = (sum + v180 + v181 + v182 + v183 + v184 + v185 + v186 + v187 + v188 + v189 + v190 + v191 + v192 + v193 + v194 + v195 + v196 + v197 + v198 + v199) mod 1000003
	sum = (sum + v200 + v201 + v202 + v203 + v204 + v205 + v206 + v207 + v208 + v209 + v210 + v211 + v212 + v213 + v214 + v215 + v216 + v217 + v218 + v219) mod 1000003
	sum = (sum + v220 + v221 + v222 + v223 + v224 + v225 + v226 + v227 + v228 + v229 + v230 + v231 + v232 + v233 + v234 + v235 + v236 + v237 + v238 + v239) mod 1000003
	sum = (sum + v240 + v241 + v242 + v243 + v244 + v245 + v246 + v247 + v248 + v249 + v250 + v251 + v252 + v253 + v254 + v255 + v256 + v257 + v258 + v259) mod 1000003
	sum = (sum + v260 + v261 + v262 + v263 + v264 + v265 + v266 + v267 + v268 + v269 + v270 + v271 + v272 + v273 + v274 + v275 + v276 + v277 + v278 + v279) mod 1000003
	sum = (sum + v280 + v281 + v282 + v283 + v284 + v285 + v286 + v287 + v288 + v289 + v290 + v291 + v292 + v293 + v294 + v295 + v296 + v297 + v298 + v299) mod 1000003
	sum = (sum + v300 + v301 + v302 + v303 + v304 + v305 + v306 + v307 + v308 + v309 + v310 + v311 + v312 + v313 + v314 + v315 + v316 + v317 + v318 + v319) mod 1000003
	sum = (sum + v320 + v321 + v322 + v323 + v324 + v325 + v326 + v327 + v328 + v329 + v330 + v331 + v332 + v333 + v334 + v335 + v336 + v337 + v338 + v339) mod 1000003
	sum = (sum + v340 + v341 + v342 + v343 + v344 + v345 + v346 + v347 + v348 + v349 + v350 + v351 + v352 + v353 + v354 + v355 + v356 + v357 + v358 + v359) mod 1000003
	sum = (sum + v360 + v361 + v362 + v363 + v364 + v365 + v366 + v367 + v368 + v369 + v370 + v371 + v372 + v373 + v374 + v375 + v376 + v377 + v378 + v379) mod 1000003
	sum = (sum + v380 + v381 + v382 + v383 + v384 + v385 + v386 + v387 + v388 + v389 + v390 + v391 + v392 + v393 + v394 + v395 + v396 + v397 + v398 + v399) mod 1000003
	sum = (sum + v400 + v401 + v402 + v403 + v404 + v405 + v406 + v407 + v408 + v409 + v410 + v411 + v412 + v413 + v414 + v415 + v416 + v417 + v418 + v419) mod 1000003
	sum = (sum + v420 + v421 + v422 + v423 + v424 + v425 + v426 + v427 + v428 + v429 + v430 + v431 + v432 + v433 + v434 + v435 + v436 + v437 + v438 + v439) mod 1000003
	sum = (sum + v440 + v441 + v442 + v443 + v444 + v445 + v446 + v447 + v448 + v449 + v450 + v451 + v452 + v453 + v454 + v455 + v456 + v457 + v458 + v459) mod 1000003
	sum = (sum + v460 + v461 + v462 + v463 + v464 + v465 + v466 + v467 + v468 + v469 + v470 + v471 + v472 + v473 + v474 + v475 + v476 + v477 + v478 + v479) mod 1000003
	sum = (sum + v480 + v481 + v482 + v483 + v484 + v485 + v486 + v487 + v488 + v489 + v490 + v491 + v492 + v493 + v494 + v495 + v496 + v497 + v498 + v499) mod 1000003
	sum = (sum + v500 + v501 + v502 + v503 + v504 + v505 + v506 + v507 + v508 + v509 + v510 + v511 + v512 + v513 + v514 + v515 + v516 + v517 + v518 + v519) mod 1000003
	sum = (sum + v520 + v521 + v522 + v523 + v524 + v525 + v526 + v527 + v528 + v529 + v530 + v531 + v532 + v533 + v534 + v535 + v536 + v537 + v538 + v539) mod 1000003
	sum = (sum + v540 + v541 + v542 + v543 + v544 + v545 + v546 + v547 + v548 + v549 + v550 + v551 + v552 + v553 + v554 + v555 + v556 + v557 + v558 + v559) mod 1000003
	sum = (sum + v560 + v561 + v562 + v563 + v564 + v565 + v566 + v567 + v568 + v569 + v570 + v571 + v572 + v573 + v574 + v575 + v576 + v577 + v578 + v579) mod 1000003
	sum = (sum + v580 + v581 + v582 + v583 + v584 + v585 + v586 + v587 + v588 + v589 + v590 + v591 + v592 + v593 + v594 + v595 + v596 + v597 + v598 + v599) mod 1000003
	sum = (sum + v600 + v601 + v602 + v603 + v604 + v605 + v606 + v607 + v608 + v609 + v610 + v611 + v612 + v613 + v614 + v615 + v616 + v617 + v618 + v619) mod 1000003
	sum = (sum + v620 + v621 + v622 + v623 + v624 + v625 + v626 + v627 + v628 + v629 + v630 + v631 + v632 + v633 + v634 + v635 + v636 + v637 + v638 + v639) mod 1000003
	sum = (sum + v640 + v641 + v642 + v643 + v644 + v645 + v646 + v647 + v648 + v649 + v650 + v651 + v652 + v653 + v654 + v655 + v656 + v657 + v658 + v659) mod 1000003
	sum = (sum + v660 + v661 + v662 + v663 + v664 + v665 + v666 + v667 + v668 + v669 + v670 + v671 + v672 + v673 + v674 + v675 + v676 + v677 + v678 + v679) mod 1000003
	sum = (sum + v680 + v681 + v682 + v683 + v684 + v685 + v686 + v687 + v688 + v689 + v690 + v691 + v692 + v693 + v694 + v695 + v696 + v697 + v698 + v699) mod 1000003
	sum = (sum + v700 + v701 + v702 + v703 + v704 + v705 + v706 + v707 + v708 + v709 + v710 + v711 + v712 + v713 + v714 + v715 + v716 + v717 + v718 + v719) mod 1000003
	sum = (sum + v720 + v721 + v722 + v723 + v724 + v725 + v726 + v727 + v728 + v729 + v730 + v731 + v732 + v733 + v734 + v735 + v736 + v737 + v738 + v739) mod 1000003
	sum = (sum + v740 + v741 + v742 + v743 + v744 + v745 + v746 + v747 + v748 + v749 + v750 + v751 + v752 + v753 + v754 + v755 + v756 + v757 + v758 + v759) mod 1000003
	sum = (sum + v760 + v761 + v762 + v763 + v764 + v765 + v766 + v767 + v768 + v769 + v770 + v771 + v772 + v773 + v774 + v775 + v776 + v777 + v778 + v779) mod 1000003
	sum = (sum + v780 + v781 + v782 + v783 + v784 + v785 + v786 + v787 + v788 + v789 + v790 + v791 + v792 + v793 + v794 + v795 + v796 + v797 + v798 + v799) mod 1000003
	sum = (sum + v800 + v801 + v802 + v803 + v804 + v805 + v806 + v807 + v808 + v809 + v810 + v811 + v812 + v813 + v814 + v815 + v816 + v817 + v818 + v819) mod 1000003
	sum = (sum + v820 + v821 + v822 + v823 + v824 + v825 + v826 + v827 + v828 + v829 + v830 + v831 + v832 + v833 + v834 + v835 + v836 + v837 + v838 + v839) mod 1000003
	sum = (sum + v840 + v841 + v842 + v843 + v844 + v845 + v846 + v847 + v848 + v849 + v850 + v851 + v852 + v853 + v854 + v855 + v856 + v857 + v858 + v859) mod 1000003
	sum = (sum + v860 + v861 + v862 + v863 + v864 + v865 + v866 + v867 + v868 + v869 + v870 + v871 + v872 + v873 + v874 + v875 + v876 + v877 + v878 + v879) mod 1000003
	sum = (sum + v880 + v881 + v882 + v883 + v884 + v885 + v886 + v887 + v888 + v889 + v890 + v891 + v892 + v893 + v894 + v895 + v896 + v897 + v898 + v899) mod 1000003
	sum = (sum + v900 + v901 + v902 + v903 + v904 + v905 + v906 + v907 + v908 + v909 + v910 + v911 + v912 + v913 + v914 + v915 + v916 + v917 + v918 + v919) mod 1000003
	sum = (sum + v920 + v921 + v922 + v923 + v924 + v925 + v926 + v927 + v928 + v929 + v930 + v931 + v932 + v933 + v934 + v935 + v936 + v937 + v938 + v939) mod 1000003
	sum = (sum + v940 + v941 + v942 + v943 + v944 + v945 + v946 + v947 + v948 + v949 + v950 + v951 + v952 + v953 + v954 + v955 + v956 + v957 + v958 + v959) mod 1000003
	sum = (sum + v960 + v961 + v962 + v963 + v964 + v965 + v966 + v967 + v968 + v969 + v970 + v971 + v972 + v973 + v974 + v975 + v976 + v977 + v978 + v979) mod 1000003
	sum = (sum + v980 + v981 + v982 + v983 + v984 + v985 + v986 + v987 + v988 + v989 + v990 + v991 + v992 + v993 + v994 + v995 + v996 + v997 + v998 + v999) mod 1000003
	return sum
end 'wide'

function work(seed Integer) returns Integer
	Runtime.yield()
	return wide(seed)
end 'work'

function main() returns ExitCode
	let p = async work(7)
	let r = await p
	print("r={r}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
r=716508
```
```exitcode
0
```

<!-- test: async-stack-growth.main-recurses-deeper-than-the-os-stack -->
**`main` IS A GREEN THREAD, SO ITS STACK GROWS LIKE EVERY OTHER ONE.** Two million nested calls need tens of
megabytes on every lane — far past the 1 MB a Windows main thread reserves and the 8 MB a Linux or macOS one gets. In Go the
main goroutine's stack grows like any goroutine's, to a limit of a gigabyte. The `sleep` makes this a program
that runs the scheduler, which is what puts `main` on a green thread at all.
```maxon
typealias Integer = int(i64.min to i64.max)

function down(n Integer) returns Integer
	if n == 0 'bottom'
		return 0
	end 'bottom'
	return 1 + down(n - 1)
end 'down'

function main() returns ExitCode
	sleep(1)
	let depth = down(2000000)
	print("depth={depth}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
depth=2000000
```
```exitcode
0
```

<!-- test: async-stack-growth.a-stack-grown-by-deep-recursion-shrinks-once-idle -->
<!-- procs: 1 -->
**A STACK THAT GREW FOR ONE DEEP CALL IS GIVEN BACK ONCE THE THREAD HAS BEEN IDLE.** Go halves a goroutine's
stack when less than a quarter of it is in use (`vendor/go/src/runtime/stack.go`, `shrinkstack`). A coroutine
recurses twenty thousand deep, returns, and parks twice: the first park follows the growth too closely to
shrink, the second comes after the shrink window and halves the stack. `__Builtins.gtStackBytes()` is the
calling green thread's current stack size. The same recursion then runs again on the shrunk stack, which must
grow back correctly.
```maxon
typealias Integer = int(i64.min to i64.max)

function down(n Integer) returns Integer
	if n == 0 'bottom'
		return 0
	end 'bottom'
	return 1 + down(n - 1)
end 'down'

function deepThenIdle() returns Integer
	let depth = down(20000)
	let grown = __Builtins.gtStackBytes()
	sleep(1100)
	sleep(1)
	let after = __Builtins.gtStackBytes()
	let again = down(20000)
	print("depth={depth} shrunk={after < grown} again={again}\n")
	return 0
end 'deepThenIdle'

function main() returns ExitCode
	let p = async deepThenIdle()
	let r = await p
	return r as ExitCode
end 'main'
```
```stdout
depth=20000 shrunk=true again=20000
```
```exitcode
0
```

<!-- test: async-stack-growth.a-stack-grown-in-the-current-window-is-not-shrunk -->
<!-- procs: 1 -->
**A STACK THAT GREW A MOMENT AGO IS KEPT.** The same recursion, then a park straight away: a thread that
just needed the stack is likely to need it again, so a shrink waits for the window to pass rather than copying
the stack down and back up.
```maxon
typealias Integer = int(i64.min to i64.max)

function down(n Integer) returns Integer
	if n == 0 'bottom'
		return 0
	end 'bottom'
	return 1 + down(n - 1)
end 'down'

function deepThenPark() returns Integer
	let depth = down(20000)
	let grown = __Builtins.gtStackBytes()
	sleep(1)
	let after = __Builtins.gtStackBytes()
	print("depth={depth} shrunk={after < grown}\n")
	return 0
end 'deepThenPark'

function main() returns ExitCode
	let p = async deepThenPark()
	let r = await p
	return r as ExitCode
end 'main'
```
```stdout
depth=20000 shrunk=false
```
```exitcode
0
```

<!-- test: async-stack-growth.the-starting-stack-follows-the-stacks-threads-needed -->
<!-- procs: 1 -->
**A NEW GREEN THREAD STARTS WITH THE STACK ITS PREDECESSORS NEEDED.** Go sizes a new goroutine's first stack
from the average of the stacks it has seen (`stack.go`, `startingStackSize`), so a program whose threads all
recurse does not pay the same growth on every one. Sixty-four coroutines each recurse four thousand deep and
finish, which closes a recompute window; a fresh coroutine then starts larger than the first one did. The
program arms no timer: the seed follows finished threads whether or not a timer ever fires.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function down(n Integer) returns Integer
	if n == 0 'bottom'
		return 0
	end 'bottom'
	return 1 + down(n - 1)
end 'down'

function seedBytes() returns Integer
	Runtime.yield()
	return __Builtins.gtStackBytes()
end 'seedBytes'

function deep(n Integer) returns Integer
	Runtime.yield()
	return down(n)
end 'deep'

function main() returns ExitCode
	let first = async seedBytes()
	let before = await first
	var ps = IntPromiseArray.create()
	var i = 0
	while i < 64 'deepEach'
		ps.push(async deep(4000))
		i = i + 1
	end 'deepEach'
	var total = 0
	var k = 0
	while k < 64 'awaitEach'
		let p = try ps.get(k) otherwise panic("ps.get OOB at {k} — bounded by the pushes above")
		total = total + await p
		k = k + 1
	end 'awaitEach'
	let later = async seedBytes()
	let after = await later
	print("total={total} larger={after > before}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
total=256000 larger=true
```
```exitcode
0
```

<!-- test: async-stack-growth.a-panic-after-a-shrink-prints-the-whole-backtrace -->
<!-- procs: 1 -->
**A SHRUNK STACK STILL READS AS ONE CHAIN OF FRAMES.** Moving a stack to a smaller one rewrites every saved frame
pointer that pointed into the old one; the panic backtrace is what reads that chain. A coroutine recurses deep,
parks twice so its stack is halved, then calls three named frames that panic: the trace names every frame from
the panicking one down to the coroutine's entry.
```maxon
typealias Integer = int(i64.min to i64.max)

function down(n Integer) returns Integer
	if n == 0 'bottom'
		return 0
	end 'bottom'
	return 1 + down(n - 1)
end 'down'

function third(n Integer) returns Integer
	if n > 0 'positive'
		panic("after the shrink")
	end 'positive'
	return n
end 'third'

function second(n Integer) returns Integer
	return third(n) + 1
end 'second'

function first(n Integer) returns Integer
	return second(n) + 1
end 'first'

function deepThenIdle() returns Integer
	let depth = down(20000)
	let grown = __Builtins.gtStackBytes()
	sleep(1100)
	sleep(1)
	let after = __Builtins.gtStackBytes()
	print("depth={depth} shrunk={after < grown}\n")
	return first(depth)
end 'deepThenIdle'

function main() returns ExitCode
	let p = async deepThenIdle()
	let r = await p
	return r as ExitCode
end 'main'
```
```stdout
depth=20000 shrunk=true
```
```stderr
panic at async-stack-growth.a-panic-after-a-shrink-prints-the-whole-backtrace.test:13: after the shrink
Stack trace:
  in third
  in second
  in first
  in deepThenIdle
  in __gt_trampoline
```
```exitcode
1
```

<!-- test: async-stack-growth.a-trace-deeper-than-the-cap-ends-with-an-elision-line -->
<!-- procs: 1 -->
**A BACKTRACE STOPS AT 100 FRAMES AND SAYS SO.** A coroutine parks once, then recurses 150 deep — growing its
stack several times on the way — and panics at the bottom. The walk follows the relocated chain frame by frame
inside the thread's own stack, prints the innermost 100, and ends with a line saying more frames were left out; a
trace without that line is the whole chain.
```maxon
typealias Integer = int(i64.min to i64.max)

function descend(n Integer) returns Integer
	if n == 0 'bottom'
		panic("at the bottom")
	end 'bottom'
	return 1 + descend(n - 1)
end 'descend'

function parkThenDescend() returns Integer
	sleep(1)
	return descend(150)
end 'parkThenDescend'

function main() returns ExitCode
	let p = async parkThenDescend()
	let r = await p
	return r as ExitCode
end 'main'
```
```stderr
panic at async-stack-growth.a-trace-deeper-than-the-cap-ends-with-an-elision-line.test:6: at the bottom
Stack trace:
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  in descend
  ...additional frames elided...
```
```exitcode
1
```

<!-- test: async-stack-growth.the-starting-stack-comes-back-down-when-threads-need-less -->
<!-- procs: 1 -->
**THE STARTING STACK FALLS AGAIN WHEN THE THREADS STOP NEEDING IT.** The average is of what each finished thread
NEEDED — its final size if it had to grow, else the depth it was seen at — not of the stack it was handed, so a
seed raised by one burst of deep threads cannot hold every later thread at that size. Sixty-four deep coroutines
raise the seed, as in the case above; then a hundred and twenty-eight shallow ones, each started on that raised
seed and never needing it, close two more windows, and a fresh coroutine starts where the first one did.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function down(n Integer) returns Integer
	if n == 0 'bottom'
		return 0
	end 'bottom'
	return 1 + down(n - 1)
end 'down'

function seedBytes() returns Integer
	Runtime.yield()
	return __Builtins.gtStackBytes()
end 'seedBytes'

function deep(n Integer) returns Integer
	Runtime.yield()
	return down(n)
end 'deep'

function shallow(n Integer) returns Integer
	Runtime.yield()
	return n
end 'shallow'

function main() returns ExitCode
	let first = async seedBytes()
	let before = await first
	var deeps = IntPromiseArray.create()
	var i = 0
	while i < 64 'deepEach'
		deeps.push(async deep(4000))
		i = i + 1
	end 'deepEach'
	var deepTotal = 0
	var k = 0
	while k < 64 'awaitDeep'
		let p = try deeps.get(k) otherwise panic("deeps.get OOB at {k} — bounded by the pushes above")
		deepTotal = deepTotal + await p
		k = k + 1
	end 'awaitDeep'
	let mid = async seedBytes()
	let raised = await mid
	var shallows = IntPromiseArray.create()
	var j = 0
	while j < 128 'shallowEach'
		shallows.push(async shallow(1))
		j = j + 1
	end 'shallowEach'
	var shallowTotal = 0
	var m = 0
	while m < 128 'awaitShallow'
		let p = try shallows.get(m) otherwise panic("shallows.get OOB at {m} — bounded by the pushes above")
		shallowTotal = shallowTotal + await p
		m = m + 1
	end 'awaitShallow'
	let later = async seedBytes()
	let after = await later
	print("deep={deepTotal} shallow={shallowTotal} raised={raised > before} restored={after == before}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
deep=256000 shallow=128 raised=true restored=true
```
```exitcode
0
```
