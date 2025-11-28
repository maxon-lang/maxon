; ModuleID = 'fannkuch-redux.maxon'
source_filename = "fannkuch-redux.maxon"
target triple = "x86_64-pc-windows-msvc"

; Function Attrs: nounwind memory(readwrite, argmem: none)
define range(i32 0, -2147483648) i32 @main() local_unnamed_addr #0 {
loop.preheader:
  %p.malloc = tail call dereferenceable_or_null(64) ptr @malloc(i64 64)
  %0 = tail call ptr @memset(ptr %p.malloc, i32 0, i64 64)
  %count.malloc = tail call dereferenceable_or_null(64) ptr @malloc(i64 64)
  %1 = tail call ptr @memset(ptr %count.malloc, i32 0, i64 64)
  store i32 0, ptr %p.malloc, align 4
  %arrayidx.1.i = getelementptr inbounds nuw i8, ptr %p.malloc, i64 4
  %arrayidx.2.i = getelementptr inbounds nuw i8, ptr %p.malloc, i64 8
  %arrayidx.3.i = getelementptr inbounds nuw i8, ptr %p.malloc, i64 12
  %arrayidx.4.i = getelementptr inbounds nuw i8, ptr %p.malloc, i64 16
  %arrayidx.5.i = getelementptr inbounds nuw i8, ptr %p.malloc, i64 20
  %arrayidx.6.i = getelementptr inbounds nuw i8, ptr %p.malloc, i64 24
  %arrayidx.7.i = getelementptr inbounds nuw i8, ptr %p.malloc, i64 28
  %arrayidx.8.i = getelementptr inbounds nuw i8, ptr %p.malloc, i64 32
  %arrayidx.9.i = getelementptr inbounds nuw i8, ptr %p.malloc, i64 36
  %arrayidx.10.i = getelementptr inbounds nuw i8, ptr %p.malloc, i64 40
  %pp.malloc.i = tail call dereferenceable_or_null(64) ptr @malloc(i64 64)
  %2 = tail call ptr @memset(ptr %pp.malloc.i, i32 0, i64 64)
  %arrayidx.1.i.i = getelementptr inbounds nuw i8, ptr %pp.malloc.i, i64 4
  %arrayidx.2.i.i = getelementptr inbounds nuw i8, ptr %pp.malloc.i, i64 8
  %arrayidx.3.i.i = getelementptr inbounds nuw i8, ptr %pp.malloc.i, i64 12
  %arrayidx.4.i.i = getelementptr inbounds nuw i8, ptr %pp.malloc.i, i64 16
  %arrayidx.5.i.i = getelementptr inbounds nuw i8, ptr %pp.malloc.i, i64 20
  %arrayidx.6.i.i = getelementptr inbounds nuw i8, ptr %pp.malloc.i, i64 24
  %arrayidx.7.i.i = getelementptr inbounds nuw i8, ptr %pp.malloc.i, i64 28
  %arrayidx.8.i.i = getelementptr inbounds nuw i8, ptr %pp.malloc.i, i64 32
  %arrayidx.9.i.i = getelementptr inbounds nuw i8, ptr %pp.malloc.i, i64 36
  %arrayidx.10.i.i = getelementptr inbounds nuw i8, ptr %pp.malloc.i, i64 40
  %arrayidx15.i = getelementptr inbounds nuw i8, ptr %count.malloc, i64 40
  store i32 0, ptr %arrayidx15.i, align 4
  store i32 0, ptr %pp.malloc.i, align 4
  store i32 1, ptr %arrayidx.1.i.i, align 4
  store i32 2, ptr %arrayidx.2.i.i, align 4
  store i32 3, ptr %arrayidx.3.i.i, align 4
  store i32 4, ptr %arrayidx.4.i.i, align 4
  store i32 5, ptr %arrayidx.5.i.i, align 4
  store i32 6, ptr %arrayidx.6.i.i, align 4
  store i32 7, ptr %arrayidx.7.i.i, align 4
  store i32 8, ptr %arrayidx.8.i.i, align 4
  store i32 9, ptr %arrayidx.9.i.i, align 4
  store i32 10, ptr %arrayidx.10.i.i, align 4
  tail call void @llvm.memcpy.p0.p0.i64(ptr noundef nonnull align 4 dereferenceable(44) %p.malloc, ptr noundef nonnull align 4 dereferenceable(44) %pp.malloc.i, i64 44, i1 false)
  %arrayidx15.1.i = getelementptr inbounds nuw i8, ptr %count.malloc, i64 36
  store i32 0, ptr %arrayidx15.1.i, align 4
  %arrayelem.i.1.i = load i32, ptr %p.malloc, align 4
  store i32 %arrayelem.i.1.i, ptr %pp.malloc.i, align 4
  %arrayelem.1.i.1.i = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem.1.i.1.i, ptr %arrayidx.1.i.i, align 4
  %arrayelem.2.i.1.i = load i32, ptr %arrayidx.2.i, align 4
  store i32 %arrayelem.2.i.1.i, ptr %arrayidx.2.i.i, align 4
  %arrayelem.3.i.1.i = load i32, ptr %arrayidx.3.i, align 4
  store i32 %arrayelem.3.i.1.i, ptr %arrayidx.3.i.i, align 4
  %arrayelem.4.i.1.i = load i32, ptr %arrayidx.4.i, align 4
  store i32 %arrayelem.4.i.1.i, ptr %arrayidx.4.i.i, align 4
  %arrayelem.5.i.1.i = load i32, ptr %arrayidx.5.i, align 4
  store i32 %arrayelem.5.i.1.i, ptr %arrayidx.5.i.i, align 4
  %arrayelem.6.i.1.i = load i32, ptr %arrayidx.6.i, align 4
  store i32 %arrayelem.6.i.1.i, ptr %arrayidx.6.i.i, align 4
  %arrayelem.7.i.1.i = load i32, ptr %arrayidx.7.i, align 4
  store i32 %arrayelem.7.i.1.i, ptr %arrayidx.7.i.i, align 4
  %arrayelem.8.i.1.i = load i32, ptr %arrayidx.8.i, align 4
  store i32 %arrayelem.8.i.1.i, ptr %arrayidx.8.i.i, align 4
  %arrayelem.9.i.1.i = load i32, ptr %arrayidx.9.i, align 4
  store i32 %arrayelem.9.i.1.i, ptr %arrayidx.9.i.i, align 4
  tail call void @llvm.memcpy.p0.p0.i64(ptr noundef nonnull align 4 dereferenceable(40) %p.malloc, ptr noundef nonnull align 4 dereferenceable(40) %pp.malloc.i, i64 40, i1 false)
  %arrayidx15.2.i = getelementptr inbounds nuw i8, ptr %count.malloc, i64 32
  store i32 0, ptr %arrayidx15.2.i, align 4
  %arrayelem.i.2.i = load i32, ptr %p.malloc, align 4
  store i32 %arrayelem.i.2.i, ptr %pp.malloc.i, align 4
  %arrayelem.1.i.2.i = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem.1.i.2.i, ptr %arrayidx.1.i.i, align 4
  %arrayelem.2.i.2.i = load i32, ptr %arrayidx.2.i, align 4
  store i32 %arrayelem.2.i.2.i, ptr %arrayidx.2.i.i, align 4
  %arrayelem.3.i.2.i = load i32, ptr %arrayidx.3.i, align 4
  store i32 %arrayelem.3.i.2.i, ptr %arrayidx.3.i.i, align 4
  %arrayelem.4.i.2.i = load i32, ptr %arrayidx.4.i, align 4
  store i32 %arrayelem.4.i.2.i, ptr %arrayidx.4.i.i, align 4
  %arrayelem.5.i.2.i = load i32, ptr %arrayidx.5.i, align 4
  store i32 %arrayelem.5.i.2.i, ptr %arrayidx.5.i.i, align 4
  %arrayelem.6.i.2.i = load i32, ptr %arrayidx.6.i, align 4
  store i32 %arrayelem.6.i.2.i, ptr %arrayidx.6.i.i, align 4
  %arrayelem.7.i.2.i = load i32, ptr %arrayidx.7.i, align 4
  store i32 %arrayelem.7.i.2.i, ptr %arrayidx.7.i.i, align 4
  %arrayelem.8.i.2.i = load i32, ptr %arrayidx.8.i, align 4
  store i32 %arrayelem.8.i.2.i, ptr %arrayidx.8.i.i, align 4
  tail call void @llvm.memcpy.p0.p0.i64(ptr noundef nonnull align 4 dereferenceable(36) %p.malloc, ptr noundef nonnull align 4 dereferenceable(36) %pp.malloc.i, i64 36, i1 false)
  %arrayidx15.3.i = getelementptr inbounds nuw i8, ptr %count.malloc, i64 28
  store i32 0, ptr %arrayidx15.3.i, align 4
  %arrayelem.i.3.i = load i32, ptr %p.malloc, align 4
  store i32 %arrayelem.i.3.i, ptr %pp.malloc.i, align 4
  %arrayelem.1.i.3.i = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem.1.i.3.i, ptr %arrayidx.1.i.i, align 4
  %arrayelem.2.i.3.i = load i32, ptr %arrayidx.2.i, align 4
  store i32 %arrayelem.2.i.3.i, ptr %arrayidx.2.i.i, align 4
  %arrayelem.3.i.3.i = load i32, ptr %arrayidx.3.i, align 4
  store i32 %arrayelem.3.i.3.i, ptr %arrayidx.3.i.i, align 4
  %arrayelem.4.i.3.i = load i32, ptr %arrayidx.4.i, align 4
  store i32 %arrayelem.4.i.3.i, ptr %arrayidx.4.i.i, align 4
  %arrayelem.5.i.3.i = load i32, ptr %arrayidx.5.i, align 4
  store i32 %arrayelem.5.i.3.i, ptr %arrayidx.5.i.i, align 4
  %arrayelem.6.i.3.i = load i32, ptr %arrayidx.6.i, align 4
  store i32 %arrayelem.6.i.3.i, ptr %arrayidx.6.i.i, align 4
  %arrayelem.7.i.3.i = load i32, ptr %arrayidx.7.i, align 4
  store i32 %arrayelem.7.i.3.i, ptr %arrayidx.7.i.i, align 4
  tail call void @llvm.memcpy.p0.p0.i64(ptr noundef nonnull align 4 dereferenceable(32) %p.malloc, ptr noundef nonnull align 4 dereferenceable(32) %pp.malloc.i, i64 32, i1 false)
  %arrayidx15.4.i = getelementptr inbounds nuw i8, ptr %count.malloc, i64 24
  store i32 0, ptr %arrayidx15.4.i, align 4
  %arrayelem.i.4.i = load i32, ptr %p.malloc, align 4
  store i32 %arrayelem.i.4.i, ptr %pp.malloc.i, align 4
  %arrayelem.1.i.4.i = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem.1.i.4.i, ptr %arrayidx.1.i.i, align 4
  %arrayelem.2.i.4.i = load i32, ptr %arrayidx.2.i, align 4
  store i32 %arrayelem.2.i.4.i, ptr %arrayidx.2.i.i, align 4
  %arrayelem.3.i.4.i = load i32, ptr %arrayidx.3.i, align 4
  store i32 %arrayelem.3.i.4.i, ptr %arrayidx.3.i.i, align 4
  %arrayelem.4.i.4.i = load i32, ptr %arrayidx.4.i, align 4
  store i32 %arrayelem.4.i.4.i, ptr %arrayidx.4.i.i, align 4
  %arrayelem.5.i.4.i = load i32, ptr %arrayidx.5.i, align 4
  store i32 %arrayelem.5.i.4.i, ptr %arrayidx.5.i.i, align 4
  %arrayelem.6.i.4.i = load i32, ptr %arrayidx.6.i, align 4
  store i32 %arrayelem.6.i.4.i, ptr %arrayidx.6.i.i, align 4
  tail call void @llvm.memcpy.p0.p0.i64(ptr noundef nonnull align 4 dereferenceable(28) %p.malloc, ptr noundef nonnull align 4 dereferenceable(28) %pp.malloc.i, i64 28, i1 false)
  %arrayidx15.5.i = getelementptr inbounds nuw i8, ptr %count.malloc, i64 20
  store i32 0, ptr %arrayidx15.5.i, align 4
  %arrayelem.i.5.i = load i32, ptr %p.malloc, align 4
  store i32 %arrayelem.i.5.i, ptr %pp.malloc.i, align 4
  %arrayelem.1.i.5.i = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem.1.i.5.i, ptr %arrayidx.1.i.i, align 4
  %arrayelem.2.i.5.i = load i32, ptr %arrayidx.2.i, align 4
  store i32 %arrayelem.2.i.5.i, ptr %arrayidx.2.i.i, align 4
  %arrayelem.3.i.5.i = load i32, ptr %arrayidx.3.i, align 4
  store i32 %arrayelem.3.i.5.i, ptr %arrayidx.3.i.i, align 4
  %arrayelem.4.i.5.i = load i32, ptr %arrayidx.4.i, align 4
  store i32 %arrayelem.4.i.5.i, ptr %arrayidx.4.i.i, align 4
  %arrayelem.5.i.5.i = load i32, ptr %arrayidx.5.i, align 4
  store i32 %arrayelem.5.i.5.i, ptr %arrayidx.5.i.i, align 4
  tail call void @llvm.memcpy.p0.p0.i64(ptr noundef nonnull align 4 dereferenceable(24) %p.malloc, ptr noundef nonnull align 4 dereferenceable(24) %pp.malloc.i, i64 24, i1 false)
  %arrayidx15.6.i = getelementptr inbounds nuw i8, ptr %count.malloc, i64 16
  store i32 0, ptr %arrayidx15.6.i, align 4
  %arrayelem.i.6.i = load i32, ptr %p.malloc, align 4
  store i32 %arrayelem.i.6.i, ptr %pp.malloc.i, align 4
  %arrayelem.1.i.6.i = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem.1.i.6.i, ptr %arrayidx.1.i.i, align 4
  %arrayelem.2.i.6.i = load i32, ptr %arrayidx.2.i, align 4
  store i32 %arrayelem.2.i.6.i, ptr %arrayidx.2.i.i, align 4
  %arrayelem.3.i.6.i = load i32, ptr %arrayidx.3.i, align 4
  store i32 %arrayelem.3.i.6.i, ptr %arrayidx.3.i.i, align 4
  %arrayelem.4.i.6.i = load i32, ptr %arrayidx.4.i, align 4
  store i32 %arrayelem.4.i.6.i, ptr %arrayidx.4.i.i, align 4
  tail call void @llvm.memcpy.p0.p0.i64(ptr noundef nonnull align 4 dereferenceable(20) %p.malloc, ptr noundef nonnull align 4 dereferenceable(20) %pp.malloc.i, i64 20, i1 false)
  %arrayidx15.7.i = getelementptr inbounds nuw i8, ptr %count.malloc, i64 12
  store i32 0, ptr %arrayidx15.7.i, align 4
  %arrayelem.i.7.i = load i32, ptr %p.malloc, align 4
  store i32 %arrayelem.i.7.i, ptr %pp.malloc.i, align 4
  %arrayelem.1.i.7.i = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem.1.i.7.i, ptr %arrayidx.1.i.i, align 4
  %arrayelem.2.i.7.i = load i32, ptr %arrayidx.2.i, align 4
  store i32 %arrayelem.2.i.7.i, ptr %arrayidx.2.i.i, align 4
  %arrayelem.3.i.7.i = load i32, ptr %arrayidx.3.i, align 4
  store i32 %arrayelem.3.i.7.i, ptr %arrayidx.3.i.i, align 4
  tail call void @llvm.memcpy.p0.p0.i64(ptr noundef nonnull align 4 dereferenceable(16) %p.malloc, ptr noundef nonnull align 4 dereferenceable(16) %pp.malloc.i, i64 16, i1 false)
  %arrayidx15.8.i = getelementptr inbounds nuw i8, ptr %count.malloc, i64 8
  store i32 0, ptr %arrayidx15.8.i, align 4
  %arrayelem.i.8.i = load i32, ptr %p.malloc, align 4
  store i32 %arrayelem.i.8.i, ptr %pp.malloc.i, align 4
  %arrayelem.1.i.8.i = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem.1.i.8.i, ptr %arrayidx.1.i.i, align 4
  %arrayelem.2.i.8.i = load i32, ptr %arrayidx.2.i, align 4
  store i32 %arrayelem.2.i.8.i, ptr %arrayidx.2.i.i, align 4
  tail call void @llvm.memcpy.p0.p0.i64(ptr noundef nonnull align 4 dereferenceable(12) %p.malloc, ptr noundef nonnull align 4 dereferenceable(12) %pp.malloc.i, i64 12, i1 false)
  %arrayidx15.9.i = getelementptr inbounds nuw i8, ptr %count.malloc, i64 4
  store i32 0, ptr %arrayidx15.9.i, align 4
  %arrayelem.i.9.i = load i32, ptr %p.malloc, align 4
  store i32 %arrayelem.i.9.i, ptr %pp.malloc.i, align 4
  %arrayelem.1.i.9.i = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem.1.i.9.i, ptr %arrayidx.1.i.i, align 4
  %arrayelem.2.i.9.i = load i32, ptr %arrayidx.2.i, align 4
  store i32 %arrayelem.2.i.9.i, ptr %arrayidx.2.i.i, align 4
  %arrayelem.3.i.9.i = load i32, ptr %arrayidx.3.i, align 4
  store i32 %arrayelem.3.i.9.i, ptr %arrayidx.3.i.i, align 4
  %arrayelem.4.i.9.i = load i32, ptr %arrayidx.4.i, align 4
  store i32 %arrayelem.4.i.9.i, ptr %arrayidx.4.i.i, align 4
  %arrayelem.5.i.9.i = load i32, ptr %arrayidx.5.i, align 4
  store i32 %arrayelem.5.i.9.i, ptr %arrayidx.5.i.i, align 4
  %arrayelem.6.i.9.i = load i32, ptr %arrayidx.6.i, align 4
  store i32 %arrayelem.6.i.9.i, ptr %arrayidx.6.i.i, align 4
  %arrayelem.7.i.9.i = load i32, ptr %arrayidx.7.i, align 4
  store i32 %arrayelem.7.i.9.i, ptr %arrayidx.7.i.i, align 4
  %arrayelem.8.i.9.i = load i32, ptr %arrayidx.8.i, align 4
  store i32 %arrayelem.8.i.9.i, ptr %arrayidx.8.i.i, align 4
  %arrayelem.9.i.9.i = load i32, ptr %arrayidx.9.i, align 4
  store i32 %arrayelem.9.i.9.i, ptr %arrayidx.9.i.i, align 4
  %arrayelem.10.i.9.i = load i32, ptr %arrayidx.10.i, align 4
  store i32 %arrayelem.10.i.9.i, ptr %arrayidx.10.i.i, align 4
  %3 = load i64, ptr %pp.malloc.i, align 4
  store i64 %3, ptr %p.malloc, align 4
  tail call void @free(ptr %pp.malloc.i)
  br label %loop

loop:                                             ; preds = %ifcont49, %loop.preheader
  %idx.0107 = phi i32 [ %addtmp40, %ifcont49 ], [ 0, %loop.preheader ]
  %maxFlips.0105 = phi i32 [ %maxFlips.2, %ifcont49 ], [ 0, %loop.preheader ]
  %arrayelem19 = load i32, ptr %p.malloc, align 4
  %cmptmp20.not = icmp eq i32 %arrayelem19, 0
  br i1 %cmptmp20.not, label %ifcont38, label %ifcont.i

ifcont.i:                                         ; preds = %loop
  %pp.malloc.i73 = tail call dereferenceable_or_null(64) ptr @malloc(i64 64)
  %4 = tail call ptr @memset(ptr %pp.malloc.i73, i32 0, i64 64)
  store i32 %arrayelem19, ptr %pp.malloc.i73, align 4
  %arrayidx.1.i.i74 = getelementptr inbounds nuw i8, ptr %pp.malloc.i73, i64 4
  %arrayelem.1.i.i75 = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem.1.i.i75, ptr %arrayidx.1.i.i74, align 4
  %arrayidx.2.i.i76 = getelementptr inbounds nuw i8, ptr %pp.malloc.i73, i64 8
  %arrayelem.2.i.i77 = load i32, ptr %arrayidx.2.i, align 4
  store i32 %arrayelem.2.i.i77, ptr %arrayidx.2.i.i76, align 4
  %arrayidx.3.i.i78 = getelementptr inbounds nuw i8, ptr %pp.malloc.i73, i64 12
  %arrayelem.3.i.i79 = load i32, ptr %arrayidx.3.i, align 4
  store i32 %arrayelem.3.i.i79, ptr %arrayidx.3.i.i78, align 4
  %arrayidx.4.i.i80 = getelementptr inbounds nuw i8, ptr %pp.malloc.i73, i64 16
  %arrayelem.4.i.i81 = load i32, ptr %arrayidx.4.i, align 4
  store i32 %arrayelem.4.i.i81, ptr %arrayidx.4.i.i80, align 4
  %arrayidx.5.i.i82 = getelementptr inbounds nuw i8, ptr %pp.malloc.i73, i64 20
  %arrayelem.5.i.i83 = load i32, ptr %arrayidx.5.i, align 4
  store i32 %arrayelem.5.i.i83, ptr %arrayidx.5.i.i82, align 4
  %arrayidx.6.i.i84 = getelementptr inbounds nuw i8, ptr %pp.malloc.i73, i64 24
  %arrayelem.6.i.i85 = load i32, ptr %arrayidx.6.i, align 4
  store i32 %arrayelem.6.i.i85, ptr %arrayidx.6.i.i84, align 4
  %arrayidx.7.i.i86 = getelementptr inbounds nuw i8, ptr %pp.malloc.i73, i64 28
  %arrayelem.7.i.i87 = load i32, ptr %arrayidx.7.i, align 4
  store i32 %arrayelem.7.i.i87, ptr %arrayidx.7.i.i86, align 4
  %arrayidx.8.i.i88 = getelementptr inbounds nuw i8, ptr %pp.malloc.i73, i64 32
  %arrayelem.8.i.i89 = load i32, ptr %arrayidx.8.i, align 4
  store i32 %arrayelem.8.i.i89, ptr %arrayidx.8.i.i88, align 4
  %arrayidx.9.i.i90 = getelementptr inbounds nuw i8, ptr %pp.malloc.i73, i64 36
  %arrayelem.9.i.i91 = load i32, ptr %arrayidx.9.i, align 4
  store i32 %arrayelem.9.i.i91, ptr %arrayidx.9.i.i90, align 4
  %arrayidx.10.i.i92 = getelementptr inbounds nuw i8, ptr %pp.malloc.i73, i64 40
  %arrayelem.10.i.i93 = load i32, ptr %arrayidx.10.i, align 4
  store i32 %arrayelem.10.i.i93, ptr %arrayidx.10.i.i92, align 4
  %5 = sext i32 %arrayelem19 to i64
  %arrayidx970.i = getelementptr inbounds i32, ptr %pp.malloc.i73, i64 %5
  %arrayelem1071.i = load i32, ptr %arrayidx970.i, align 4
  %cmptmp11.not72.i = icmp eq i32 %arrayelem1071.i, 0
  br i1 %cmptmp11.not72.i, label %countFlips.exit, label %loop.i

loop.i:                                           ; preds = %ifcont.i, %ifcont47.i
  %arrayelem1076.i = phi i32 [ %arrayelem10.i, %ifcont47.i ], [ %arrayelem1071.i, %ifcont.i ]
  %arrayidx975.i = phi ptr [ %arrayidx9.i, %ifcont47.i ], [ %arrayidx970.i, %ifcont.i ]
  %firstValue.074.i = phi i32 [ %arrayelem1076.i, %ifcont47.i ], [ %arrayelem19, %ifcont.i ]
  %flips.073.i = phi i32 [ %addtmp50.i, %ifcont47.i ], [ 1, %ifcont.i ]
  store i32 %firstValue.074.i, ptr %arrayidx975.i, align 4
  %cmptmp2767.i = icmp sgt i32 %firstValue.074.i, 2
  br i1 %cmptmp2767.i, label %loop28.preheader.i, label %ifcont47.i

loop28.preheader.i:                               ; preds = %loop.i
  %j.066.i = add nsw i32 %firstValue.074.i, -1
  br label %loop28.i

loop28.i:                                         ; preds = %loop28.i, %loop28.preheader.i
  %j.069.i = phi i32 [ %j.0.i, %loop28.i ], [ %j.066.i, %loop28.preheader.i ]
  %i.068.i = phi i32 [ %addtmp.i95, %loop28.i ], [ 1, %loop28.preheader.i ]
  %6 = zext nneg i32 %i.068.i to i64
  %arrayidx31.i = getelementptr inbounds nuw i32, ptr %pp.malloc.i73, i64 %6
  %arrayelem32.i = load i32, ptr %arrayidx31.i, align 4
  %7 = sext i32 %j.069.i to i64
  %arrayidx38.i = getelementptr inbounds i32, ptr %pp.malloc.i73, i64 %7
  %arrayelem39.i = load i32, ptr %arrayidx38.i, align 4
  store i32 %arrayelem39.i, ptr %arrayidx31.i, align 4
  store i32 %arrayelem32.i, ptr %arrayidx38.i, align 4
  %addtmp.i95 = add nuw nsw i32 %i.068.i, 1
  %j.0.i = add nsw i32 %j.069.i, -1
  %cmptmp27.i = icmp slt i32 %addtmp.i95, %j.0.i
  br i1 %cmptmp27.i, label %loop28.i, label %ifcont47.i

ifcont47.i:                                       ; preds = %loop28.i, %loop.i
  %addtmp50.i = add i32 %flips.073.i, 1
  %8 = sext i32 %arrayelem1076.i to i64
  %arrayidx9.i = getelementptr inbounds i32, ptr %pp.malloc.i73, i64 %8
  %arrayelem10.i = load i32, ptr %arrayidx9.i, align 4
  %cmptmp11.not.i = icmp eq i32 %arrayelem10.i, 0
  br i1 %cmptmp11.not.i, label %countFlips.exit, label %loop.i

countFlips.exit:                                  ; preds = %ifcont47.i, %ifcont.i
  %common.ret.op.i94 = phi i32 [ 1, %ifcont.i ], [ %addtmp50.i, %ifcont47.i ]
  %spec.select = tail call i32 @llvm.smax.i32(i32 %common.ret.op.i94, i32 %maxFlips.0105)
  br label %ifcont38

ifcont38:                                         ; preds = %countFlips.exit, %loop
  %maxFlips.2 = phi i32 [ %maxFlips.0105, %loop ], [ %spec.select, %countFlips.exit ]
  %addtmp40 = add nuw nsw i32 %idx.0107, 1
  %cmptmp43 = icmp samesign ult i32 %idx.0107, 39916799
  br i1 %cmptmp43, label %then44, label %afterloop

then44:                                           ; preds = %ifcont38
  %arrayelem.i96 = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem19, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem.i96, ptr %p.malloc, align 4
  %arrayelem1276.i = load i32, ptr %arrayidx15.9.i, align 4
  %cmptmp.not77.i = icmp slt i32 %arrayelem1276.i, 1
  br i1 %cmptmp.not77.i, label %ifcont49, label %loop.i97

loop.i97:                                         ; preds = %then44, %afterfor.i
  %arrayidx1180.i = phi ptr [ %arrayidx11.i, %afterfor.i ], [ %arrayidx15.9.i, %then44 ]
  %i.079.i = phi i32 [ %addtmp.i98, %afterfor.i ], [ 1, %then44 ]
  %firstValue.078.i = phi i32 [ %arrayelem20.i, %afterfor.i ], [ %arrayelem.i96, %then44 ]
  store i32 0, ptr %arrayidx1180.i, align 4
  %addtmp.i98 = add i32 %i.079.i, 1
  %arrayelem20.i = load i32, ptr %arrayidx.1.i, align 4
  store i32 %arrayelem20.i, ptr %p.malloc, align 4
  %cmptmp3.i71.i = icmp slt i32 %addtmp.i98, 2
  br i1 %cmptmp3.i71.i, label %afterfor.i, label %forloop.preheader.i

forloop.preheader.i:                              ; preds = %loop.i97
  %9 = zext nneg i32 %i.079.i to i64
  %10 = shl nuw nsw i64 %9, 2
  tail call void @llvm.memmove.p0.p0.i64(ptr nonnull align 4 %arrayidx.1.i, ptr nonnull align 4 %arrayidx.2.i, i64 %10, i1 false)
  br label %afterfor.i

afterfor.i:                                       ; preds = %forloop.preheader.i, %loop.i97
  %11 = sext i32 %addtmp.i98 to i64
  %arrayidx37.i = getelementptr inbounds i32, ptr %p.malloc, i64 %11
  store i32 %firstValue.078.i, ptr %arrayidx37.i, align 4
  %arrayidx11.i = getelementptr inbounds i32, ptr %count.malloc, i64 %11
  %arrayelem12.i = load i32, ptr %arrayidx11.i, align 4
  %cmptmp.not.i = icmp slt i32 %arrayelem12.i, %addtmp.i98
  br i1 %cmptmp.not.i, label %ifcont49, label %loop.i97

ifcont49:                                         ; preds = %afterfor.i, %then44
  %arrayidx11.lcssa.i = phi ptr [ %arrayidx15.9.i, %then44 ], [ %arrayidx11.i, %afterfor.i ]
  %arrayelem12.lcssa.i = phi i32 [ %arrayelem1276.i, %then44 ], [ %arrayelem12.i, %afterfor.i ]
  %addtmp47.i = add nsw i32 %arrayelem12.lcssa.i, 1
  store i32 %addtmp47.i, ptr %arrayidx11.lcssa.i, align 4
  br label %loop

afterloop:                                        ; preds = %ifcont38
  tail call void @free(ptr %count.malloc)
  tail call void @free(ptr %p.malloc)
  ret i32 %maxFlips.2
}

; Function Attrs: mustprogress nofree nounwind willreturn allockind("alloc,uninitialized") allocsize(0) memory(inaccessiblemem: readwrite)
declare noalias noundef ptr @malloc(i64 noundef) local_unnamed_addr #1

; Function Attrs: mustprogress nounwind willreturn allockind("free") memory(argmem: readwrite, inaccessiblemem: readwrite)
declare void @free(ptr allocptr noundef captures(none)) local_unnamed_addr #2

; Function Attrs: mustprogress nocallback nofree nounwind willreturn memory(argmem: readwrite)
declare ptr @memset(ptr writeonly, i32, i64) local_unnamed_addr #3

; Function Attrs: nofree
declare void @exit(i32) local_unnamed_addr #4

; Function Attrs: noreturn
define void @_start() local_unnamed_addr #5 {
entry:
  %0 = tail call i32 @main()
  tail call void @exit(i32 %0)
  unreachable
}

; Function Attrs: nocallback nofree nounwind willreturn memory(argmem: readwrite)
declare void @llvm.memcpy.p0.p0.i64(ptr noalias writeonly captures(none), ptr noalias readonly captures(none), i64, i1 immarg) #6

; Function Attrs: nocallback nofree nounwind willreturn memory(argmem: readwrite)
declare void @llvm.memmove.p0.p0.i64(ptr writeonly captures(none), ptr readonly captures(none), i64, i1 immarg) #6

; Function Attrs: nocallback nofree nosync nounwind speculatable willreturn memory(none)
declare i32 @llvm.smax.i32(i32, i32) #7

attributes #0 = { nounwind memory(readwrite, argmem: none) "no-builtin-memset" "stackrealign" }
attributes #1 = { mustprogress nofree nounwind willreturn allockind("alloc,uninitialized") allocsize(0) memory(inaccessiblemem: readwrite) "alloc-family"="malloc" }
attributes #2 = { mustprogress nounwind willreturn allockind("free") memory(argmem: readwrite, inaccessiblemem: readwrite) "alloc-family"="malloc" }
attributes #3 = { mustprogress nocallback nofree nounwind willreturn memory(argmem: readwrite) }
attributes #4 = { nofree }
attributes #5 = { noreturn }
attributes #6 = { nocallback nofree nounwind willreturn memory(argmem: readwrite) }
attributes #7 = { nocallback nofree nosync nounwind speculatable willreturn memory(none) }
