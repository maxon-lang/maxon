module {
  func @Array.count(__self_ptr: i64) -> i64 {
  entry:
    %354 = func.param __self_ptr : StdI64
    memref.store %354, __self_ptr
    %355 = memref.load_indirect %354+0
    memref.store %355, self.iterIndex
    %356 = arith.constant {value = 8 : i64}
    %357 = arith.addi %354, %356
    %358 = memref.load_indirect %357+0
    memref.store %358, self.managed.buffer
    %359 = memref.load_indirect %357+8
    memref.store %359, self.managed.length
    %360 = memref.load_indirect %357+16
    memref.store %360, self.managed.capacity
    %361 = memref.load self.iterIndex : i64
    %362 = memref.load self.managed.length : i64
    func.return %362
  }
  func @Array.get(__self_ptr: i64, index: i64) -> i64 {
  entry:
    %363 = func.param __self_ptr : StdI64
    memref.store %363, __self_ptr
    %364 = memref.load_indirect %363+0
    memref.store %364, self.iterIndex
    %365 = arith.constant {value = 8 : i64}
    %366 = arith.addi %363, %365
    %367 = memref.load_indirect %366+0
    memref.store %367, self.managed.buffer
    %368 = memref.load_indirect %366+8
    memref.store %368, self.managed.length
    %369 = memref.load_indirect %366+16
    memref.store %369, self.managed.capacity
    %370 = memref.load self.iterIndex : i64
    %371 = func.param index : StdI64
    memref.store %371, index
    %372 = memref.load self.managed.length : i64
    memref.store %372, len
    %373 = arith.constant {value = 0 : i64}
    %374 = arith.cmpi lt %371, %373
    cf.cond_br %374 [then: lower_0, else: lower_0.after]
  lower_0:
    %375 = arith.constant {value = 0 : i64}
    %376 = arith.constant {value = 1 : i64}
    %377 = arith.addi %375, %376
    func.error_return %377
  lower_0.after:
    %378 = memref.load index : i64
    %379 = memref.load len : i64
    %380 = arith.cmpi ge %378, %379
    cf.cond_br %380 [then: upper_1, else: upper_1.after]
  upper_1:
    %381 = arith.constant {value = 0 : i64}
    %382 = arith.constant {value = 1 : i64}
    %383 = arith.addi %381, %382
    func.error_return %383
  upper_1.after:
    %384 = memref.load index : i64
    %385 = memref.load self.managed.buffer : i64
    %386 = arith.constant {value = 8 : i64}
    %387 = arith.muli %384, %386
    %388 = arith.addi %385, %387
    %389 = memref.load_indirect %388+0
    func.return %389
  }
  func @Array.set(__self_ptr: i64, index: i64, value: i64) {
  entry:
    %390 = func.param __self_ptr : StdI64
    memref.store %390, __self_ptr
    %391 = memref.load_indirect %390+0
    memref.store %391, self.iterIndex
    %392 = arith.constant {value = 8 : i64}
    %393 = arith.addi %390, %392
    %394 = memref.load_indirect %393+0
    memref.store %394, self.managed.buffer
    %395 = memref.load_indirect %393+8
    memref.store %395, self.managed.length
    %396 = memref.load_indirect %393+16
    memref.store %396, self.managed.capacity
    %397 = memref.load self.iterIndex : i64
    %398 = func.param index : StdI64
    memref.store %398, index
    %399 = func.param value : StdI64
    memref.store %399, value
    %400 = memref.load self.managed.length : i64
    memref.store %400, len
    %401 = arith.constant {value = 0 : i64}
    %402 = arith.cmpi ge %398, %401
    cf.cond_br %402 [then: lower_0, else: lower_0.after]
  lower_0:
    %403 = memref.load index : i64
    %404 = memref.load len : i64
    %405 = arith.cmpi lt %403, %404
    cf.cond_br %405 [then: upper_1, else: upper_1.merge]
  upper_1:
    %406 = memref.load index : i64
    %407 = memref.load value : i64
    %408 = memref.load self.managed.buffer : i64
    %409 = arith.constant {value = 8 : i64}
    %410 = arith.muli %406, %409
    %411 = arith.addi %408, %410
    memref.store_indirect %407, %411+0
    cf.br upper_1.merge
  upper_1.merge:
  lower_0.after:
    func.return
  }
  func @Array.reserve(__self_ptr: i64, minCapacity: i64) {
  entry:
    %412 = func.param __self_ptr : StdI64
    memref.store %412, __self_ptr
    %413 = memref.load_indirect %412+0
    memref.store %413, self.iterIndex
    %414 = arith.constant {value = 8 : i64}
    %415 = arith.addi %412, %414
    %416 = memref.load_indirect %415+0
    memref.store %416, self.managed.buffer
    %417 = memref.load_indirect %415+8
    memref.store %417, self.managed.length
    %418 = memref.load_indirect %415+16
    memref.store %418, self.managed.capacity
    %419 = memref.load self.iterIndex : i64
    %420 = func.param minCapacity : StdI64
    memref.store %420, minCapacity
    %421 = memref.load self.managed.capacity : i64
    memref.store %421, cap
    %422 = arith.cmpi gt %420, %421
    cf.cond_br %422 [then: grow_0, else: grow_0.merge]
  grow_0:
    %423 = memref.load minCapacity : i64
    %424 = memref.load self.managed.buffer : i64
    %425 = arith.constant {value = 8 : i64}
    %426 = arith.muli %423, %425
    %427 = std.call_runtime @maxon_realloc %424, %426
    memref.store %427, self.managed.buffer
    memref.store %423, self.managed.capacity
    %428 = memref.load __self_ptr : i64
    memref.store_indirect %427, %428+8
    %429 = memref.load __self_ptr : i64
    memref.store_indirect %423, %429+24
    cf.br grow_0.merge
  grow_0.merge:
    func.return
  }
  func @Array.resize(__self_ptr: i64, newLength: i64) {
  entry:
    %430 = func.param __self_ptr : StdI64
    memref.store %430, __self_ptr
    %431 = memref.load_indirect %430+0
    memref.store %431, self.iterIndex
    %432 = arith.constant {value = 8 : i64}
    %433 = arith.addi %430, %432
    %434 = memref.load_indirect %433+0
    memref.store %434, self.managed.buffer
    %435 = memref.load_indirect %433+8
    memref.store %435, self.managed.length
    %436 = memref.load_indirect %433+16
    memref.store %436, self.managed.capacity
    %437 = memref.load self.iterIndex : i64
    %438 = func.param newLength : StdI64
    memref.store %438, newLength
    %440 = memref.load self.managed.capacity : i64
    memref.store %440, __selfbuf_439.managed.capacity
    %441 = memref.load self.managed.length : i64
    memref.store %441, __selfbuf_439.managed.length
    %442 = memref.load self.managed.buffer : i64
    memref.store %442, __selfbuf_439.managed.buffer
    %443 = memref.load self.iterIndex : i64
    memref.store %443, __selfbuf_439.iterIndex
    %444 = memref.lea __selfbuf_439
    func.call @Array.reserve %444, %438
    %445 = memref.load __selfbuf_439.iterIndex : i64
    memref.store %445, self.iterIndex
    %446 = memref.load __selfbuf_439.managed.buffer : i64
    memref.store %446, self.managed.buffer
    %447 = memref.load __selfbuf_439.managed.length : i64
    memref.store %447, self.managed.length
    %448 = memref.load __selfbuf_439.managed.capacity : i64
    memref.store %448, self.managed.capacity
    %449 = memref.load __self_ptr : i64
    %450 = memref.load self.iterIndex : i64
    memref.store_indirect %450, %449+0
    %451 = arith.constant {value = 8 : i64}
    %452 = arith.addi %449, %451
    %453 = memref.load self.managed.buffer : i64
    memref.store_indirect %453, %452+0
    %454 = memref.load self.managed.length : i64
    memref.store_indirect %454, %452+8
    %455 = memref.load self.managed.capacity : i64
    memref.store_indirect %455, %452+16
    memref.store %438, self.managed.length
    %456 = memref.load __self_ptr : i64
    memref.store_indirect %438, %456+16
    func.return
  }
  func @main() -> i64 {
  entry:
    %457 = arith.constant {value = 1 : i64}
    %458 = arith.constant {value = 2 : i64}
    %459 = arith.constant {value = 3 : i64}
    memref.store %459, __arr_0.2
    memref.store %458, __arr_0.1
    memref.store %457, __arr_0.0
    %460 = arith.constant {value = 0 : i64}
    %461 = arith.constant {value = 3 : i64}
    %462 = arith.constant {value = 0 : i64}
    memref.store %460, __struct_6.buffer
    memref.store %461, __struct_6.length
    memref.store %462, __struct_6.capacity
    %463 = arith.constant {value = 0 : i64}
    memref.store %463, __struct_8.iterIndex
    %464 = memref.load __struct_6.buffer : i64
    memref.store %464, __struct_8.managed.buffer
    %465 = memref.load __struct_6.length : i64
    memref.store %465, __struct_8.managed.length
    %466 = memref.load __struct_6.capacity : i64
    memref.store %466, __struct_8.managed.capacity
    %467 = memref.lea __arr_0
    %468 = std.ptr_to_i64 %467
    memref.store %468, __struct_8.managed.buffer
    %469 = arith.constant {value = 0 : i64}
    memref.store %469, __sret_9.iterIndex
    %470 = arith.constant {value = 0 : i64}
    memref.store %470, __sret_9.capacity
    %471 = arith.constant {value = 0 : i64}
    memref.store %471, __sret_9.count
    %472 = arith.constant {value = 0 : i64}
    memref.store %472, __sret_9.states.managed.capacity
    %473 = arith.constant {value = 0 : i64}
    memref.store %473, __sret_9.states.managed.length
    %474 = arith.constant {value = 0 : i64}
    memref.store %474, __sret_9.states.managed.buffer
    %475 = arith.constant {value = 0 : i64}
    memref.store %475, __sret_9.states.iterIndex
    %476 = arith.constant {value = 0 : i64}
    memref.store %476, __sret_9.elements.managed.capacity
    %477 = arith.constant {value = 0 : i64}
    memref.store %477, __sret_9.elements.managed.length
    %478 = arith.constant {value = 0 : i64}
    memref.store %478, __sret_9.elements.managed.buffer
    %479 = arith.constant {value = 0 : i64}
    memref.store %479, __sret_9.elements.iterIndex
    %480 = memref.lea __sret_9
    %481 = memref.load __struct_8.iterIndex : i64
    %482 = memref.load __struct_8.managed.buffer : i64
    %483 = memref.load __struct_8.managed.length : i64
    %484 = memref.load __struct_8.managed.capacity : i64
    func.call @__Set_3_i64.init %480, %481, %482, %483, %484
    %485 = memref.load __sret_9.elements.iterIndex : i64
    memref.store %485, s.elements.iterIndex
    %486 = memref.load __sret_9.elements.managed.buffer : i64
    memref.store %486, s.elements.managed.buffer
    %487 = memref.load __sret_9.elements.managed.length : i64
    memref.store %487, s.elements.managed.length
    %488 = memref.load __sret_9.elements.managed.capacity : i64
    memref.store %488, s.elements.managed.capacity
    %489 = memref.load __sret_9.states.iterIndex : i64
    memref.store %489, s.states.iterIndex
    %490 = memref.load __sret_9.states.managed.buffer : i64
    memref.store %490, s.states.managed.buffer
    %491 = memref.load __sret_9.states.managed.length : i64
    memref.store %491, s.states.managed.length
    %492 = memref.load __sret_9.states.managed.capacity : i64
    memref.store %492, s.states.managed.capacity
    %493 = memref.load __sret_9.count : i64
    memref.store %493, s.count
    %494 = memref.load __sret_9.capacity : i64
    memref.store %494, s.capacity
    %495 = memref.load __sret_9.iterIndex : i64
    memref.store %495, s.iterIndex
    %497 = memref.load s.iterIndex : i64
    memref.store %497, __selfbuf_496.iterIndex
    %498 = memref.load s.capacity : i64
    memref.store %498, __selfbuf_496.capacity
    %499 = memref.load s.count : i64
    memref.store %499, __selfbuf_496.count
    %500 = memref.load s.states.managed.capacity : i64
    memref.store %500, __selfbuf_496.states.managed.capacity
    %501 = memref.load s.states.managed.length : i64
    memref.store %501, __selfbuf_496.states.managed.length
    %502 = memref.load s.states.managed.buffer : i64
    memref.store %502, __selfbuf_496.states.managed.buffer
    %503 = memref.load s.states.iterIndex : i64
    memref.store %503, __selfbuf_496.states.iterIndex
    %504 = memref.load s.elements.managed.capacity : i64
    memref.store %504, __selfbuf_496.elements.managed.capacity
    %505 = memref.load s.elements.managed.length : i64
    memref.store %505, __selfbuf_496.elements.managed.length
    %506 = memref.load s.elements.managed.buffer : i64
    memref.store %506, __selfbuf_496.elements.managed.buffer
    %507 = memref.load s.elements.iterIndex : i64
    memref.store %507, __selfbuf_496.elements.iterIndex
    %508 = memref.lea __selfbuf_496
    %509 = func.call @__Set_3_i64.count %508
    %510 = memref.load __selfbuf_496.elements.iterIndex : i64
    memref.store %510, s.elements.iterIndex
    %511 = memref.load __selfbuf_496.elements.managed.buffer : i64
    memref.store %511, s.elements.managed.buffer
    %512 = memref.load __selfbuf_496.elements.managed.length : i64
    memref.store %512, s.elements.managed.length
    %513 = memref.load __selfbuf_496.elements.managed.capacity : i64
    memref.store %513, s.elements.managed.capacity
    %514 = memref.load __selfbuf_496.states.iterIndex : i64
    memref.store %514, s.states.iterIndex
    %515 = memref.load __selfbuf_496.states.managed.buffer : i64
    memref.store %515, s.states.managed.buffer
    %516 = memref.load __selfbuf_496.states.managed.length : i64
    memref.store %516, s.states.managed.length
    %517 = memref.load __selfbuf_496.states.managed.capacity : i64
    memref.store %517, s.states.managed.capacity
    %518 = memref.load __selfbuf_496.count : i64
    memref.store %518, s.count
    %519 = memref.load __selfbuf_496.capacity : i64
    memref.store %519, s.capacity
    %520 = memref.load __selfbuf_496.iterIndex : i64
    memref.store %520, s.iterIndex
    func.return %509
  }
  func @IntArray.get(__self_ptr: i64, index: i64) -> i64 {
  entry:
    %521 = func.param __self_ptr : StdI64
    memref.store %521, __self_ptr
    %522 = memref.load_indirect %521+0
    memref.store %522, self.iterIndex
    %523 = arith.constant {value = 8 : i64}
    %524 = arith.addi %521, %523
    %525 = memref.load_indirect %524+0
    memref.store %525, self.managed.buffer
    %526 = memref.load_indirect %524+8
    memref.store %526, self.managed.length
    %527 = memref.load_indirect %524+16
    memref.store %527, self.managed.capacity
    %528 = memref.load self.iterIndex : i64
    %529 = func.param index : StdI64
    memref.store %529, index
    %530 = memref.load self.managed.length : i64
    memref.store %530, len
    %531 = arith.constant {value = 0 : i64}
    %532 = arith.cmpi lt %529, %531
    cf.cond_br %532 [then: lower_0, else: lower_0.after]
  lower_0:
    %533 = arith.constant {value = 0 : i64}
    %534 = arith.constant {value = 1 : i64}
    %535 = arith.addi %533, %534
    func.error_return %535
  lower_0.after:
    %536 = memref.load index : i64
    %537 = memref.load len : i64
    %538 = arith.cmpi ge %536, %537
    cf.cond_br %538 [then: upper_1, else: upper_1.after]
  upper_1:
    %539 = arith.constant {value = 0 : i64}
    %540 = arith.constant {value = 1 : i64}
    %541 = arith.addi %539, %540
    func.error_return %541
  upper_1.after:
    %542 = memref.load index : i64
    %543 = memref.load self.managed.buffer : i64
    %544 = arith.constant {value = 8 : i64}
    %545 = arith.muli %542, %544
    %546 = arith.addi %543, %545
    %547 = memref.load_indirect %546+0
    func.return %547
  }
  func @IntArray.set(__self_ptr: i64, index: i64, value: i64) {
  entry:
    %548 = func.param __self_ptr : StdI64
    memref.store %548, __self_ptr
    %549 = memref.load_indirect %548+0
    memref.store %549, self.iterIndex
    %550 = arith.constant {value = 8 : i64}
    %551 = arith.addi %548, %550
    %552 = memref.load_indirect %551+0
    memref.store %552, self.managed.buffer
    %553 = memref.load_indirect %551+8
    memref.store %553, self.managed.length
    %554 = memref.load_indirect %551+16
    memref.store %554, self.managed.capacity
    %555 = memref.load self.iterIndex : i64
    %556 = func.param index : StdI64
    memref.store %556, index
    %557 = func.param value : StdI64
    memref.store %557, value
    %558 = memref.load self.managed.length : i64
    memref.store %558, len
    %559 = arith.constant {value = 0 : i64}
    %560 = arith.cmpi ge %556, %559
    cf.cond_br %560 [then: lower_0, else: lower_0.after]
  lower_0:
    %561 = memref.load index : i64
    %562 = memref.load len : i64
    %563 = arith.cmpi lt %561, %562
    cf.cond_br %563 [then: upper_1, else: upper_1.merge]
  upper_1:
    %564 = memref.load index : i64
    %565 = memref.load value : i64
    %566 = memref.load self.managed.buffer : i64
    %567 = arith.constant {value = 8 : i64}
    %568 = arith.muli %564, %567
    %569 = arith.addi %566, %568
    memref.store_indirect %565, %569+0
    cf.br upper_1.merge
  upper_1.merge:
  lower_0.after:
    func.return
  }
  func @IntArray.reserve(__self_ptr: i64, minCapacity: i64) {
  entry:
    %570 = func.param __self_ptr : StdI64
    memref.store %570, __self_ptr
    %571 = memref.load_indirect %570+0
    memref.store %571, self.iterIndex
    %572 = arith.constant {value = 8 : i64}
    %573 = arith.addi %570, %572
    %574 = memref.load_indirect %573+0
    memref.store %574, self.managed.buffer
    %575 = memref.load_indirect %573+8
    memref.store %575, self.managed.length
    %576 = memref.load_indirect %573+16
    memref.store %576, self.managed.capacity
    %577 = memref.load self.iterIndex : i64
    %578 = func.param minCapacity : StdI64
    memref.store %578, minCapacity
    %579 = memref.load self.managed.capacity : i64
    memref.store %579, cap
    %580 = arith.cmpi gt %578, %579
    cf.cond_br %580 [then: grow_0, else: grow_0.merge]
  grow_0:
    %581 = memref.load minCapacity : i64
    %582 = memref.load self.managed.buffer : i64
    %583 = arith.constant {value = 8 : i64}
    %584 = arith.muli %581, %583
    %585 = std.call_runtime @maxon_realloc %582, %584
    memref.store %585, self.managed.buffer
    memref.store %581, self.managed.capacity
    %586 = memref.load __self_ptr : i64
    memref.store_indirect %585, %586+8
    %587 = memref.load __self_ptr : i64
    memref.store_indirect %581, %587+24
    cf.br grow_0.merge
  grow_0.merge:
    func.return
  }
  func @IntArray.resize(__self_ptr: i64, newLength: i64) {
  entry:
    %588 = func.param __self_ptr : StdI64
    memref.store %588, __self_ptr
    %589 = memref.load_indirect %588+0
    memref.store %589, self.iterIndex
    %590 = arith.constant {value = 8 : i64}
    %591 = arith.addi %588, %590
    %592 = memref.load_indirect %591+0
    memref.store %592, self.managed.buffer
    %593 = memref.load_indirect %591+8
    memref.store %593, self.managed.length
    %594 = memref.load_indirect %591+16
    memref.store %594, self.managed.capacity
    %595 = memref.load self.iterIndex : i64
    %596 = func.param newLength : StdI64
    memref.store %596, newLength
    %598 = memref.load self.managed.capacity : i64
    memref.store %598, __selfbuf_597.managed.capacity
    %599 = memref.load self.managed.length : i64
    memref.store %599, __selfbuf_597.managed.length
    %600 = memref.load self.managed.buffer : i64
    memref.store %600, __selfbuf_597.managed.buffer
    %601 = memref.load self.iterIndex : i64
    memref.store %601, __selfbuf_597.iterIndex
    %602 = memref.lea __selfbuf_597
    func.call @IntArray.reserve %602, %596
    %603 = memref.load __selfbuf_597.iterIndex : i64
    memref.store %603, self.iterIndex
    %604 = memref.load __selfbuf_597.managed.buffer : i64
    memref.store %604, self.managed.buffer
    %605 = memref.load __selfbuf_597.managed.length : i64
    memref.store %605, self.managed.length
    %606 = memref.load __selfbuf_597.managed.capacity : i64
    memref.store %606, self.managed.capacity
    %607 = memref.load __self_ptr : i64
    %608 = memref.load self.iterIndex : i64
    memref.store_indirect %608, %607+0
    %609 = arith.constant {value = 8 : i64}
    %610 = arith.addi %607, %609
    %611 = memref.load self.managed.buffer : i64
    memref.store_indirect %611, %610+0
    %612 = memref.load self.managed.length : i64
    memref.store_indirect %612, %610+8
    %613 = memref.load self.managed.capacity : i64
    memref.store_indirect %613, %610+16
    memref.store %596, self.managed.length
    %614 = memref.load __self_ptr : i64
    memref.store_indirect %596, %614+16
    func.return
  }
  func @__Set_3_i64.init(__sret: i64, arr.iterIndex: i64, arr.managed.buffer: i64, arr.managed.length: i64, arr.managed.capacity: i64) {
  entry:
    %615 = func.param __sret : StdI64
    memref.store %615, __sret
    %616 = func.param arr.iterIndex : StdI64
    memref.store %616, arr.iterIndex
    %617 = func.param arr.managed.buffer : StdI64
    memref.store %617, arr.managed.buffer
    %618 = func.param arr.managed.length : StdI64
    memref.store %618, arr.managed.length
    %619 = func.param arr.managed.capacity : StdI64
    memref.store %619, arr.managed.capacity
    %621 = memref.load arr.managed.capacity : i64
    memref.store %621, __selfbuf_620.managed.capacity
    %622 = memref.load arr.managed.length : i64
    memref.store %622, __selfbuf_620.managed.length
    %623 = memref.load arr.managed.buffer : i64
    memref.store %623, __selfbuf_620.managed.buffer
    %624 = memref.load arr.iterIndex : i64
    memref.store %624, __selfbuf_620.iterIndex
    %625 = memref.lea __selfbuf_620
    %626 = func.call @Array.count %625
    %627 = memref.load __selfbuf_620.iterIndex : i64
    memref.store %627, arr.iterIndex
    %628 = memref.load __selfbuf_620.managed.buffer : i64
    memref.store %628, arr.managed.buffer
    %629 = memref.load __selfbuf_620.managed.length : i64
    memref.store %629, arr.managed.length
    %630 = memref.load __selfbuf_620.managed.capacity : i64
    memref.store %630, arr.managed.capacity
    memref.store %626, numElements
    %631 = arith.constant {value = 16 : i64}
    memref.store %631, cap
    cf.br calc_cap_0.header
  calc_cap_0.header:
    %632 = arith.constant {value = 2 : i64}
    %633 = memref.load numElements : i64
    %634 = arith.muli %633, %632
    %635 = memref.load cap : i64
    %636 = arith.cmpi lt %635, %634
    cf.cond_br %636 [then: calc_cap_0, else: calc_cap_0.exit]
  calc_cap_0:
    %637 = arith.constant {value = 2 : i64}
    %638 = memref.load cap : i64
    %639 = arith.muli %638, %637
    memref.store %639, cap
    cf.br calc_cap_0.header
  calc_cap_0.exit:
    %640 = arith.constant {value = 0 : i64}
    %641 = arith.constant {value = 0 : i64}
    %642 = arith.constant {value = 0 : i64}
    %643 = arith.constant {value = 0 : i64}
    memref.store %641, __struct_116.buffer
    memref.store %642, __struct_116.length
    memref.store %643, __struct_116.capacity
    memref.store %640, __struct_117.iterIndex
    %644 = memref.load __struct_116.buffer : i64
    memref.store %644, __struct_117.managed.buffer
    %645 = memref.load __struct_116.length : i64
    memref.store %645, __struct_117.managed.length
    %646 = memref.load __struct_116.capacity : i64
    memref.store %646, __struct_117.managed.capacity
    %647 = memref.load __struct_117.iterIndex : i64
    memref.store %647, elems.iterIndex
    %648 = memref.load __struct_117.managed.buffer : i64
    memref.store %648, elems.managed.buffer
    %649 = memref.load __struct_117.managed.length : i64
    memref.store %649, elems.managed.length
    %650 = memref.load __struct_117.managed.capacity : i64
    memref.store %650, elems.managed.capacity
    %651 = memref.load cap : i64
    %653 = memref.load elems.managed.capacity : i64
    memref.store %653, __selfbuf_652.managed.capacity
    %654 = memref.load elems.managed.length : i64
    memref.store %654, __selfbuf_652.managed.length
    %655 = memref.load elems.managed.buffer : i64
    memref.store %655, __selfbuf_652.managed.buffer
    %656 = memref.load elems.iterIndex : i64
    memref.store %656, __selfbuf_652.iterIndex
    %657 = memref.lea __selfbuf_652
    func.call @Array.resize %657, %651
    %658 = memref.load __selfbuf_652.iterIndex : i64
    memref.store %658, elems.iterIndex
    %659 = memref.load __selfbuf_652.managed.buffer : i64
    memref.store %659, elems.managed.buffer
    %660 = memref.load __selfbuf_652.managed.length : i64
    memref.store %660, elems.managed.length
    %661 = memref.load __selfbuf_652.managed.capacity : i64
    memref.store %661, elems.managed.capacity
    %662 = arith.constant {value = 0 : i64}
    %663 = arith.constant {value = 0 : i64}
    %664 = arith.constant {value = 0 : i64}
    %665 = arith.constant {value = 0 : i64}
    memref.store %663, __struct_123.buffer
    memref.store %664, __struct_123.length
    memref.store %665, __struct_123.capacity
    memref.store %662, __struct_124.iterIndex
    %666 = memref.load __struct_123.buffer : i64
    memref.store %666, __struct_124.managed.buffer
    %667 = memref.load __struct_123.length : i64
    memref.store %667, __struct_124.managed.length
    %668 = memref.load __struct_123.capacity : i64
    memref.store %668, __struct_124.managed.capacity
    %669 = memref.load __struct_124.iterIndex : i64
    memref.store %669, sts.iterIndex
    %670 = memref.load __struct_124.managed.buffer : i64
    memref.store %670, sts.managed.buffer
    %671 = memref.load __struct_124.managed.length : i64
    memref.store %671, sts.managed.length
    %672 = memref.load __struct_124.managed.capacity : i64
    memref.store %672, sts.managed.capacity
    %673 = memref.load cap : i64
    %675 = memref.load sts.managed.capacity : i64
    memref.store %675, __selfbuf_674.managed.capacity
    %676 = memref.load sts.managed.length : i64
    memref.store %676, __selfbuf_674.managed.length
    %677 = memref.load sts.managed.buffer : i64
    memref.store %677, __selfbuf_674.managed.buffer
    %678 = memref.load sts.iterIndex : i64
    memref.store %678, __selfbuf_674.iterIndex
    %679 = memref.lea __selfbuf_674
    func.call @IntArray.resize %679, %673
    %680 = memref.load __selfbuf_674.iterIndex : i64
    memref.store %680, sts.iterIndex
    %681 = memref.load __selfbuf_674.managed.buffer : i64
    memref.store %681, sts.managed.buffer
    %682 = memref.load __selfbuf_674.managed.length : i64
    memref.store %682, sts.managed.length
    %683 = memref.load __selfbuf_674.managed.capacity : i64
    memref.store %683, sts.managed.capacity
    %684 = arith.constant {value = 0 : i64}
    %685 = memref.load cap : i64
    %686 = arith.constant {value = 0 : i64}
    %687 = memref.load elems.iterIndex : i64
    memref.store %687, __struct_129.elements.iterIndex
    %688 = memref.load elems.managed.buffer : i64
    memref.store %688, __struct_129.elements.managed.buffer
    %689 = memref.load elems.managed.length : i64
    memref.store %689, __struct_129.elements.managed.length
    %690 = memref.load elems.managed.capacity : i64
    memref.store %690, __struct_129.elements.managed.capacity
    %691 = memref.load sts.iterIndex : i64
    memref.store %691, __struct_129.states.iterIndex
    %692 = memref.load sts.managed.buffer : i64
    memref.store %692, __struct_129.states.managed.buffer
    %693 = memref.load sts.managed.length : i64
    memref.store %693, __struct_129.states.managed.length
    %694 = memref.load sts.managed.capacity : i64
    memref.store %694, __struct_129.states.managed.capacity
    memref.store %684, __struct_129.count
    memref.store %685, __struct_129.capacity
    memref.store %686, __struct_129.iterIndex
    %695 = memref.load __struct_129.elements.iterIndex : i64
    memref.store %695, result.elements.iterIndex
    %696 = memref.load __struct_129.elements.managed.buffer : i64
    memref.store %696, result.elements.managed.buffer
    %697 = memref.load __struct_129.elements.managed.length : i64
    memref.store %697, result.elements.managed.length
    %698 = memref.load __struct_129.elements.managed.capacity : i64
    memref.store %698, result.elements.managed.capacity
    %699 = memref.load __struct_129.states.iterIndex : i64
    memref.store %699, result.states.iterIndex
    %700 = memref.load __struct_129.states.managed.buffer : i64
    memref.store %700, result.states.managed.buffer
    %701 = memref.load __struct_129.states.managed.length : i64
    memref.store %701, result.states.managed.length
    %702 = memref.load __struct_129.states.managed.capacity : i64
    memref.store %702, result.states.managed.capacity
    %703 = memref.load __struct_129.count : i64
    memref.store %703, result.count
    %704 = memref.load __struct_129.capacity : i64
    memref.store %704, result.capacity
    %705 = memref.load __struct_129.iterIndex : i64
    memref.store %705, result.iterIndex
    %706 = arith.constant {value = 0 : i64}
    memref.store %706, __for_idx_2
    %707 = memref.load arr.managed.length : i64
    memref.store %707, __for_len_2
    cf.br insert_loop_1.header
  insert_loop_1.header:
    %708 = memref.load __for_idx_2 : i64
    %709 = memref.load __for_len_2 : i64
    %710 = arith.cmpi lt %708, %709
    cf.cond_br %710 [then: insert_loop_1, else: insert_loop_1.exit]
  insert_loop_1:
    %711 = memref.load __for_idx_2 : i64
    %712 = memref.load arr.managed.buffer : i64
    %713 = arith.constant {value = 8 : i64}
    %714 = arith.muli %711, %713
    %715 = arith.addi %712, %714
    %716 = memref.load_indirect %715+0
    memref.store %716, elem
    %717 = memref.load __for_idx_2 : i64
    %718 = arith.constant {value = 1 : i64}
    %719 = arith.addi %717, %718
    memref.store %719, __for_idx_2
    %721 = memref.load result.iterIndex : i64
    memref.store %721, __selfbuf_720.iterIndex
    %722 = memref.load result.capacity : i64
    memref.store %722, __selfbuf_720.capacity
    %723 = memref.load result.count : i64
    memref.store %723, __selfbuf_720.count
    %724 = memref.load result.states.managed.capacity : i64
    memref.store %724, __selfbuf_720.states.managed.capacity
    %725 = memref.load result.states.managed.length : i64
    memref.store %725, __selfbuf_720.states.managed.length
    %726 = memref.load result.states.managed.buffer : i64
    memref.store %726, __selfbuf_720.states.managed.buffer
    %727 = memref.load result.states.iterIndex : i64
    memref.store %727, __selfbuf_720.states.iterIndex
    %728 = memref.load result.elements.managed.capacity : i64
    memref.store %728, __selfbuf_720.elements.managed.capacity
    %729 = memref.load result.elements.managed.length : i64
    memref.store %729, __selfbuf_720.elements.managed.length
    %730 = memref.load result.elements.managed.buffer : i64
    memref.store %730, __selfbuf_720.elements.managed.buffer
    %731 = memref.load result.elements.iterIndex : i64
    memref.store %731, __selfbuf_720.elements.iterIndex
    %732 = memref.lea __selfbuf_720
    func.call @__Set_3_i64.insert %732, %716
    %733 = memref.load __selfbuf_720.elements.iterIndex : i64
    memref.store %733, result.elements.iterIndex
    %734 = memref.load __selfbuf_720.elements.managed.buffer : i64
    memref.store %734, result.elements.managed.buffer
    %735 = memref.load __selfbuf_720.elements.managed.length : i64
    memref.store %735, result.elements.managed.length
    %736 = memref.load __selfbuf_720.elements.managed.capacity : i64
    memref.store %736, result.elements.managed.capacity
    %737 = memref.load __selfbuf_720.states.iterIndex : i64
    memref.store %737, result.states.iterIndex
    %738 = memref.load __selfbuf_720.states.managed.buffer : i64
    memref.store %738, result.states.managed.buffer
    %739 = memref.load __selfbuf_720.states.managed.length : i64
    memref.store %739, result.states.managed.length
    %740 = memref.load __selfbuf_720.states.managed.capacity : i64
    memref.store %740, result.states.managed.capacity
    %741 = memref.load __selfbuf_720.count : i64
    memref.store %741, result.count
    %742 = memref.load __selfbuf_720.capacity : i64
    memref.store %742, result.capacity
    %743 = memref.load __selfbuf_720.iterIndex : i64
    memref.store %743, result.iterIndex
    cf.br insert_loop_1.header
  insert_loop_1.exit:
    %744 = memref.load __sret : i64
    %745 = arith.constant {value = 0 : i64}
    %746 = arith.addi %744, %745
    %747 = memref.load result.elements.iterIndex : i64
    memref.store_indirect %747, %746+0
    %748 = arith.constant {value = 8 : i64}
    %749 = arith.addi %746, %748
    %750 = memref.load result.elements.managed.buffer : i64
    memref.store_indirect %750, %749+0
    %751 = memref.load result.elements.managed.length : i64
    memref.store_indirect %751, %749+8
    %752 = memref.load result.elements.managed.capacity : i64
    memref.store_indirect %752, %749+16
    %753 = arith.constant {value = 8 : i64}
    %754 = arith.addi %744, %753
    %755 = memref.load result.states.iterIndex : i64
    memref.store_indirect %755, %754+0
    %756 = arith.constant {value = 8 : i64}
    %757 = arith.addi %754, %756
    %758 = memref.load result.states.managed.buffer : i64
    memref.store_indirect %758, %757+0
    %759 = memref.load result.states.managed.length : i64
    memref.store_indirect %759, %757+8
    %760 = memref.load result.states.managed.capacity : i64
    memref.store_indirect %760, %757+16
    %761 = memref.load result.count : i64
    memref.store_indirect %761, %744+16
    %762 = memref.load result.capacity : i64
    memref.store_indirect %762, %744+24
    %763 = memref.load result.iterIndex : i64
    memref.store_indirect %763, %744+32
    func.return
  }
  func @__Set_3_i64.insert(__self_ptr: i64, element: i64) {
  entry:
    %764 = func.param __self_ptr : StdI64
    memref.store %764, __self_ptr
    %765 = arith.constant {value = 0 : i64}
    %766 = arith.addi %764, %765
    %767 = memref.load_indirect %766+0
    memref.store %767, self.elements.iterIndex
    %768 = arith.constant {value = 8 : i64}
    %769 = arith.addi %766, %768
    %770 = memref.load_indirect %769+0
    memref.store %770, self.elements.managed.buffer
    %771 = memref.load_indirect %769+8
    memref.store %771, self.elements.managed.length
    %772 = memref.load_indirect %769+16
    memref.store %772, self.elements.managed.capacity
    %773 = arith.constant {value = 8 : i64}
    %774 = arith.addi %764, %773
    %775 = memref.load_indirect %774+0
    memref.store %775, self.states.iterIndex
    %776 = arith.constant {value = 8 : i64}
    %777 = arith.addi %774, %776
    %778 = memref.load_indirect %777+0
    memref.store %778, self.states.managed.buffer
    %779 = memref.load_indirect %777+8
    memref.store %779, self.states.managed.length
    %780 = memref.load_indirect %777+16
    memref.store %780, self.states.managed.capacity
    %781 = memref.load_indirect %764+16
    memref.store %781, self.count
    %782 = memref.load_indirect %764+24
    memref.store %782, self.capacity
    %783 = memref.load_indirect %764+32
    memref.store %783, self.iterIndex
    %784 = memref.load self.count : i64
    %785 = memref.load self.capacity : i64
    %786 = memref.load self.iterIndex : i64
    %787 = func.param element : StdI64
    memref.store %787, element
    %788 = arith.constant {value = 0 : i64}
    %789 = arith.cmpi eq %785, %788
    cf.cond_br %789 [then: init_empty_0, else: init_empty_0.merge]
  init_empty_0:
    %790 = arith.constant {value = 0 : i64}
    %791 = arith.constant {value = 0 : i64}
    %792 = arith.constant {value = 0 : i64}
    %793 = arith.constant {value = 0 : i64}
    memref.store %791, __struct_157.buffer
    memref.store %792, __struct_157.length
    memref.store %793, __struct_157.capacity
    memref.store %790, __struct_158.iterIndex
    %794 = memref.load __struct_157.buffer : i64
    memref.store %794, __struct_158.managed.buffer
    %795 = memref.load __struct_157.length : i64
    memref.store %795, __struct_158.managed.length
    %796 = memref.load __struct_157.capacity : i64
    memref.store %796, __struct_158.managed.capacity
    %797 = memref.load __struct_158.iterIndex : i64
    memref.store %797, newElements.iterIndex
    %798 = memref.load __struct_158.managed.buffer : i64
    memref.store %798, newElements.managed.buffer
    %799 = memref.load __struct_158.managed.length : i64
    memref.store %799, newElements.managed.length
    %800 = memref.load __struct_158.managed.capacity : i64
    memref.store %800, newElements.managed.capacity
    %801 = arith.constant {value = 16 : i64}
    %803 = memref.load newElements.managed.capacity : i64
    memref.store %803, __selfbuf_802.managed.capacity
    %804 = memref.load newElements.managed.length : i64
    memref.store %804, __selfbuf_802.managed.length
    %805 = memref.load newElements.managed.buffer : i64
    memref.store %805, __selfbuf_802.managed.buffer
    %806 = memref.load newElements.iterIndex : i64
    memref.store %806, __selfbuf_802.iterIndex
    %807 = memref.lea __selfbuf_802
    func.call @Array.resize %807, %801
    %808 = memref.load __selfbuf_802.iterIndex : i64
    memref.store %808, newElements.iterIndex
    %809 = memref.load __selfbuf_802.managed.buffer : i64
    memref.store %809, newElements.managed.buffer
    %810 = memref.load __selfbuf_802.managed.length : i64
    memref.store %810, newElements.managed.length
    %811 = memref.load __selfbuf_802.managed.capacity : i64
    memref.store %811, newElements.managed.capacity
    %812 = arith.constant {value = 0 : i64}
    %813 = arith.constant {value = 0 : i64}
    %814 = arith.constant {value = 0 : i64}
    %815 = arith.constant {value = 0 : i64}
    memref.store %813, __struct_164.buffer
    memref.store %814, __struct_164.length
    memref.store %815, __struct_164.capacity
    memref.store %812, __struct_165.iterIndex
    %816 = memref.load __struct_164.buffer : i64
    memref.store %816, __struct_165.managed.buffer
    %817 = memref.load __struct_164.length : i64
    memref.store %817, __struct_165.managed.length
    %818 = memref.load __struct_164.capacity : i64
    memref.store %818, __struct_165.managed.capacity
    %819 = memref.load __struct_165.iterIndex : i64
    memref.store %819, newStates.iterIndex
    %820 = memref.load __struct_165.managed.buffer : i64
    memref.store %820, newStates.managed.buffer
    %821 = memref.load __struct_165.managed.length : i64
    memref.store %821, newStates.managed.length
    %822 = memref.load __struct_165.managed.capacity : i64
    memref.store %822, newStates.managed.capacity
    %823 = arith.constant {value = 16 : i64}
    %825 = memref.load newStates.managed.capacity : i64
    memref.store %825, __selfbuf_824.managed.capacity
    %826 = memref.load newStates.managed.length : i64
    memref.store %826, __selfbuf_824.managed.length
    %827 = memref.load newStates.managed.buffer : i64
    memref.store %827, __selfbuf_824.managed.buffer
    %828 = memref.load newStates.iterIndex : i64
    memref.store %828, __selfbuf_824.iterIndex
    %829 = memref.lea __selfbuf_824
    func.call @IntArray.resize %829, %823
    %830 = memref.load __selfbuf_824.iterIndex : i64
    memref.store %830, newStates.iterIndex
    %831 = memref.load __selfbuf_824.managed.buffer : i64
    memref.store %831, newStates.managed.buffer
    %832 = memref.load __selfbuf_824.managed.length : i64
    memref.store %832, newStates.managed.length
    %833 = memref.load __selfbuf_824.managed.capacity : i64
    memref.store %833, newStates.managed.capacity
    %834 = memref.load newElements.iterIndex : i64
    memref.store %834, elements.iterIndex
    %835 = memref.load newElements.managed.buffer : i64
    memref.store %835, elements.managed.buffer
    %836 = memref.load newElements.managed.length : i64
    memref.store %836, elements.managed.length
    %837 = memref.load newElements.managed.capacity : i64
    memref.store %837, elements.managed.capacity
    %838 = memref.load newStates.iterIndex : i64
    memref.store %838, states.iterIndex
    %839 = memref.load newStates.managed.buffer : i64
    memref.store %839, states.managed.buffer
    %840 = memref.load newStates.managed.length : i64
    memref.store %840, states.managed.length
    %841 = memref.load newStates.managed.capacity : i64
    memref.store %841, states.managed.capacity
    %842 = arith.constant {value = 16 : i64}
    memref.store %842, capacity
    %843 = memref.load __self_ptr : i64
    memref.store_indirect %842, %843+24
    cf.br init_empty_0.merge
  init_empty_0.merge:
    %844 = arith.constant {value = 3 : i64}
    %845 = memref.load capacity : i64
    %846 = arith.muli %845, %844
    %847 = arith.constant {value = 4 : i64}
    %848 = arith.sitofp %846
    %849 = arith.sitofp %847
    %850 = arith.divf %848, %849
    %851 = arith.fptosi %850
    memref.store %851, loadLimit
    %852 = memref.load self.count : i64
    %853 = arith.cmpi ge %852, %851
    cf.cond_br %853 [then: grow_1, else: grow_1.merge]
  grow_1:
    %855 = memref.load self.iterIndex : i64
    memref.store %855, __selfbuf_854.iterIndex
    %856 = memref.load self.capacity : i64
    memref.store %856, __selfbuf_854.capacity
    %857 = memref.load self.count : i64
    memref.store %857, __selfbuf_854.count
    %858 = memref.load self.states.managed.capacity : i64
    memref.store %858, __selfbuf_854.states.managed.capacity
    %859 = memref.load self.states.managed.length : i64
    memref.store %859, __selfbuf_854.states.managed.length
    %860 = memref.load self.states.managed.buffer : i64
    memref.store %860, __selfbuf_854.states.managed.buffer
    %861 = memref.load self.states.iterIndex : i64
    memref.store %861, __selfbuf_854.states.iterIndex
    %862 = memref.load self.elements.managed.capacity : i64
    memref.store %862, __selfbuf_854.elements.managed.capacity
    %863 = memref.load self.elements.managed.length : i64
    memref.store %863, __selfbuf_854.elements.managed.length
    %864 = memref.load self.elements.managed.buffer : i64
    memref.store %864, __selfbuf_854.elements.managed.buffer
    %865 = memref.load self.elements.iterIndex : i64
    memref.store %865, __selfbuf_854.elements.iterIndex
    %866 = memref.lea __selfbuf_854
    func.call @__Set_3_i64.grow %866
    %867 = memref.load __selfbuf_854.elements.iterIndex : i64
    memref.store %867, self.elements.iterIndex
    %868 = memref.load __selfbuf_854.elements.managed.buffer : i64
    memref.store %868, self.elements.managed.buffer
    %869 = memref.load __selfbuf_854.elements.managed.length : i64
    memref.store %869, self.elements.managed.length
    %870 = memref.load __selfbuf_854.elements.managed.capacity : i64
    memref.store %870, self.elements.managed.capacity
    %871 = memref.load __selfbuf_854.states.iterIndex : i64
    memref.store %871, self.states.iterIndex
    %872 = memref.load __selfbuf_854.states.managed.buffer : i64
    memref.store %872, self.states.managed.buffer
    %873 = memref.load __selfbuf_854.states.managed.length : i64
    memref.store %873, self.states.managed.length
    %874 = memref.load __selfbuf_854.states.managed.capacity : i64
    memref.store %874, self.states.managed.capacity
    %875 = memref.load __selfbuf_854.count : i64
    memref.store %875, self.count
    %876 = memref.load __selfbuf_854.capacity : i64
    memref.store %876, self.capacity
    %877 = memref.load __selfbuf_854.iterIndex : i64
    memref.store %877, self.iterIndex
    %878 = memref.load __self_ptr : i64
    %879 = arith.constant {value = 0 : i64}
    %880 = arith.addi %878, %879
    %881 = memref.load self.elements.iterIndex : i64
    memref.store_indirect %881, %880+0
    %882 = arith.constant {value = 8 : i64}
    %883 = arith.addi %880, %882
    %884 = memref.load self.elements.managed.buffer : i64
    memref.store_indirect %884, %883+0
    %885 = memref.load self.elements.managed.length : i64
    memref.store_indirect %885, %883+8
    %886 = memref.load self.elements.managed.capacity : i64
    memref.store_indirect %886, %883+16
    %887 = arith.constant {value = 8 : i64}
    %888 = arith.addi %878, %887
    %889 = memref.load self.states.iterIndex : i64
    memref.store_indirect %889, %888+0
    %890 = arith.constant {value = 8 : i64}
    %891 = arith.addi %888, %890
    %892 = memref.load self.states.managed.buffer : i64
    memref.store_indirect %892, %891+0
    %893 = memref.load self.states.managed.length : i64
    memref.store_indirect %893, %891+8
    %894 = memref.load self.states.managed.capacity : i64
    memref.store_indirect %894, %891+16
    %895 = memref.load self.count : i64
    memref.store_indirect %895, %878+16
    %896 = memref.load self.capacity : i64
    memref.store_indirect %896, %878+24
    %897 = memref.load self.iterIndex : i64
    memref.store_indirect %897, %878+32
    cf.br grow_1.merge
  grow_1.merge:
    %898 = memref.load element : i64
    memref.store %898, hash
    %899 = memref.load capacity : i64
    %900 = arith.remsi %898, %899
    memref.store %900, index
    %901 = arith.constant {value = 0 : i64}
    %902 = arith.cmpi lt %900, %901
    cf.cond_br %902 [then: fix_negative_2, else: fix_negative_2.merge]
  fix_negative_2:
    %903 = memref.load index : i64
    %904 = memref.load capacity : i64
    %905 = arith.addi %903, %904
    memref.store %905, index
    cf.br fix_negative_2.merge
  fix_negative_2.merge:
    %906 = arith.constant {value = -1 : i64}
    memref.store %906, firstDeleted
    %907 = arith.constant {value = 0 : i64}
    memref.store %907, probes
    cf.br probe_3.header
  probe_3.header:
    %908 = memref.load probes : i64
    %909 = memref.load capacity : i64
    %910 = arith.cmpi lt %908, %909
    cf.cond_br %910 [then: probe_3, else: probe_3.exit]
  probe_3:
    %911 = memref.load index : i64
    %913 = memref.load states.managed.capacity : i64
    memref.store %913, __selfbuf_912.managed.capacity
    %914 = memref.load states.managed.length : i64
    memref.store %914, __selfbuf_912.managed.length
    %915 = memref.load states.managed.buffer : i64
    memref.store %915, __selfbuf_912.managed.buffer
    %916 = memref.load states.iterIndex : i64
    memref.store %916, __selfbuf_912.iterIndex
    %917 = memref.lea __selfbuf_912
    %918, %919 = func.try_call @IntArray.get %917, %911
    memref.store %919, __error_flag
    %920 = memref.load __selfbuf_912.iterIndex : i64
    memref.store %920, states.iterIndex
    %921 = memref.load __selfbuf_912.managed.buffer : i64
    memref.store %921, states.managed.buffer
    %922 = memref.load __selfbuf_912.managed.length : i64
    memref.store %922, states.managed.length
    %923 = memref.load __selfbuf_912.managed.capacity : i64
    memref.store %923, states.managed.capacity
    %924 = arith.constant {value = 0 : i64}
    memref.store %924, __try_default_5
    memref.store %918, __try_result_4
    %925 = arith.constant {value = 0 : i64}
    %926 = arith.cmpi ne %919, %925
    cf.cond_br %926 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %927 = memref.load __try_default_5 : i64
    memref.store %927, __try_result_4
    cf.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %928 = memref.load __try_result_4 : i64
    memref.store %928, state
    %929 = arith.constant {value = 0 : i64}
    %930 = arith.cmpi eq %928, %929
    cf.cond_br %930 [then: empty_8, else: empty_8.after]
  empty_8:
    %931 = memref.load index : i64
    memref.store %931, insertIndex
    %932 = arith.constant {value = 0 : i64}
    %933 = memref.load firstDeleted : i64
    %934 = arith.cmpi ge %933, %932
    cf.cond_br %934 [then: use_deleted_9, else: use_deleted_9.merge]
  use_deleted_9:
    %935 = memref.load firstDeleted : i64
    memref.store %935, insertIndex
    cf.br use_deleted_9.merge
  use_deleted_9.merge:
    %936 = memref.load insertIndex : i64
    %937 = memref.load element : i64
    %939 = memref.load elements.managed.capacity : i64
    memref.store %939, __selfbuf_938.managed.capacity
    %940 = memref.load elements.managed.length : i64
    memref.store %940, __selfbuf_938.managed.length
    %941 = memref.load elements.managed.buffer : i64
    memref.store %941, __selfbuf_938.managed.buffer
    %942 = memref.load elements.iterIndex : i64
    memref.store %942, __selfbuf_938.iterIndex
    %943 = memref.lea __selfbuf_938
    func.call @Array.set %943, %936, %937
    %944 = memref.load __selfbuf_938.iterIndex : i64
    memref.store %944, elements.iterIndex
    %945 = memref.load __selfbuf_938.managed.buffer : i64
    memref.store %945, elements.managed.buffer
    %946 = memref.load __selfbuf_938.managed.length : i64
    memref.store %946, elements.managed.length
    %947 = memref.load __selfbuf_938.managed.capacity : i64
    memref.store %947, elements.managed.capacity
    %948 = memref.load insertIndex : i64
    %949 = arith.constant {value = 1 : i64}
    %951 = memref.load states.managed.capacity : i64
    memref.store %951, __selfbuf_950.managed.capacity
    %952 = memref.load states.managed.length : i64
    memref.store %952, __selfbuf_950.managed.length
    %953 = memref.load states.managed.buffer : i64
    memref.store %953, __selfbuf_950.managed.buffer
    %954 = memref.load states.iterIndex : i64
    memref.store %954, __selfbuf_950.iterIndex
    %955 = memref.lea __selfbuf_950
    func.call @IntArray.set %955, %948, %949
    %956 = memref.load __selfbuf_950.iterIndex : i64
    memref.store %956, states.iterIndex
    %957 = memref.load __selfbuf_950.managed.buffer : i64
    memref.store %957, states.managed.buffer
    %958 = memref.load __selfbuf_950.managed.length : i64
    memref.store %958, states.managed.length
    %959 = memref.load __selfbuf_950.managed.capacity : i64
    memref.store %959, states.managed.capacity
    %960 = arith.constant {value = 1 : i64}
    %961 = memref.load self.count : i64
    %962 = arith.addi %961, %960
    memref.store %962, count
    %963 = memref.load __self_ptr : i64
    memref.store_indirect %962, %963+16
    func.return
  empty_8.after:
    %964 = arith.constant {value = 2 : i64}
    %965 = memref.load state : i64
    %966 = arith.cmpi eq %965, %964
    %967 = arith.constant {value = 0 : i64}
    %968 = memref.load firstDeleted : i64
    %969 = arith.cmpi lt %968, %967
    %970 = arith.andi1 %966, %969
    cf.cond_br %970 [then: mark_deleted_10, else: mark_deleted_10.merge]
  mark_deleted_10:
    %971 = memref.load index : i64
    memref.store %971, firstDeleted
    cf.br mark_deleted_10.merge
  mark_deleted_10.merge:
    %972 = arith.constant {value = 1 : i64}
    %973 = memref.load state : i64
    %974 = arith.cmpi eq %973, %972
    cf.cond_br %974 [then: check_exists_11, else: check_exists_11.after]
  check_exists_11:
    %975 = memref.load index : i64
    %977 = memref.load elements.managed.capacity : i64
    memref.store %977, __selfbuf_976.managed.capacity
    %978 = memref.load elements.managed.length : i64
    memref.store %978, __selfbuf_976.managed.length
    %979 = memref.load elements.managed.buffer : i64
    memref.store %979, __selfbuf_976.managed.buffer
    %980 = memref.load elements.iterIndex : i64
    memref.store %980, __selfbuf_976.iterIndex
    %981 = memref.lea __selfbuf_976
    %982, %983 = func.try_call @Array.get %981, %975
    memref.store %983, __error_flag
    %984 = memref.load __selfbuf_976.iterIndex : i64
    memref.store %984, elements.iterIndex
    %985 = memref.load __selfbuf_976.managed.buffer : i64
    memref.store %985, elements.managed.buffer
    %986 = memref.load __selfbuf_976.managed.length : i64
    memref.store %986, elements.managed.length
    %987 = memref.load __selfbuf_976.managed.capacity : i64
    memref.store %987, elements.managed.capacity
    memref.store %983, __try_error_12
    memref.store %982, __try_result_13
    %988 = arith.constant {value = 0 : i64}
    %989 = arith.cmpi eq %983, %988
    cf.cond_br %989 [then: get_existing_14, else: get_existing_14.after]
  get_existing_14:
    %990 = memref.load __try_result_13 : i64
    memref.store %990, existing
    %991 = memref.load element : i64
    %992 = arith.cmpi eq %990, %991
    cf.cond_br %992 [then: exists_15, else: exists_15.after]
  exists_15:
    func.return
  exists_15.after:
  get_existing_14.after:
  check_exists_11.after:
    %993 = arith.constant {value = 1 : i64}
    %994 = memref.load index : i64
    %995 = arith.addi %994, %993
    %996 = memref.load capacity : i64
    %997 = arith.remsi %995, %996
    memref.store %997, index
    %998 = arith.constant {value = 1 : i64}
    %999 = memref.load probes : i64
    %1000 = arith.addi %999, %998
    memref.store %1000, probes
    cf.br probe_3.header
  probe_3.exit:
    %1001 = arith.constant {value = 0 : i64}
    %1002 = memref.load firstDeleted : i64
    %1003 = arith.cmpi ge %1002, %1001
    cf.cond_br %1003 [then: fallback_16, else: fallback_16.merge]
  fallback_16:
    %1004 = memref.load firstDeleted : i64
    %1005 = memref.load element : i64
    %1007 = memref.load elements.managed.capacity : i64
    memref.store %1007, __selfbuf_1006.managed.capacity
    %1008 = memref.load elements.managed.length : i64
    memref.store %1008, __selfbuf_1006.managed.length
    %1009 = memref.load elements.managed.buffer : i64
    memref.store %1009, __selfbuf_1006.managed.buffer
    %1010 = memref.load elements.iterIndex : i64
    memref.store %1010, __selfbuf_1006.iterIndex
    %1011 = memref.lea __selfbuf_1006
    func.call @Array.set %1011, %1004, %1005
    %1012 = memref.load __selfbuf_1006.iterIndex : i64
    memref.store %1012, elements.iterIndex
    %1013 = memref.load __selfbuf_1006.managed.buffer : i64
    memref.store %1013, elements.managed.buffer
    %1014 = memref.load __selfbuf_1006.managed.length : i64
    memref.store %1014, elements.managed.length
    %1015 = memref.load __selfbuf_1006.managed.capacity : i64
    memref.store %1015, elements.managed.capacity
    %1016 = memref.load firstDeleted : i64
    %1017 = arith.constant {value = 1 : i64}
    %1019 = memref.load states.managed.capacity : i64
    memref.store %1019, __selfbuf_1018.managed.capacity
    %1020 = memref.load states.managed.length : i64
    memref.store %1020, __selfbuf_1018.managed.length
    %1021 = memref.load states.managed.buffer : i64
    memref.store %1021, __selfbuf_1018.managed.buffer
    %1022 = memref.load states.iterIndex : i64
    memref.store %1022, __selfbuf_1018.iterIndex
    %1023 = memref.lea __selfbuf_1018
    func.call @IntArray.set %1023, %1016, %1017
    %1024 = memref.load __selfbuf_1018.iterIndex : i64
    memref.store %1024, states.iterIndex
    %1025 = memref.load __selfbuf_1018.managed.buffer : i64
    memref.store %1025, states.managed.buffer
    %1026 = memref.load __selfbuf_1018.managed.length : i64
    memref.store %1026, states.managed.length
    %1027 = memref.load __selfbuf_1018.managed.capacity : i64
    memref.store %1027, states.managed.capacity
    %1028 = arith.constant {value = 1 : i64}
    %1029 = memref.load count : i64
    %1030 = arith.addi %1029, %1028
    memref.store %1030, count
    %1031 = memref.load __self_ptr : i64
    memref.store_indirect %1030, %1031+16
    cf.br fallback_16.merge
  fallback_16.merge:
    func.return
  }
  func @__Set_3_i64.count(__self_ptr: i64) -> i64 {
  entry:
    %1032 = func.param __self_ptr : StdI64
    memref.store %1032, __self_ptr
    %1033 = arith.constant {value = 0 : i64}
    %1034 = arith.addi %1032, %1033
    %1035 = memref.load_indirect %1034+0
    memref.store %1035, self.elements.iterIndex
    %1036 = arith.constant {value = 8 : i64}
    %1037 = arith.addi %1034, %1036
    %1038 = memref.load_indirect %1037+0
    memref.store %1038, self.elements.managed.buffer
    %1039 = memref.load_indirect %1037+8
    memref.store %1039, self.elements.managed.length
    %1040 = memref.load_indirect %1037+16
    memref.store %1040, self.elements.managed.capacity
    %1041 = arith.constant {value = 8 : i64}
    %1042 = arith.addi %1032, %1041
    %1043 = memref.load_indirect %1042+0
    memref.store %1043, self.states.iterIndex
    %1044 = arith.constant {value = 8 : i64}
    %1045 = arith.addi %1042, %1044
    %1046 = memref.load_indirect %1045+0
    memref.store %1046, self.states.managed.buffer
    %1047 = memref.load_indirect %1045+8
    memref.store %1047, self.states.managed.length
    %1048 = memref.load_indirect %1045+16
    memref.store %1048, self.states.managed.capacity
    %1049 = memref.load_indirect %1032+16
    memref.store %1049, self.count
    %1050 = memref.load_indirect %1032+24
    memref.store %1050, self.capacity
    %1051 = memref.load_indirect %1032+32
    memref.store %1051, self.iterIndex
    %1052 = memref.load self.count : i64
    %1053 = memref.load self.capacity : i64
    %1054 = memref.load self.iterIndex : i64
    func.return %1052
  }
  func @__Set_3_i64.grow(__self_ptr: i64) {
  entry:
    %1055 = func.param __self_ptr : StdI64
    memref.store %1055, __self_ptr
    %1056 = arith.constant {value = 0 : i64}
    %1057 = arith.addi %1055, %1056
    %1058 = memref.load_indirect %1057+0
    memref.store %1058, self.elements.iterIndex
    %1059 = arith.constant {value = 8 : i64}
    %1060 = arith.addi %1057, %1059
    %1061 = memref.load_indirect %1060+0
    memref.store %1061, self.elements.managed.buffer
    %1062 = memref.load_indirect %1060+8
    memref.store %1062, self.elements.managed.length
    %1063 = memref.load_indirect %1060+16
    memref.store %1063, self.elements.managed.capacity
    %1064 = arith.constant {value = 8 : i64}
    %1065 = arith.addi %1055, %1064
    %1066 = memref.load_indirect %1065+0
    memref.store %1066, self.states.iterIndex
    %1067 = arith.constant {value = 8 : i64}
    %1068 = arith.addi %1065, %1067
    %1069 = memref.load_indirect %1068+0
    memref.store %1069, self.states.managed.buffer
    %1070 = memref.load_indirect %1068+8
    memref.store %1070, self.states.managed.length
    %1071 = memref.load_indirect %1068+16
    memref.store %1071, self.states.managed.capacity
    %1072 = memref.load_indirect %1055+16
    memref.store %1072, self.count
    %1073 = memref.load_indirect %1055+24
    memref.store %1073, self.capacity
    %1074 = memref.load_indirect %1055+32
    memref.store %1074, self.iterIndex
    %1075 = memref.load self.count : i64
    %1076 = memref.load self.capacity : i64
    %1077 = memref.load self.iterIndex : i64
    memref.store %1076, oldCapacity
    %1078 = arith.constant {value = 2 : i64}
    %1079 = arith.muli %1076, %1078
    memref.store %1079, newCapacity
    %1080 = arith.constant {value = 0 : i64}
    %1081 = arith.cmpi eq %1079, %1080
    cf.cond_br %1081 [then: handle_zero_0, else: handle_zero_0.merge]
  handle_zero_0:
    %1082 = arith.constant {value = 16 : i64}
    memref.store %1082, newCapacity
    cf.br handle_zero_0.merge
  handle_zero_0.merge:
    %1083 = memref.load self.elements.iterIndex : i64
    memref.store %1083, oldElements.iterIndex
    %1084 = memref.load self.elements.managed.buffer : i64
    memref.store %1084, oldElements.managed.buffer
    %1085 = memref.load self.elements.managed.length : i64
    memref.store %1085, oldElements.managed.length
    %1086 = memref.load self.elements.managed.capacity : i64
    memref.store %1086, oldElements.managed.capacity
    %1087 = memref.load self.states.iterIndex : i64
    memref.store %1087, oldStates.iterIndex
    %1088 = memref.load self.states.managed.buffer : i64
    memref.store %1088, oldStates.managed.buffer
    %1089 = memref.load self.states.managed.length : i64
    memref.store %1089, oldStates.managed.length
    %1090 = memref.load self.states.managed.capacity : i64
    memref.store %1090, oldStates.managed.capacity
    %1091 = arith.constant {value = 0 : i64}
    %1092 = arith.constant {value = 0 : i64}
    %1093 = arith.constant {value = 0 : i64}
    %1094 = arith.constant {value = 0 : i64}
    memref.store %1092, __struct_277.buffer
    memref.store %1093, __struct_277.length
    memref.store %1094, __struct_277.capacity
    memref.store %1091, __struct_278.iterIndex
    %1095 = memref.load __struct_277.buffer : i64
    memref.store %1095, __struct_278.managed.buffer
    %1096 = memref.load __struct_277.length : i64
    memref.store %1096, __struct_278.managed.length
    %1097 = memref.load __struct_277.capacity : i64
    memref.store %1097, __struct_278.managed.capacity
    %1098 = memref.load __struct_278.iterIndex : i64
    memref.store %1098, newElements.iterIndex
    %1099 = memref.load __struct_278.managed.buffer : i64
    memref.store %1099, newElements.managed.buffer
    %1100 = memref.load __struct_278.managed.length : i64
    memref.store %1100, newElements.managed.length
    %1101 = memref.load __struct_278.managed.capacity : i64
    memref.store %1101, newElements.managed.capacity
    %1102 = memref.load newCapacity : i64
    %1104 = memref.load newElements.managed.capacity : i64
    memref.store %1104, __selfbuf_1103.managed.capacity
    %1105 = memref.load newElements.managed.length : i64
    memref.store %1105, __selfbuf_1103.managed.length
    %1106 = memref.load newElements.managed.buffer : i64
    memref.store %1106, __selfbuf_1103.managed.buffer
    %1107 = memref.load newElements.iterIndex : i64
    memref.store %1107, __selfbuf_1103.iterIndex
    %1108 = memref.lea __selfbuf_1103
    func.call @Array.resize %1108, %1102
    %1109 = memref.load __selfbuf_1103.iterIndex : i64
    memref.store %1109, newElements.iterIndex
    %1110 = memref.load __selfbuf_1103.managed.buffer : i64
    memref.store %1110, newElements.managed.buffer
    %1111 = memref.load __selfbuf_1103.managed.length : i64
    memref.store %1111, newElements.managed.length
    %1112 = memref.load __selfbuf_1103.managed.capacity : i64
    memref.store %1112, newElements.managed.capacity
    %1113 = arith.constant {value = 0 : i64}
    %1114 = arith.constant {value = 0 : i64}
    %1115 = arith.constant {value = 0 : i64}
    %1116 = arith.constant {value = 0 : i64}
    memref.store %1114, __struct_284.buffer
    memref.store %1115, __struct_284.length
    memref.store %1116, __struct_284.capacity
    memref.store %1113, __struct_285.iterIndex
    %1117 = memref.load __struct_284.buffer : i64
    memref.store %1117, __struct_285.managed.buffer
    %1118 = memref.load __struct_284.length : i64
    memref.store %1118, __struct_285.managed.length
    %1119 = memref.load __struct_284.capacity : i64
    memref.store %1119, __struct_285.managed.capacity
    %1120 = memref.load __struct_285.iterIndex : i64
    memref.store %1120, newStates.iterIndex
    %1121 = memref.load __struct_285.managed.buffer : i64
    memref.store %1121, newStates.managed.buffer
    %1122 = memref.load __struct_285.managed.length : i64
    memref.store %1122, newStates.managed.length
    %1123 = memref.load __struct_285.managed.capacity : i64
    memref.store %1123, newStates.managed.capacity
    %1124 = memref.load newCapacity : i64
    %1126 = memref.load newStates.managed.capacity : i64
    memref.store %1126, __selfbuf_1125.managed.capacity
    %1127 = memref.load newStates.managed.length : i64
    memref.store %1127, __selfbuf_1125.managed.length
    %1128 = memref.load newStates.managed.buffer : i64
    memref.store %1128, __selfbuf_1125.managed.buffer
    %1129 = memref.load newStates.iterIndex : i64
    memref.store %1129, __selfbuf_1125.iterIndex
    %1130 = memref.lea __selfbuf_1125
    func.call @IntArray.resize %1130, %1124
    %1131 = memref.load __selfbuf_1125.iterIndex : i64
    memref.store %1131, newStates.iterIndex
    %1132 = memref.load __selfbuf_1125.managed.buffer : i64
    memref.store %1132, newStates.managed.buffer
    %1133 = memref.load __selfbuf_1125.managed.length : i64
    memref.store %1133, newStates.managed.length
    %1134 = memref.load __selfbuf_1125.managed.capacity : i64
    memref.store %1134, newStates.managed.capacity
    %1135 = memref.load newElements.iterIndex : i64
    memref.store %1135, elements.iterIndex
    %1136 = memref.load newElements.managed.buffer : i64
    memref.store %1136, elements.managed.buffer
    %1137 = memref.load newElements.managed.length : i64
    memref.store %1137, elements.managed.length
    %1138 = memref.load newElements.managed.capacity : i64
    memref.store %1138, elements.managed.capacity
    %1139 = memref.load newStates.iterIndex : i64
    memref.store %1139, states.iterIndex
    %1140 = memref.load newStates.managed.buffer : i64
    memref.store %1140, states.managed.buffer
    %1141 = memref.load newStates.managed.length : i64
    memref.store %1141, states.managed.length
    %1142 = memref.load newStates.managed.capacity : i64
    memref.store %1142, states.managed.capacity
    %1143 = memref.load newCapacity : i64
    memref.store %1143, capacity
    %1144 = memref.load __self_ptr : i64
    memref.store_indirect %1143, %1144+24
    %1145 = arith.constant {value = 0 : i64}
    memref.store %1145, count
    %1146 = memref.load __self_ptr : i64
    memref.store_indirect %1145, %1146+16
    %1147 = arith.constant {value = 0 : i64}
    memref.store %1147, i
    cf.br rehash_1.header
  rehash_1.header:
    %1148 = memref.load i : i64
    %1149 = memref.load oldCapacity : i64
    %1150 = arith.cmpi lt %1148, %1149
    cf.cond_br %1150 [then: rehash_1, else: rehash_1.exit]
  rehash_1:
    %1151 = memref.load i : i64
    %1153 = memref.load oldStates.managed.capacity : i64
    memref.store %1153, __selfbuf_1152.managed.capacity
    %1154 = memref.load oldStates.managed.length : i64
    memref.store %1154, __selfbuf_1152.managed.length
    %1155 = memref.load oldStates.managed.buffer : i64
    memref.store %1155, __selfbuf_1152.managed.buffer
    %1156 = memref.load oldStates.iterIndex : i64
    memref.store %1156, __selfbuf_1152.iterIndex
    %1157 = memref.lea __selfbuf_1152
    %1158, %1159 = func.try_call @IntArray.get %1157, %1151
    memref.store %1159, __error_flag
    %1160 = memref.load __selfbuf_1152.iterIndex : i64
    memref.store %1160, oldStates.iterIndex
    %1161 = memref.load __selfbuf_1152.managed.buffer : i64
    memref.store %1161, oldStates.managed.buffer
    %1162 = memref.load __selfbuf_1152.managed.length : i64
    memref.store %1162, oldStates.managed.length
    %1163 = memref.load __selfbuf_1152.managed.capacity : i64
    memref.store %1163, oldStates.managed.capacity
    %1164 = arith.constant {value = 0 : i64}
    memref.store %1164, __try_default_3
    memref.store %1158, __try_result_2
    %1165 = arith.constant {value = 0 : i64}
    %1166 = arith.cmpi ne %1159, %1165
    cf.cond_br %1166 [then: otherwise_default_error_4, else: otherwise_default_continue_5]
  otherwise_default_error_4:
    %1167 = memref.load __try_default_3 : i64
    memref.store %1167, __try_result_2
    cf.br otherwise_default_continue_5
  otherwise_default_continue_5:
    %1168 = memref.load __try_result_2 : i64
    memref.store %1168, state
    %1169 = arith.constant {value = 1 : i64}
    %1170 = arith.cmpi eq %1168, %1169
    cf.cond_br %1170 [then: occupied_6, else: occupied_6.after]
  occupied_6:
    %1171 = memref.load i : i64
    %1173 = memref.load oldElements.managed.capacity : i64
    memref.store %1173, __selfbuf_1172.managed.capacity
    %1174 = memref.load oldElements.managed.length : i64
    memref.store %1174, __selfbuf_1172.managed.length
    %1175 = memref.load oldElements.managed.buffer : i64
    memref.store %1175, __selfbuf_1172.managed.buffer
    %1176 = memref.load oldElements.iterIndex : i64
    memref.store %1176, __selfbuf_1172.iterIndex
    %1177 = memref.lea __selfbuf_1172
    %1178, %1179 = func.try_call @Array.get %1177, %1171
    memref.store %1179, __error_flag
    %1180 = memref.load __selfbuf_1172.iterIndex : i64
    memref.store %1180, oldElements.iterIndex
    %1181 = memref.load __selfbuf_1172.managed.buffer : i64
    memref.store %1181, oldElements.managed.buffer
    %1182 = memref.load __selfbuf_1172.managed.length : i64
    memref.store %1182, oldElements.managed.length
    %1183 = memref.load __selfbuf_1172.managed.capacity : i64
    memref.store %1183, oldElements.managed.capacity
    memref.store %1179, __try_error_7
    memref.store %1178, __try_result_8
    %1184 = arith.constant {value = 0 : i64}
    %1185 = arith.cmpi eq %1179, %1184
    cf.cond_br %1185 [then: get_elem_9, else: get_elem_9.after]
  get_elem_9:
    %1186 = memref.load __try_result_8 : i64
    memref.store %1186, element
    memref.store %1186, hash
    %1187 = memref.load newCapacity : i64
    %1188 = arith.remsi %1186, %1187
    memref.store %1188, index
    %1189 = arith.constant {value = 0 : i64}
    %1190 = arith.cmpi lt %1188, %1189
    cf.cond_br %1190 [then: fix_negative_10, else: fix_negative_10.merge]
  fix_negative_10:
    %1191 = memref.load index : i64
    %1192 = memref.load newCapacity : i64
    %1193 = arith.addi %1191, %1192
    memref.store %1193, index
    cf.br fix_negative_10.merge
  fix_negative_10.merge:
    %1194 = memref.load index : i64
    %1196 = memref.load states.managed.capacity : i64
    memref.store %1196, __selfbuf_1195.managed.capacity
    %1197 = memref.load states.managed.length : i64
    memref.store %1197, __selfbuf_1195.managed.length
    %1198 = memref.load states.managed.buffer : i64
    memref.store %1198, __selfbuf_1195.managed.buffer
    %1199 = memref.load states.iterIndex : i64
    memref.store %1199, __selfbuf_1195.iterIndex
    %1200 = memref.lea __selfbuf_1195
    %1201, %1202 = func.try_call @IntArray.get %1200, %1194
    memref.store %1202, __error_flag
    %1203 = memref.load __selfbuf_1195.iterIndex : i64
    memref.store %1203, states.iterIndex
    %1204 = memref.load __selfbuf_1195.managed.buffer : i64
    memref.store %1204, states.managed.buffer
    %1205 = memref.load __selfbuf_1195.managed.length : i64
    memref.store %1205, states.managed.length
    %1206 = memref.load __selfbuf_1195.managed.capacity : i64
    memref.store %1206, states.managed.capacity
    %1207 = arith.constant {value = 0 : i64}
    memref.store %1207, __try_default_12
    memref.store %1201, __try_result_11
    %1208 = arith.constant {value = 0 : i64}
    %1209 = arith.cmpi ne %1202, %1208
    cf.cond_br %1209 [then: otherwise_default_error_13, else: otherwise_default_continue_14]
  otherwise_default_error_13:
    %1210 = memref.load __try_default_12 : i64
    memref.store %1210, __try_result_11
    cf.br otherwise_default_continue_14
  otherwise_default_continue_14:
    %1211 = memref.load __try_result_11 : i64
    memref.store %1211, currentState
    cf.br find_slot_15.header
  find_slot_15.header:
    %1212 = arith.constant {value = 0 : i64}
    %1213 = memref.load currentState : i64
    %1214 = arith.cmpi ne %1213, %1212
    cf.cond_br %1214 [then: find_slot_15, else: find_slot_15.exit]
  find_slot_15:
    %1215 = arith.constant {value = 1 : i64}
    %1216 = memref.load index : i64
    %1217 = arith.addi %1216, %1215
    %1218 = memref.load newCapacity : i64
    %1219 = arith.remsi %1217, %1218
    memref.store %1219, index
    %1221 = memref.load states.managed.capacity : i64
    memref.store %1221, __selfbuf_1220.managed.capacity
    %1222 = memref.load states.managed.length : i64
    memref.store %1222, __selfbuf_1220.managed.length
    %1223 = memref.load states.managed.buffer : i64
    memref.store %1223, __selfbuf_1220.managed.buffer
    %1224 = memref.load states.iterIndex : i64
    memref.store %1224, __selfbuf_1220.iterIndex
    %1225 = memref.lea __selfbuf_1220
    %1226, %1227 = func.try_call @IntArray.get %1225, %1219
    memref.store %1227, __error_flag
    %1228 = memref.load __selfbuf_1220.iterIndex : i64
    memref.store %1228, states.iterIndex
    %1229 = memref.load __selfbuf_1220.managed.buffer : i64
    memref.store %1229, states.managed.buffer
    %1230 = memref.load __selfbuf_1220.managed.length : i64
    memref.store %1230, states.managed.length
    %1231 = memref.load __selfbuf_1220.managed.capacity : i64
    memref.store %1231, states.managed.capacity
    %1232 = arith.constant {value = 0 : i64}
    memref.store %1232, __try_default_17
    memref.store %1226, __try_result_16
    %1233 = arith.constant {value = 0 : i64}
    %1234 = arith.cmpi ne %1227, %1233
    cf.cond_br %1234 [then: otherwise_default_error_18, else: otherwise_default_continue_19]
  otherwise_default_error_18:
    %1235 = memref.load __try_default_17 : i64
    memref.store %1235, __try_result_16
    cf.br otherwise_default_continue_19
  otherwise_default_continue_19:
    %1236 = memref.load __try_result_16 : i64
    memref.store %1236, currentState
    cf.br find_slot_15.header
  find_slot_15.exit:
    %1237 = memref.load index : i64
    %1238 = memref.load element : i64
    %1240 = memref.load elements.managed.capacity : i64
    memref.store %1240, __selfbuf_1239.managed.capacity
    %1241 = memref.load elements.managed.length : i64
    memref.store %1241, __selfbuf_1239.managed.length
    %1242 = memref.load elements.managed.buffer : i64
    memref.store %1242, __selfbuf_1239.managed.buffer
    %1243 = memref.load elements.iterIndex : i64
    memref.store %1243, __selfbuf_1239.iterIndex
    %1244 = memref.lea __selfbuf_1239
    func.call @Array.set %1244, %1237, %1238
    %1245 = memref.load __selfbuf_1239.iterIndex : i64
    memref.store %1245, elements.iterIndex
    %1246 = memref.load __selfbuf_1239.managed.buffer : i64
    memref.store %1246, elements.managed.buffer
    %1247 = memref.load __selfbuf_1239.managed.length : i64
    memref.store %1247, elements.managed.length
    %1248 = memref.load __selfbuf_1239.managed.capacity : i64
    memref.store %1248, elements.managed.capacity
    %1249 = memref.load index : i64
    %1250 = arith.constant {value = 1 : i64}
    %1252 = memref.load states.managed.capacity : i64
    memref.store %1252, __selfbuf_1251.managed.capacity
    %1253 = memref.load states.managed.length : i64
    memref.store %1253, __selfbuf_1251.managed.length
    %1254 = memref.load states.managed.buffer : i64
    memref.store %1254, __selfbuf_1251.managed.buffer
    %1255 = memref.load states.iterIndex : i64
    memref.store %1255, __selfbuf_1251.iterIndex
    %1256 = memref.lea __selfbuf_1251
    func.call @IntArray.set %1256, %1249, %1250
    %1257 = memref.load __selfbuf_1251.iterIndex : i64
    memref.store %1257, states.iterIndex
    %1258 = memref.load __selfbuf_1251.managed.buffer : i64
    memref.store %1258, states.managed.buffer
    %1259 = memref.load __selfbuf_1251.managed.length : i64
    memref.store %1259, states.managed.length
    %1260 = memref.load __selfbuf_1251.managed.capacity : i64
    memref.store %1260, states.managed.capacity
    %1261 = arith.constant {value = 1 : i64}
    %1262 = memref.load count : i64
    %1263 = arith.addi %1262, %1261
    memref.store %1263, count
    %1264 = memref.load __self_ptr : i64
    memref.store_indirect %1263, %1264+16
  get_elem_9.after:
  occupied_6.after:
    %1265 = arith.constant {value = 1 : i64}
    %1266 = memref.load i : i64
    %1267 = arith.addi %1266, %1265
    memref.store %1267, i
    cf.br rehash_1.header
  rehash_1.exit:
    func.return
  }
}
