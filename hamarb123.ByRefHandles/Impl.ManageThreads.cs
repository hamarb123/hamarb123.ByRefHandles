using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

#if NET9_0_OR_GREATER
using LockType = System.Threading.Lock;
#else
using LockType = System.Object;
#endif

namespace hamarb123.ByRefHandles;

internal partial class Impl
{
	private static Impl?[] GetInitialImpls()
	{
		var x = ArrayPool<Impl?>.Shared.Rent(16);
		x.AsSpan().Clear();
		return x;
	}
	private static readonly LockType _globalLock = new(); // Global lock
	private static Impl?[] _impls = GetInitialImpls(); // Current impls that have 1 or more slots available - values from index (_numImpls - _numFullImpls) should be ignored
	private static int _numImpls = 0; // Number of impls available at the moment (including full & empty)
	private static int _numFullImpls = 0; // Number of impls completely full at the moment
	private static int _numEmptyImpls = 0; // Number of impls completely empty at the moment
	private static nuint _numPotentialPins = 0; // Number of pin handles we can allocate at once without creating more instances
	private static nuint _numActivePins = 0; // Number of pin handles we currently have allocated at once

	// Creates an ILHelpers.Helper based on the amount of impls currenty created
	// Caller must be holding the _globalLock lock
	private static ILHelpers.Helper SelectHelper(out uint size)
	{
		if (_numImpls == 0)
		{
			// Make a 64 size instance for the first one
			size = 64;
			return new ILHelpers.Helper64();
		}
		else if (_numImpls < 32)
		{
			// Then do 4096 for the next up to 32
			size = 4096;
			return new ILHelpers.Helper4096();
		}
		else
		{
			// Then make 8192 only
			size = 8192;
			return new ILHelpers.Helper8192();
		}
	}

	// We pretend we have less slots than we actually do when we have a small number of impls.
	// This allows us to get a better spread across instances when we are creating a large amount, but still small overall, of byref handles,
	// which in turn can lead to better mutli-threaded usage.
	// Caller must be holding the _globalLock lock
#if NET7_0_OR_GREATER
	private static nuint GetImitationMaxSlots() => nuint.Min(_numPotentialPins, _numImpls switch
#else
	private static nuint GetImitationMaxSlots() => (nuint)Math.Min((ulong)_numPotentialPins, _numImpls switch
#endif
	{
		1 => 64,
		< 4 => 256 * (nuint)_numImpls,
		< 8 => 1024 * (nuint)_numImpls,
		< 16 => 2048 * (nuint)_numImpls,
		< 32 => 4096 * (nuint)_numImpls,
		_ => int.MaxValue,
	});

	// Gets an instance which has a slot available - the command lock is taken before the value is returned
	private static unsafe Impl GetThreadHelper()
	{
		// Take global lock to figure out what one we will use
		lock (_globalLock)
		{
			// Increment active count
#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
			[DoesNotReturn]
#endif
			static void Throw() => throw new
#if NET7_0_OR_GREATER
				UnreachableException
#else
				Exception
#endif
				("Reached an internal implementation limitation of pinned byref handles - no more can be created.");
			if (_numActivePins == ~(nuint)0) Throw();
			_numActivePins++;
			Impl result;

			// Create a new impl if we are using at least half of the imitatation max slots, or if there are no free slots at all
			if ((_numImpls < _impls.Length && _numActivePins * 2 > GetImitationMaxSlots()) || (_numImpls - _numFullImpls == 0))
			{
				// Instantiate new instance & start the thread asap (as that's the slowest thing we have to wait for)
				result = new();
				ILHelpers.Helper helper = SelectHelper(out var size);
				result._arrayPool = ArrayPool<ulong>.Shared.Rent((int)helper.RequiredPooledAllocSize());
				if (_numPotentialPins + size < size) Throw();
				if (_numImpls == 1 << 30) Throw();
				static ThreadStart CreateThreadLambda(Impl impl, ILHelpers.Helper helper) => () =>
				{
					fixed (ulong* ptr = impl._arrayPool)
					{
						helper.ThreadHelper(ptr, impl);
					}
				};
				Thread t = new(CreateThreadLambda(result, helper), 1 << 20 /* Explicitly reserve 1MB */);
				t.IsBackground = true;
				t.Name = "ByRef Pinning Helper Thread";
				t.Start();

				// Update global values
				_numPotentialPins += size;
				_numImpls++;

				// Expand array if needed
				if (_numImpls > _impls.Length)
				{
					Debug.Assert(_numImpls - _numFullImpls == 1);
					_impls.AsSpan().Clear();
					ArrayPool<Impl?>.Shared.Return(_impls);
					_impls = ArrayPool<Impl?>.Shared.Rent(Math.Max((_numImpls - 1) * 2, 16));
					_impls.AsSpan().Clear();
				}

				// Write into array
				_impls[_numImpls - _numFullImpls - 1] = result;

				// Enter the command lock
#if NET9_0_OR_GREATER
				result.CommandLock.Enter();
#else
				Monitor.Enter(result.CommandLock);
#endif

				// Wait for our new instance to be ready (we have to do this in the global lock, since it initializes things like MaxFreeSlots)
				SpinWait sw = new(); // We have to use a spin wait, as our end signal may not be set up yet
				State* pState;
				while (true)
				{
					// Read _pState & check if it's initialized yet
					pState = result._pState;
#if NET10_0_OR_GREATER
					Volatile.ReadBarrier();
#else
					Thread.MemoryBarrier();
#endif
					if (pState == null) sw.SpinOnce();
					else break;
				}

				// Update used slots (we can do this now, since the above guarantees that pState gets zeroed, and that's all we do to set up this one)
				pState->UsedSlots = 1;

				// Finish wait for new instance to be ready
				Wait(&pState->EndSignalValue, (result._managedState as ManagedState)?._managedEndSignal);

				// Return our instance
				return result;
			}

			// Otherwise, use an existing buffer
			var idx = _numImpls - _numFullImpls - 1;
			result = _impls[idx]!;
			var max = result._pState->MaxFreeSlots;
			if (result._pState->UsedSlots++ == 0)
			{
				_numEmptyImpls--;
			}
			else if (result._pState->UsedSlots == max)
			{
				_numFullImpls++;
			}
#if NET9_0_OR_GREATER
			result.CommandLock.Enter();
#else
			Monitor.Enter(result.CommandLock);
#endif
			return result;
		}
	}

	// We do all the global state operations of shrinking here
	private static unsafe void Shrink(Impl justReduced)
	{
		// NOTE: there is a race condition, that we are intentionally ignoring, due to the way this function is called:
		// Theoretically, you could end up creating a new instance unnecessarily due to the UsedSlots & friends info not
		// being updated yet, but we are okay with this trade-off, as we can hold locks for less time in total (which
		// should improve every multi-threaded scenario), and it's not a correctness issue, as we will never try to use
		// a slot without one being available, or anything actually problematic like that. Some alternative solutions
		// to this either require holding both locks for ages, in the same order when creating as when destroying a handle,
		// and others (such as taking global in command in outer function) would lead to a deadlock with the current
		// implementation of handle creation. So we accept that it's possible to unnecessarily create a thread in an edge
		// case, and take the savings in the much more general cases.

		// Take global lock
		lock (_globalLock)
		{
			// Used slot info, and if required, update relevant global counters if needed, and add impl back into the _impls buffer at the end, if it just got reduced from full
			if (justReduced._pState->UsedSlots-- == justReduced._pState->MaxFreeSlots) _impls[_numImpls - _numFullImpls--] = justReduced;
			if (justReduced._pState->UsedSlots == 0) _numEmptyImpls++;

			// Update active pin count
			_numActivePins--;

			// Shrink by half if we're wasting 75% or more
			if (_numEmptyImpls >= _numImpls / 4 * 3 && _numImpls > 16)
			{
				// Assertions
				Debug.Assert(justReduced._pState->UsedSlots == 0);

				// Allocate a new buffer, and clear it
				Impl?[] newArr = ArrayPool<Impl?>.Shared.Rent(Math.Max(_numImpls / 2, 16));
				newArr.AsSpan().Clear();

				// Keep all which are currently partially in use, also calculate newNumPotentialPins
				var idx = 0;
				nuint newNumPotentialPins = 0;
				var implsToConsider = _numImpls - _numFullImpls;
				for (int i = 0; i < implsToConsider; i++)
				{
					// Skip if null
					var inst = _impls[i];
					if (inst is null) continue;

					// Increase newNumPotentialPins
					var max = inst._pState->MaxFreeSlots;
					newNumPotentialPins += max;

					// Skip any that are full or empty
					if (inst._pState->UsedSlots == 0) continue;
					if (inst._pState->UsedSlots == max) continue;

					// Copy into new array in next available slot
					newArr[idx++] = inst;
				}

				// Keep the one we just shrunk also
				newArr[idx++] = justReduced;

				// Fill rest of the array, EXCLUDING slots reserved for full impl values, until it gets full, then release any remaining impls that can't fit
				var emptyCount = 1;
				var numFullImpls = _numFullImpls;
				for (int i = 0; i < implsToConsider; i++)
				{
					// Skip if null
					var inst = _impls[i];
					if (inst is null) continue;

					// Skip any that aren't empty, as they're in use, and thus either have a slot reserved, or don't need to be copied
					if (inst._pState->UsedSlots != 0) continue;

					// If it's the one we just freed a slot from, skip it, as we've already placed it
					if (inst == justReduced) continue;

					// Check if there's more space available in newArr, if so, use it
					if (idx + numFullImpls < newArr.Length)
					{
						emptyCount++;
						newArr[idx++] = inst;
					}

					// Otherwise, signal this instance to exit
					else
					{
						// Check if something pre-existing we might need to wait for.
						// Ordinarily, CallerNeedsToCheckEndSignalFirst should only be accessed when CommandLock is held, but the instance is empty,
						// so we know that we aren't receiving any other commands simultaneously, as it's already been set up for freeing the last
						// instance, and the global lock would have to be acquired (which we are currently holding) before any new command could be
						// issued. So we can just ensure we've finished the last command, even though we don't hold the command lock, and queue our
						// new one to destroy the instance.
						if (inst._pState->CallerNeedsToCheckEndSignalFirst)
						{
							Wait(&inst._pState->EndSignalValue, (inst._managedState as ManagedState)?._managedEndSignal);
						}

						// Signal to exit
						inst._pState->Slot = ushort.MaxValue - 1;
						Signal(&inst._pState->StartSignalValue, (inst._managedState as ManagedState)?._managedStartSignal);
						ArrayPool<ulong>.Shared.Return(inst._arrayPool!);
					}
				}

				// Clear the old array (so we don't root memory longer than it needs to be rooted for), return it to the pool, and replace it
				_impls.AsSpan().Clear();
				ArrayPool<Impl?>.Shared.Return(_impls);
				_impls = newArr;

				// Update all the counts
				_numImpls = idx + numFullImpls;
				_numEmptyImpls = emptyCount;
				_numFullImpls = numFullImpls;
				_numPotentialPins = newNumPotentialPins;
			}
		}
	}
}
