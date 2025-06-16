using System.Globalization;
using System.Text;

internal static class Program
{
	private static void Main(string[] args)
	{
		StringBuilder sb = new();
		sb.Append("""
		// ThreadHelper helper
		.class private auto ansi abstract beforefieldinit hamarb123.ByRefHandles.ILHelpers.Helper extends [CORE_ASSEMBLY]System.Object
		{
			.method family hidebysig specialname rtspecialname instance void .ctor () cil managed
			{
				.maxstack 8
				ldarg.0
				call instance void class [CORE_ASSEMBLY]System.Object::.ctor()
				ret
			}

			.method public hidebysig newslot abstract virtual instance void ThreadHelper<valuetype T> (uint64* pooledAlloc, class hamarb123.ByRefHandles.ILHelpers.Helper`1<!!0> inst) cil managed
			{
			}

			.method public hidebysig newslot abstract virtual instance uint32 RequiredPooledAllocSize() cil managed
			{
			}
		}

		// CommandHelper helper
		.class private auto ansi abstract beforefieldinit hamarb123.ByRefHandles.ILHelpers.Helper`1<T> extends [CORE_ASSEMBLY]System.Object
		{
			.method family hidebysig specialname rtspecialname instance void .ctor () cil managed
			{
				.maxstack 8
				ldarg.0
				call instance void class [CORE_ASSEMBLY]System.Object::.ctor()
				ret
			}

			.method public hidebysig newslot abstract virtual instance uint8& Command (!0& state, [out] native uint& slot, valuetype [SPAN_ASSEMBLY]System.Span`1<uint64> usedSlots, uint16& usedSlotsFreeListSize, valuetype [SPAN_ASSEMBLY]System.Span`1<uint16> usedSlotsFreeList, valuetype [SPAN_ASSEMBLY]System.Span`1<native uint> pointerValues) cil managed
			{
			}

			.method public hidebysig newslot abstract virtual instance void InitState ([out] !0& state, valuetype [SPAN_ASSEMBLY]System.Span`1<uint64> usedSlots, uint16& usedSlotsFreeListSize, valuetype [SPAN_ASSEMBLY]System.Span`1<uint16> usedSlotsFreeList, valuetype [SPAN_ASSEMBLY]System.Span`1<native uint> pointerValues) cil managed
			{
			}
		}

		""");
		foreach (var x in (ReadOnlySpan<int>)[64, 4096, 8192])
		{
			sb.Append(string.Create(CultureInfo.InvariantCulture, $$"""

			.class private auto ansi sealed beforefieldinit hamarb123.ByRefHandles.ILHelpers.Helper{{x}} extends hamarb123.ByRefHandles.ILHelpers.Helper
			{
				.method public hidebysig specialname rtspecialname instance void .ctor () cil managed
				{
					.maxstack 8
					ldarg.0
					call instance void class [CORE_ASSEMBLY]System.Object::.ctor()
					ret
				}

				.method public hidebysig virtual instance uint32 RequiredPooledAllocSize() cil managed
				{
					.maxstack 8
					ldc.i4 {{x / 64 + (x / 64 + 3) / 4}}
					ldc.i4 {{x}}
					ldc.i4.8
					sizeof native uint
					div.un
					div.un
					add
					ret
				}

				.method public hidebysig virtual instance void ThreadHelper<valuetype T> (uint64* pooledAlloc, class hamarb123.ByRefHandles.ILHelpers.Helper`1<!!0> inst) cil managed
			#ifdef HAS_AGGRESSIVE_OPTIMIZATION
					aggressiveoptimization
			#endif
				{
					.maxstack 7
					.locals
					(

			"""));
			for (int i = 0; i < x; i++)
			{
				sb.Append(string.Create(CultureInfo.InvariantCulture, $$"""
							uint8& pinned, // {{i}}

				"""));
			}
			sb.Append(string.Create(CultureInfo.InvariantCulture, $$"""
						native uint 'tempInt',
						uint8& 'tempByRef',
						!!0 'state',
						valuetype [SPAN_ASSEMBLY]System.Span`1<uint64> 'usedSlots',
						uint16 'usedSlotsFreeListSize',
						valuetype [SPAN_ASSEMBLY]System.Span`1<uint16> 'usedSlotsFreeList',
						valuetype [SPAN_ASSEMBLY]System.Span`1<native uint> 'pointerValues'
					)

					ldarg.1
					ldc.i4 {{x / 64}}
			#ifdef HAS_MEMORY_MARSHAL_CREATE_SPAN
					call valuetype [SPAN_ASSEMBLY]System.Span`1<!!0> [MEMORY_ASSEMBLY]System.Runtime.InteropServices.MemoryMarshal::CreateSpan<uint64>(!!0&, int32)
			#else
					newobj instance void valuetype [SPAN_ASSEMBLY]System.Span`1<uint64>::.ctor(void*, int32)
			#endif
					stloc 'usedSlots'

					ldarg.1
					ldc.i4 {{x / 8}}
					conv.u
					add
					ldc.i4 {{x / 64}}
			#ifdef HAS_MEMORY_MARSHAL_CREATE_SPAN
					call valuetype [SPAN_ASSEMBLY]System.Span`1<!!0> [MEMORY_ASSEMBLY]System.Runtime.InteropServices.MemoryMarshal::CreateSpan<uint16>(!!0&, int32)
			#else
					newobj instance void valuetype [SPAN_ASSEMBLY]System.Span`1<uint16>::.ctor(void*, int32)
			#endif
					stloc 'usedSlotsFreeList'

					ldarg.1
					ldc.i4 {{x / 8 + x / 32}}
					conv.u
					add
					ldc.i4 {{x}}
			#ifdef HAS_MEMORY_MARSHAL_CREATE_SPAN
					call valuetype [SPAN_ASSEMBLY]System.Span`1<!!0> [MEMORY_ASSEMBLY]System.Runtime.InteropServices.MemoryMarshal::CreateSpan<native uint>(!!0&, int32)
			#else
					newobj instance void valuetype [SPAN_ASSEMBLY]System.Span`1<native uint>::.ctor(void*, int32)
			#endif
					stloc 'pointerValues'

					ldarg.2
					ldloca 'state'
					ldloc 'usedSlots'
					ldloca 'usedSlotsFreeListSize'
					ldloc 'usedSlotsFreeList'
					ldloc 'pointerValues'
					callvirt instance void class hamarb123.ByRefHandles.ILHelpers.Helper`1<!!0>::InitState(!0&, valuetype [SPAN_ASSEMBLY]System.Span`1<uint64>, uint16&, valuetype [SPAN_ASSEMBLY]System.Span`1<uint16>, valuetype [SPAN_ASSEMBLY]System.Span`1<native uint>)

					repeat:
					ldc.i4.0
					conv.u
					stloc 'tempByRef'

					ldarg.2
					ldloca 'state'
					ldloca 'tempInt'
					ldloc 'usedSlots'
					ldloca 'usedSlotsFreeListSize'
					ldloc 'usedSlotsFreeList'
					ldloc 'pointerValues'
					callvirt instance uint8& class hamarb123.ByRefHandles.ILHelpers.Helper`1<!!0>::Command(!0&, native uint&, valuetype [SPAN_ASSEMBLY]System.Span`1<uint64>, uint16&, valuetype [SPAN_ASSEMBLY]System.Span`1<uint16>, valuetype [SPAN_ASSEMBLY]System.Span`1<native uint>)

					stloc 'tempByRef'
					ldloc 'tempInt'
					ldc.i4 {{x - 1}}
					cgt.un
					brtrue end
					ldloc 'tempInt'
					conv.i4

					switch
					(
			"""));
			for (int i = 0; i < x; i++)
			{
				sb.Append(string.Create(CultureInfo.InvariantCulture, $$"""

							_{{i}}{{(i != (x - 1) ? "," : "")}}
				"""));
			}
			sb.Append("""

					)


			""");
			for (int i = 0; i < x; i++)
			{
				sb.Append(string.Create(CultureInfo.InvariantCulture, $$"""

						_{{i}}:
						ldloc 'tempByRef'
						stloc {{i}}
						br repeat

				"""));
			}
			sb.Append("""


					end:
					ret
				}
			}

			""");
		}

		var contents = sb.ToString();
		var dir = AppContext.BaseDirectory;
		while (!"hamarb123.ByRefHandles".Equals(Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase))
		{
			dir = Directory.GetParent(dir)!.FullName;
		}
		File.WriteAllText(Path.Combine(dir, "hamarb123.ByRefHandles.ILHelpers", "PinnedByRefHandleHelpers.generated.il"), contents.ReplaceLineEndings("\n"));
	}
}
