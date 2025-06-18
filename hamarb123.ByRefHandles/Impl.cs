using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NETCOREAPP3_0_OR_GREATER
using System.Runtime.Intrinsics;
#if NET5_0_OR_GREATER
using System.Runtime.Intrinsics.Arm;
#endif
using System.Runtime.Intrinsics.X86;
#endif


#if NET9_0_OR_GREATER
using LockType = System.Threading.Lock;
#else
using LockType = System.Object;
#endif

namespace hamarb123.ByRefHandles;

internal unsafe sealed partial class Impl : ILHelpers.Helper<Impl.State>
{
	// Tracks whether WaitOnAddress is supported
	private static readonly bool IsWaitOnAddressSupported = WindowsHelpers.IsWaitOnAddressSupported || MacOSHelpers.IsWaitOnAddressSupported;

	internal struct State
	{
		public unsafe byte* Pointer; // New byref to assign
		public unsafe ushort* SlotPtr; // Pointer to write new slot into (unused in other modes)
		public unsafe nuint* ByRefValues; // Pointer to byref as an untracked pointer for each slot
		public uint StartSignalValue; // Value for futex-like APIs for starting signal
		public uint EndSignalValue; // Value for futex-like APIs for ending signal
		public ushort Slot; // Slot to update, or MaxValue to assign new, or MaxValue - 1 to end, or MaxValue - 2 - slot to free
		public ushort UsedSlots; // Number of slots currently being used - to update this, you must hold the global lock, the command lock is not needed
		public ushort MaxFreeSlots; // Number of slots available in total
		public ushort UpdatingSlot; // Which slot is being updated
		public bool CallerNeedsToCheckEndSignalFirst; // Tracks whether a caller needs to wait for the end signal first - this allows us to not wait for the finish signal for a null byref, since it doesn't need to be pinned manually
		public bool ShouldSignalCompletionOnCommand; // Tracks whether we need to signal completion upon re-entering the Command function
	}

	private sealed class ManagedState : IDisposable
	{
		public readonly LockType _locker = new();
		public readonly SemaphoreSlim _managedStartSignal = new(0, 1);
		public readonly SemaphoreSlim _managedEndSignal = new(0, 1);

		public void Dispose()
		{
			_managedStartSignal.Dispose();
			_managedEndSignal.Dispose();
		}
	}

	private State* _pState; // Pointer to unmanaged state
#if NET9_0_OR_GREATER
#pragma warning disable CS9216 // A value of type 'System.Threading.Lock' converted to a different type will use likely unintended monitor-based locking in 'lock' statement.
#endif
	private readonly object _managedState = IsWaitOnAddressSupported ? new LockType() : new ManagedState(); // Stores either the ManagedState object, or just the LockType object inline
#if NET9_0_OR_GREATER
#pragma warning restore CS9216 // A value of type 'System.Threading.Lock' converted to a different type will use likely unintended monitor-based locking in 'lock' statement.
#endif
	private ulong[]? _arrayPool;
	public override int GetHashCode() => ((nuint)_pState).GetHashCode();

	// Lock for queueing commands
	private LockType CommandLock => _managedState switch
	{
		ManagedState ms => ms._locker,
		var x => Unsafe.As<LockType>(x),
	};

	// Waits on a signal, resets it once signalled, and returns
	private static void Wait(uint* ptr, SemaphoreSlim? semaphore)
	{
		if (!IsWaitOnAddressSupported)
		{
			semaphore!.Wait();
		}
		else
		{
			Impl(ptr);

			// WaitOnAddress based impl
			static void Impl(uint* ptr)
			{
				SpinWait sw = new();
				while (true)
				{
					if (sw.NextSpinWillYield)
					{
#if NET5_0_OR_GREATER
						if (OperatingSystem.IsWindows()) WindowsHelpers.WaitOnAddress(ptr, 0);
#else
						if (WindowsHelpers.IsWaitOnAddressSupported) WindowsHelpers.WaitOnAddress(ptr, 0);
#endif
						else MacOSHelpers.WaitOnAddress(ptr, 0);
						break;
					}
					else
					{
						var actual = Volatile.Read(ref *ptr);
						if (actual != 0) break;
						sw.SpinOnce();
					}
				}
				Volatile.Write(ref *ptr, 0);
			}
		}
	}

	// Signals a signal
	private static void Signal(uint* ptr, SemaphoreSlim? semaphore)
	{
		if (!IsWaitOnAddressSupported)
		{
			semaphore!.Release();
		}
		else
		{
			Impl(ptr);

			// WakeByAddress based impl
			static void Impl(uint* ptr)
			{
				Volatile.Write(ref *ptr, 1);
#if NET5_0_OR_GREATER
				if (OperatingSystem.IsWindows()) WindowsHelpers.WakeByAddress(ptr);
#else
				if (WindowsHelpers.IsWaitOnAddressSupported) WindowsHelpers.WakeByAddress(ptr);
#endif
				else MacOSHelpers.WakeByAddress(ptr);
			}
		}
	}

	// Meaning of parameters for following 2 functions:
	// - state: custom state variable stored on stack for lifetime of the ThreadHelper function (at the same address), which is the only caller of the following 2 functions
	// - usedSlots: bitfield of currently used slots, each value tracks 64 slots
	// - usedSlotsFreeListSize: number of meaningful values in usedSlotsFreeList
	// - usedSlotsFreeList: indexes into usedSlots that are currently not completely full
	// - pointerValues: current pointer value for each tracked byref as an untracked managed pointer, with same lifetime requirements as state
	// - slot (Command only): the slot to assign the returned byref into, or >= MaxFreeSlots to indicate to exit

#if NETCOREAPP3_0_OR_GREATER
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
	public override void InitState(out State state, Span<ulong> usedSlots, ref ushort usedSlotsFreeListSize, Span<ushort> usedSlotsFreeList, Span<nuint> pointerValues)
	{
		// Initialize state to default
		state = default;

		// Initialize _pState variable - ensure this happens after the defaulting operation.
		// Note: we require state to be on the stack & have a lifetime of at least all future Command calls.
#if NET10_0_OR_GREATER
		Volatile.WriteBarrier();
#else
		Thread.MemoryBarrier();
#endif
		_pState = (State*)Unsafe.AsPointer(ref state);

		// Initialize MaxFreeSlots
		var numSlots = (ushort)(usedSlots.Length * 64);
		state.MaxFreeSlots = numSlots;

		// Initialize ByRefValues
		state.ByRefValues = (nuint*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(pointerValues));

		// Signal creating thread that we're set up
		Signal(&_pState->EndSignalValue, (_managedState as ManagedState)?._managedEndSignal);

		// Initialize other stuff
		usedSlots.Clear();
		pointerValues.Clear();
		usedSlotsFreeListSize = (ushort)usedSlots.Length;

		// Initialize usedSlotsFreeList to 0, 1, 2, 3, ...
#if NET7_0_OR_GREATER
#if NET8_0_OR_GREATER
		if (Vector512.IsHardwareAccelerated && usedSlotsFreeList.Length >= Vector512<ushort>.Count)
		{
			Vector512<ushort> value = Vector512.Create((ushort)0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31);
			ref var reference = ref MemoryMarshal.GetReference(usedSlotsFreeList);
			ref var endM1V = ref Unsafe.SubtractByteOffset(ref Unsafe.Add(ref reference, (uint)usedSlotsFreeList.Length), sizeof(Vector512<ushort>));
			while (Unsafe.IsAddressLessThan(ref reference, ref endM1V))
			{
				Vector512.StoreUnsafe(value, ref reference);
				value += Vector512.Create((ushort)32);
				reference = ref Unsafe.AddByteOffset(ref reference, sizeof(Vector512<ushort>));
			}
			Vector512.StoreUnsafe(Vector512.Create((ushort)usedSlotsFreeList.Length) - Vector512.Create((ushort)32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1), ref endM1V);
		}
		else
#endif
		if (Vector256.IsHardwareAccelerated && usedSlotsFreeList.Length >= Vector256<ushort>.Count)
		{
			Vector256<ushort> value = Vector256.Create((ushort)0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
			ref var reference = ref MemoryMarshal.GetReference(usedSlotsFreeList);
			ref var endM1V = ref Unsafe.SubtractByteOffset(ref Unsafe.Add(ref reference, (uint)usedSlotsFreeList.Length), sizeof(Vector256<ushort>));
			while (Unsafe.IsAddressLessThan(ref reference, ref endM1V))
			{
				Vector256.StoreUnsafe(value, ref reference);
				value += Vector256.Create((ushort)16);
				reference = ref Unsafe.AddByteOffset(ref reference, sizeof(Vector256<ushort>));
			}
			Vector256.StoreUnsafe(Vector256.Create((ushort)usedSlotsFreeList.Length) - Vector256.Create((ushort)16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1), ref endM1V);
		}
		else if (Vector128.IsHardwareAccelerated && usedSlotsFreeList.Length >= Vector128<ushort>.Count)
		{
			Vector128<ushort> value = Vector128.Create((ushort)0, 1, 2, 3, 4, 5, 6, 7);
			ref var reference = ref MemoryMarshal.GetReference(usedSlotsFreeList);
			ref var endM1V = ref Unsafe.SubtractByteOffset(ref Unsafe.Add(ref reference, (uint)usedSlotsFreeList.Length), sizeof(Vector128<ushort>));
			while (Unsafe.IsAddressLessThan(ref reference, ref endM1V))
			{
				Vector128.StoreUnsafe(value, ref reference);
				value += Vector128.Create((ushort)8);
				reference = ref Unsafe.AddByteOffset(ref reference, sizeof(Vector128<ushort>));
			}
			Vector128.StoreUnsafe(Vector128.Create((ushort)usedSlotsFreeList.Length) - Vector128.Create((ushort)8, 7, 6, 5, 4, 3, 2, 1), ref endM1V);
		}
		else
#elif NETCOREAPP3_0_OR_GREATER
		if ((Sse2.IsSupported
#if NET5_0_OR_GREATER
			|| AdvSimd.IsSupported
#endif
			) && usedSlotsFreeList.Length >= Vector128<ushort>.Count)
		{
			Vector128<ushort> value = Vector128.Create((ushort)0, 1, 2, 3, 4, 5, 6, 7);
			ref var reference = ref MemoryMarshal.GetReference(usedSlotsFreeList);
			ref var endM1V = ref Unsafe.SubtractByteOffset(ref Unsafe.Add(ref reference, (uint)usedSlotsFreeList.Length), (uint)sizeof(Vector128<ushort>));
			while (Unsafe.IsAddressLessThan(ref reference, ref endM1V))
			{
				Unsafe.WriteUnaligned(ref Unsafe.As<ushort, byte>(ref reference), value);
				if (Sse2.IsSupported) value = Sse2.Add(value, Vector128.Create((ushort)8));
#if NET5_0_OR_GREATER
				else if (AdvSimd.IsSupported) value = AdvSimd.Add(value, Vector128.Create((ushort)8));
#endif
				else Debug.Fail("Assertion failed in hamarb123.ByRefHandles.Impl.InitState");
				reference = ref Unsafe.AddByteOffset(ref reference, (uint)sizeof(Vector128<ushort>));
			}
			if (Sse2.IsSupported) value = Sse2.Subtract(Vector128.Create((ushort)usedSlotsFreeList.Length), Vector128.Create((ushort)8, 7, 6, 5, 4, 3, 2, 1));
#if NET5_0_OR_GREATER
			else if (AdvSimd.IsSupported) value = AdvSimd.Subtract(Vector128.Create((ushort)usedSlotsFreeList.Length), Vector128.Create((ushort)8, 7, 6, 5, 4, 3, 2, 1));
#endif
			else Debug.Fail("Assertion failed in hamarb123.ByRefHandles.Impl.InitState");
			Unsafe.WriteUnaligned(ref Unsafe.As<ushort, byte>(ref endM1V), value);
		}
		else
#endif
		{
			for (int i = 0; i < usedSlotsFreeList.Length; i++)
			{
				usedSlotsFreeList[i] = (ushort)i;
			}
		}
	}

	public unsafe override ref byte Command(ref State state, out nuint slot, Span<ulong> usedSlots, ref ushort usedSlotsFreeListSize, Span<ushort> usedSlotsFreeList, Span<nuint> pointerValues)
	{
		// Signal the caller that we've successfully saved & pinned their byref if we need to
		// (this happens when we re-call this method for the last call).
		if (state.ShouldSignalCompletionOnCommand)
		{
			Signal(&_pState->EndSignalValue, (_managedState as ManagedState)?._managedEndSignal);
			state.ShouldSignalCompletionOnCommand = false;
		}

		// Wait until we receive a command
		Wait(&_pState->StartSignalValue, (_managedState as ManagedState)?._managedStartSignal);

		// Update mode
		var stateSlot = state.Slot;
		if (stateSlot < state.MaxFreeSlots)
		{
			state.ShouldSignalCompletionOnCommand = true;
			slot = stateSlot;
			return ref *state.Pointer;
		}

		// Assign new mode
		if (stateSlot == ushort.MaxValue)
		{
			state.ShouldSignalCompletionOnCommand = true;
			var slotSet = usedSlotsFreeList[usedSlotsFreeListSize - 1];
			ref var bits = ref usedSlots[slotSet];
			var bit =
#if NETCOREAPP3_0_OR_GREATER
				BitOperations.
#endif
				TrailingZeroCount(~bits);
			slot = (uint)slotSet * 64 + (uint)bit;
			*state.SlotPtr = (ushort)slot;
			bits |= 1UL << bit;
			if (bits == ~(ulong)0) usedSlotsFreeListSize--;
			pointerValues[(int)slot] = (nuint)state.Pointer;
			return ref *state.Pointer;
		}

		// Exit mode
		if (stateSlot == ushort.MaxValue - 1)
		{
			slot = ~(nuint)0;
			(_managedState as IDisposable)?.Dispose();
			return ref *(byte*)null;
		}

		// Free slot mode
		Signal(&_pState->EndSignalValue, (_managedState as ManagedState)?._managedEndSignal);
		var potentialSlot = ushort.MaxValue - 2 - stateSlot;
		//if (potentialSlot < state.MaxFreeSlots)
		{
			Debug.Assert(potentialSlot < state.MaxFreeSlots);
			slot = (ushort)potentialSlot;
			var slotSet = (ushort)(slot / 64);
			var bit = (int)slot & 63;
			ref var bits = ref usedSlots[slotSet];
			if (bits == ~(ulong)0) usedSlotsFreeList[usedSlotsFreeListSize++] = slotSet;
			bits &= ~(1UL << bit);
			return ref *(byte*)null;
		}
	}

#if !NETCOREAPP3_0_OR_GREATER
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int TrailingZeroCount(ulong value)
	{
		var low = (uint)value;
		return (low == 0) ? TrailingZeroCount((uint)(value >>> 32)) : TrailingZeroCount(low);
	}

	// This section (TrailingZeroCount stuff) is based on: https://github.com/dotnet/runtime/blob/ec11903827fc28847d775ba17e0cd1ff56cfbc2e/src/libraries/Microsoft.Bcl.Memory/src/Polyfills/System.Numerics.BitOperations.netstandard20.cs.
	// Licensed for use under MIT.

	private static ReadOnlySpan<byte> TrailingZeroCountDeBruijn => // 32
	[
		00, 01, 28, 02, 29, 14, 24, 03,
		30, 22, 20, 15, 25, 17, 04, 08,
		31, 27, 13, 23, 21, 19, 16, 07,
		26, 12, 18, 06, 11, 05, 10, 09
	];

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int TrailingZeroCount(uint value)
	{
		// Unguarded fallback contract is 0->0, BSF contract is 0->undefined
		if (value == 0)
		{
			return 32;
		}

		// uint.MaxValue >> 27 is always in range [0 - 31] so we use Unsafe.AddByteOffset to avoid bounds check
		return Unsafe.AddByteOffset(
			// Using deBruijn sequence, k=2, n=5 (2^5=32) : 0b_0000_0111_0111_1100_1011_0101_0011_0001u
			ref MemoryMarshal.GetReference(TrailingZeroCountDeBruijn),
			// uint|long -> IntPtr cast on 32-bit platforms does expensive overflow checks not needed here
			(IntPtr)(int)(((value & (uint)-(int)value) * 0x077CB531u) >> 27)); // Multi-cast mitigates redundant conv.u8
	}
#endif

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public byte* GetPointerBySlot(ushort slot)
	{
		Debug.Assert(_pState != null);
		Debug.Assert(slot < _pState->MaxFreeSlots);
		return (byte*)_pState->ByRefValues[slot];
	}

	public static (Impl, ushort) Create(ref byte reference)
	{
		// Get an instance
		Impl instance = GetThreadHelper();

		// We have already began the lock, so we just try/finally to exit it
		try
		{
			// Ensure we can run now
			if (instance._pState->CallerNeedsToCheckEndSignalFirst)
			{
				Wait(&instance._pState->EndSignalValue, (instance._managedState as ManagedState)?._managedEndSignal);
				instance._pState->CallerNeedsToCheckEndSignalFirst = false;
			}

			// Pin the byref
			fixed (byte* ptr = &reference)
			{
				// Set command
				ushort slot;
				instance._pState->Slot = ushort.MaxValue;
				instance._pState->Pointer = ptr;
				instance._pState->SlotPtr = &slot;

				// Initiate command
				Signal(&instance._pState->StartSignalValue, (instance._managedState as ManagedState)?._managedStartSignal);

				// Wait until complete
				Wait(&instance._pState->EndSignalValue, (instance._managedState as ManagedState)?._managedEndSignal);

				// Return result
				return (instance, slot);
			}
		}
		finally
		{
#if NET9_0_OR_GREATER
			instance.CommandLock.Exit();
#else
			Monitor.Exit(instance.CommandLock);
#endif
		}
	}

	public void Update(ushort slot, ref byte reference, bool wait)
	{
		// Check if it already has this pointer (there's no need to actually update it if so)
		if (_pState->ByRefValues[slot] == (nuint)Unsafe.AsPointer(ref reference)) return;

		// Lock the instance
		lock (CommandLock)
		{
			// Ensure we can run now
			if (_pState->CallerNeedsToCheckEndSignalFirst)
			{
				Wait(&_pState->EndSignalValue, (_managedState as ManagedState)?._managedEndSignal);
				_pState->CallerNeedsToCheckEndSignalFirst = false;
			}

			// Pin the byref
			fixed (byte* ptr = &reference)
			{
				// Set command
				_pState->Slot = slot;
				_pState->Pointer = ptr;

				// Initiate command
				Signal(&_pState->StartSignalValue, (_managedState as ManagedState)?._managedStartSignal);

				// Store pointer (for access via .Target & friends)
				_pState->ByRefValues[slot] = (nuint)ptr;

				// Wait until complete
				if (wait)
				{
					Wait(&_pState->EndSignalValue, (_managedState as ManagedState)?._managedEndSignal);
				}
				else
				{
					_pState->CallerNeedsToCheckEndSignalFirst = true;
					_pState->UpdatingSlot = slot;
				}
			}
		}
	}

	public void EndUpdate(ushort slot)
	{
		// Pre-emptively check if we know the values are different, even outside of the command lock.
		// The only way this method can end up seeing values other than (slot, true) is if it's already had another action done on this
		// thread which required waiting for the last one; or if another thread has already waited for it & we happen to see changes to
		// these values on this thread - either way, this action is completed if we see that. We can still have a false negative, but
		// we eliminate that possibility by checking again within the command lock (this check is only an optimisation). This method is
		// not thread-safe, so we assume there's no race condition (such as what update 0, update 1, update 0 (from another thread) can
		// cause) that makes us check "incorrectly" or incorrectly wait when we shouldn't be, as it's the user's responsibility to
		// ensure thread safety if they need it through use of mechanisms like locks and volatile, and to ensure they're waiting on the
		// correct operation performed on a particular handle.
		if (_pState->UpdatingSlot != slot || !_pState->CallerNeedsToCheckEndSignalFirst)
		{
			return;
		}

		// Lock the instance
		lock (CommandLock)
		{
			// Ensure we have finished any operations on this slot
			if (_pState->UpdatingSlot == slot && _pState->CallerNeedsToCheckEndSignalFirst)
			{
				Wait(&_pState->EndSignalValue, (_managedState as ManagedState)?._managedEndSignal);
				_pState->CallerNeedsToCheckEndSignalFirst = false;
			}
		}
	}

	public static void Destroy(Impl impl, ushort slot)
	{
		// Take instance lock
		lock (impl.CommandLock)
		{
			// Ensure we can run now
			if (impl._pState->CallerNeedsToCheckEndSignalFirst)
			{
				Wait(&impl._pState->EndSignalValue, (impl._managedState as ManagedState)?._managedEndSignal);
				impl._pState->CallerNeedsToCheckEndSignalFirst = false;
			}

			// Set command
			impl._pState->Slot = (ushort)(ushort.MaxValue - 2 - slot);

			// Initiate command
			Signal(&impl._pState->StartSignalValue, (impl._managedState as ManagedState)?._managedStartSignal);

			// Wait until complete
			impl._pState->CallerNeedsToCheckEndSignalFirst = true;
			impl._pState->UpdatingSlot = ushort.MaxValue;
		}

		// Shrink helper
		Shrink(impl, false);
	}

	// Internal helper to trim threads
	public static void TrimThreads()
	{
		Shrink(null, true);
	}
}
