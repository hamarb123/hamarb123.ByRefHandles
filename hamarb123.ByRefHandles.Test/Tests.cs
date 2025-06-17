using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace hamarb123.ByRefHandles.Test;

// All omitted readonlys in this file are intentional
#pragma warning disable IDE0044 // Add readonly modifier

public static class Tests
{
	public static int AllocateFreeNull()
	{
		// Allocate
		PinnedByRefHandle<int> handle = new(ref Unsafe.NullRef<int>());
		Thread.Yield();

		// Validate
		Assert.True(Unsafe.AreSame(ref handle.Target, ref Unsafe.NullRef<int>()));

		// Free
		handle.Dispose();
		return 42;
	}

#if NET9_0_OR_GREATER
	public static int AllocateFreeRefStruct()
	{
		// Allocate
		Span<int> sp = default;
		PinnedByRefHandle<Span<int>> handle = new(ref sp);
		Thread.Yield();

		// Validate
		Assert.True(Unsafe.AreSame(ref handle.Target, ref sp));

		// Free
		handle.Dispose();
		return 42;
	}
#endif

	public static unsafe int AllocateFreeRefStructAsByte()
	{
		// Allocate
		Span<int> sp = default;
		PinnedByRefHandle<byte> handle = new(ref *(byte*)&sp);
		Thread.Yield();

		// Validate
		Assert.True(Unsafe.AreSame(ref handle.Target, ref *(byte*)&sp));

		// Free
		handle.Dispose();
		return 42;
	}

	public static int AllocateFreeLocal()
	{
		// Allocate
		int local = default;
		PinnedByRefHandle<int> handle = new(ref local);
		Thread.Yield();

		// Validate
		Assert.True(Unsafe.AreSame(ref handle.Target, ref local));

		// Free
		handle.Dispose();
		return 42;
	}

	[FixedAddressValueType]
	private static int _favt;
	public static int AllocateFreeFAVT()
	{
		// Allocate
		PinnedByRefHandle<int> handle = new(ref _favt);
		Thread.Yield();

		// Validate
		Assert.True(Unsafe.AreSame(ref handle.Target, ref _favt));

		// Free
		handle.Dispose();
		return 42;
	}

	private static volatile object _boxedPoint = new Point(0, 0);
	public static int AllocateFreeBoxed()
	{
		// Allocate
		PinnedByRefHandle<Point> handle = new(ref Unsafe.Unbox<Point>(_boxedPoint));
		Thread.Yield();

		// Validate
		Assert.True(Unsafe.AreSame(ref handle.Target, ref Unsafe.Unbox<Point>(_boxedPoint)));

		// Free
		handle.Dispose();
		return 42;
	}

	private static volatile Point[] _pointArray = [new(0, 0), new(1, 0)];
	public static int AllocateFreeArray()
	{
		// Allocate
		PinnedByRefHandle<Point> handle = new(ref _pointArray[0]);
		Thread.Yield();

		// Validate
		Assert.True(Unsafe.AreSame(ref handle.Target, ref _pointArray[0]));

		// Free
		handle.Dispose();
		return 42;
	}

	public static int AllocateUpdateFreeArray()
	{
		// Allocate
		PinnedByRefHandle<Point> handle = new(ref _pointArray[0]);
		Thread.Yield();

		// Validate
		Assert.True(Unsafe.AreSame(ref handle.Target, ref _pointArray[0]));

		// Update
		handle.SetTarget(ref _pointArray[1]);
		Thread.Yield();

		// Validate
		Assert.True(Unsafe.AreSame(ref handle.Target, ref _pointArray[1]));

		// Free
		handle.Dispose();
		return 42;
	}

	public static int AllocateFreeWeak()
	{
		// Allocate
		object? value = new Point(0, 0);
		PinnedByRefHandle<Point> handle = new(ref Unsafe.Unbox<Point>(value));
		WeakReference wr = new(value);
		Volatile.Write(ref value, null);
		Volatile.Read(ref value);
		Thread.Yield();

		// Validate
		Volatile.Write(ref value, wr.Target);
		Assert.NotNull(value);
		Assert.True(Unsafe.AreSame(ref handle.Target, ref Unsafe.Unbox<Point>(value)));

		// Free
		handle.Dispose();
		return 42;
	}

	// Allows us to make a Memory<T> from a Span<T>.
	// Not thread safe, just good enough for our testing purposes.
	private class CustomMemoryManager<T>(Span<T> span) : MemoryManager<T>
	{
		private PinnedByRefHandle<T> _handle = new(ref MemoryMarshal.GetReference(span));
		private int _length = span.Length;
		public void Update(Span<T> newSpan)
		{
			_handle.SetTarget(ref MemoryMarshal.GetReference(newSpan));
			_length = newSpan.Length;
		}
		public override Span<T> GetSpan() => MemoryMarshal.CreateSpan(ref _handle.Target, _length);
		public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
		public override void Unpin() { }
		protected override void Dispose(bool disposing)
		{
			_handle.Dispose();
			_length = 0;
		}
	}

	public static Task<int> UseInAsync()
	{
		// Set up
		int[] arr = new int[5];
		var sp = arr.AsSpan();
		var mem = new CustomMemoryManager<int>(sp).Memory;

		// Call helper
		return Impl(arr, mem);

		static async Task<int> Impl(int[] arr, Memory<int> spHandle)
		{
			// Validate length
			await Task.Yield();
			Assert.Equal(5, spHandle.Span.Length);

			// Update [0]
			spHandle.Span[0] = 1;

			// Validate
			Assert.Equal([1, 0, 0, 0, 0], arr);
			await Task.Delay(1);

			// Update [4]
			spHandle.Span[4] = 5;

			// Validate
			Assert.Equal([1, 0, 0, 0, 5], arr);
			await Task.Yield();

			// Update [2]
			spHandle.Span[2] = 3;

			// Validate
			Assert.Equal([1, 0, 3, 0, 5], arr);
			await Task.Delay(1);
			Assert.True(Unsafe.AreSame(ref arr[0], ref spHandle.Span[0]));
			Assert.Equal([1, 0, 3, 0, 5], arr);
			return 42;
		}
	}

	private delegate TResult SpanFunc<T, TResult>(Span<T> parameter);
	public static Task<int> UseInAsyncWithUpdate()
	{
		// Set up
		int[] arr = new int[5];
		var sp = arr.AsSpan();
		CustomMemoryManager<int> mgr = new(sp);

		// Call helper
		return Impl(arr, (sp) =>
		{
			mgr.Update(sp);
			return mgr.Memory;
		}, mgr.Memory);

		static async Task<int> Impl(int[] arr, SpanFunc<int, Memory<int>> update, Memory<int> spHandle)
		{
			// Validate length
			await Task.Yield();
			Assert.Equal(5, spHandle.Span.Length);

			// Update [0]
			spHandle.Span[0] = 1;

			// Validate
			Assert.Equal([1, 0, 0, 0, 0], arr);
			await Task.Delay(1);

			// Update [4]
			spHandle.Span[4] = 5;

			// Validate
			Assert.Equal([1, 0, 0, 0, 5], arr);
			await Task.Yield();

			// Update span
			spHandle = update(spHandle.Span[1..^1]);
			await Task.Yield();

			// Validate length
			Assert.Equal(3, spHandle.Span.Length);

			// Update [2] (of original)
			spHandle.Span[1] = 3;

			// Validate
			Assert.Equal([1, 0, 3, 0, 5], arr);
			await Task.Delay(1);
			Assert.True(Unsafe.AreSame(ref arr[1], ref spHandle.Span[0]));
			return 42;
		}
	}

	public static unsafe int AllocateUpdateFreeVariable(int size, bool useTwoStageUpdate)
	{
		// Allocate array of specified size
		Point[] arr = new Point[size];
		[MethodImpl(MethodImplOptions.NoInlining)] static void Consume<T>(T value) { }
		Consume(arr);

		// Create a handle to each value, then validate them
		PinnedByRefHandle<Point>[] handles = new PinnedByRefHandle<Point>[size];
		for (int i = 0; i < size; i++)
		{
			handles[i] = new(ref arr[i]);
		}
		Thread.Yield();
		for (int i = 0; i < size; i++)
		{
			Assert.True(Unsafe.AreSame(ref handles[i].Target, ref arr[i]), $"Failed check 1 at {i}");
		}
		Thread.Yield();

		// Update the handle to the same index, but from the end, and then validate them
		for (int i = 0; i < size; i++)
		{
			if (!useTwoStageUpdate)
			{
				handles[i].SetTarget(ref arr[size - 1 - i]);
			}
			else
			{
				fixed (Point* pt = &arr[size - 1 - i])
				{
					handles[i].BeginSetTarget(ref arr[size - 1 - i]);
					if (i % 2 == 0) Thread.Yield();
					handles[i].EndSetTarget();
				}
			}
		}
		Thread.Yield();
		for (int i = 0; i < size; i++)
		{
			Assert.True(Unsafe.AreSame(ref handles[i].Target, ref arr[size - 1 - i]), $"Failed check 2 at {i}");
		}
		Thread.Yield();

		// Release all of the handles
		foreach (ref var x in handles.AsSpan()) x.Dispose();
		Thread.Yield();

		// Return
		Consume(arr);
		return 42;
	}

	public static int AllocateReinterpretFreeArray()
	{
		// Allocate
		PinnedByRefHandle<Point> handle1 = new(ref _pointArray[0]);
		Thread.Yield();

		// Validate
		Assert.True(Unsafe.AreSame(ref handle1.Target, ref _pointArray[0]));

		// Re-interpret
		var handle2 = handle1.UnsafeReinterpret<int>();
		Thread.Yield();

		// Validate
		Assert.True(Unsafe.AreSame(ref handle2.Target, ref Unsafe.As<Point, int>(ref _pointArray[0])));

		// Free
		handle2.Dispose();
		return 42;
	}

	private static void Gc()
	{
		// Thorough GC
		for (int i = 0; i < 2; i++)
		{
			GC.Collect(int.MaxValue, GCCollectionMode.Forced, true, true);
			GC.WaitForPendingFinalizers();
		}
	}

	public static int AllocateFreeActuallyReleases()
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		static WeakReference Setup(out PinnedByRefHandle<int> handle)
		{
			// Create an array, store it in a weak reference, and store the byref to first element in a handle
			int[]? arr = new int[1];
			handle = new(ref arr[0]);
			WeakReference wr = new(arr);
			return wr;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		static void Check1(ref PinnedByRefHandle<int> handle, WeakReference wr)
		{
			// Validate that the weak ref is still alive, and that the handle points to the right location
			Assert.NotNull(wr.Target);
			Assert.True(Unsafe.AreSame(ref handle.Target, ref ((int[])wr.Target!)[0]));

			// Release the handle
			handle.Dispose();

			// Allocate another handle to ensure the freeing of the last one completed
			handle = new(ref Unsafe.NullRef<int>());
			handle.Dispose();
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		static void Check2(WeakReference wr)
		{
			// Validate that the weak reference is now not alive, now that our handle is released
			Assert.Null(wr.Target);
		}

		WeakReference wr = Setup(out var handle);
		Thread.Yield();
		Gc();
		Check1(ref handle, wr);
		Thread.Yield();
		Gc();
		Check2(wr);
		return 42;
	}

	public static int AllocateConvertFreeActuallyReleases()
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		static WeakReference Setup(out (object?, nuint, nuint) handleRepr)
		{
			// Create an array, store it in a weak reference, and store the byref to first element in a handle
			int[]? arr = new int[1];
			PinnedByRefHandle<int> handle = new(ref arr[0]);
			WeakReference wr = new(arr);

			// Convert the handle using ToInternalRepresentation
			handleRepr = PinnedByRefHandle<int>.ToInternalRepresentation(handle);
			return wr;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		static void Check1((object?, nuint, nuint) handleRepr, WeakReference wr)
		{
			// Validate that the weak ref is still alive, and that the handle (which we convert back now) points to the right location
			Assert.NotNull(wr.Target);
			PinnedByRefHandle<int> handle = PinnedByRefHandle<int>.FromInternalRepresentation(handleRepr);
			Assert.True(Unsafe.AreSame(ref handle.Target, ref ((int[])wr.Target!)[0]));

			// Release the handle
			handle.Dispose();

			// Allocate another handle to ensure the freeing of the last one completed
			handle = new(ref Unsafe.NullRef<int>());
			handle.Dispose();
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		static void Check2(WeakReference wr)
		{
			// Validate that the weak reference is now not alive, now that our handle is released
			Assert.Null(wr.Target);
		}

		WeakReference wr = Setup(out var handle);
		Thread.Yield();
		Gc();
		Check1(handle, wr);
		Thread.Yield();
		Gc();
		Check2(wr);
		return 42;
	}

	public static int MultiThreadCreateDestroy()
	{
		// Allow up to 1024 to exist at once, and have 4096 slots (in seperate arrays) to pick from.
		// Create a queue to store our current handles.
		SemaphoreSlim maxAtOnce = new(1 << 10, 1 << 10);
		ConcurrentQueue<(PinnedByRefHandle<int> Handle, int Index)> queue = [];
		int[][] arrays = [.. Enumerable.Range(0, 1 << 12).Select((x) => new int[1])];

		List<Thread> threads = [];
		for (int i = 0; i < 4; i++)
		{
			var _i = i;
			Thread t1 = new(() =>
			{
				// Creating thread
				try
				{
					Random r = new(_i * 2 + 1);
					var start = Stopwatch.GetTimestamp();
					var endAt = start + Stopwatch.Frequency * 4;
					while (Stopwatch.GetTimestamp() < endAt)
					{
						for (int i = 0; i < 256; i++)
						{
							// Wait until we can make one w/o exceeding our limit (or exit this loop if not w/i 1ms)
							if (!maxAtOnce.Wait(1)) break;

							// Create a handle and add it to the queue
							var idx = r.Next(arrays.Length);
							PinnedByRefHandle<int> handle = new(ref arrays[idx][0]);
							queue.Enqueue((handle, idx));
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
					Environment.Exit(40);
				}
			});
			Thread t2 = new(() =>
			{
				// Destroying thread
				try
				{
					Random r = new(_i * 2 + 2);
					var start = Stopwatch.GetTimestamp();
					var endAt = start + Stopwatch.Frequency * 4;
					while (Stopwatch.GetTimestamp() < endAt)
					{
						for (int i = 0; i < 256; i++)
						{
							if (queue.TryDequeue(out var inst))
							{
								// Check if the ref points to where it's meant to, then release it
								Assert.True(Unsafe.AreSame(ref inst.Handle.Target, ref arrays[inst.Index][0]));
								inst.Handle.Dispose();
								maxAtOnce.Release();
							}
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
					Environment.Exit(41);
				}
			});
			t1.Start();
			t2.Start();
			threads.Add(t1);
			threads.Add(t2);
		}

		// Wait until all exit
		foreach (var thread in threads) thread.Join();
		return 42;
	}

	public static int MultiThreadCreateUpdateDestroy()
	{
		// Allow up to 1024 to exist at once, and have 4096 slots (in seperate arrays) to pick from.
		// Create a queue to store our current handles.
		SemaphoreSlim maxAtOnce = new(1 << 10, 1 << 10);
		ConcurrentQueue<(PinnedByRefHandle<int> Handle, int Index)> queue = [];
		int[][] arrays = [.. Enumerable.Range(0, 1 << 12).Select((x) => new int[1])];

		List<Thread> threads = [];
		for (int i = 0; i < 3; i++)
		{
			int _i = i;
			Thread t1 = new(() =>
			{
				// Creating thread
				try
				{
					Random r = new(_i * 3 + 1);
					var start = Stopwatch.GetTimestamp();
					var endAt = start + Stopwatch.Frequency * 4;
					while (Stopwatch.GetTimestamp() < endAt)
					{
						for (int i = 0; i < 256; i++)
						{
							// Wait until we can make one w/o exceeding our limit (or exit this loop if not w/i 1ms)
							if (!maxAtOnce.Wait(1)) break;

							// Create a handle and add it to the queue
							var idx = r.Next(arrays.Length);
							PinnedByRefHandle<int> handle = new(ref arrays[idx][0]);
							queue.Enqueue((handle, idx));
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
					Environment.Exit(40);
				}
			});
			Thread t2 = new(() =>
			{
				// Destroying thread
				try
				{
					Random r = new(_i * 3 + 2);
					var start = Stopwatch.GetTimestamp();
					var endAt = start + Stopwatch.Frequency * 4;
					while (Stopwatch.GetTimestamp() < endAt)
					{
						for (int i = 0; i < 256; i++)
						{
							if (queue.TryDequeue(out var inst))
							{
								// Check if the ref points to where it's meant to, then release it
								Assert.True(Unsafe.AreSame(ref inst.Handle.Target, ref arrays[inst.Index][0]));
								inst.Handle.Dispose();
								maxAtOnce.Release();
							}
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
					Environment.Exit(41);
				}
			});
			Thread t3 = new(() =>
			{
				// Updating thread
				try
				{
					Random r = new(_i * 3 + 3);
					var start = Stopwatch.GetTimestamp();
					var endAt = start + Stopwatch.Frequency * 4;
					while (Stopwatch.GetTimestamp() < endAt)
					{
						for (int i = 0; i < 256; i++)
						{
							if (queue.TryDequeue(out var inst))
							{
								// Check if the ref points to where it's meant to, then update which one it points into & re-add it
								Assert.True(Unsafe.AreSame(ref inst.Handle.Target, ref arrays[inst.Index][0]));
								var idx = r.Next(arrays.Length);
								inst.Handle.SetTarget(ref arrays[idx][0]);
								queue.Enqueue((inst.Handle, idx));
							}
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
					Environment.Exit(43);
				}
			});
			t1.Start();
			t2.Start();
			t3.Start();
			threads.Add(t1);
			threads.Add(t2);
			threads.Add(t3);
		}

		// Wait until all exit
		foreach (var thread in threads) thread.Join();
		return 42;
	}

	public static int MultiThreadCreateUpdateDestroySmallHelperOnly()
	{
		// Variant of MultiThreadCreateUpdateDestroy, except it's run with only the small helper

		// Allow up to 2048 to exist at once, and have 8192 slots (in seperate arrays) to pick from.
		// Create a queue to store our current handles.
		SemaphoreSlim maxAtOnce = new(1 << 11, 1 << 11);
		ConcurrentQueue<(PinnedByRefHandle<int> Handle, int Index)> queue = [];
		int[][] arrays = [.. Enumerable.Range(0, 1 << 13).Select((x) => new int[1])];

		List<Thread> threads = [];
		for (int i = 0; i < 3; i++)
		{
			int _i = i;
			Thread t1 = new(() =>
			{
				// Creating thread
				try
				{
					Random r = new(_i * 3 + 1);
					var start = Stopwatch.GetTimestamp();
					var endAt = start + Stopwatch.Frequency * 4;
					while (Stopwatch.GetTimestamp() < endAt)
					{
						for (int i = 0; i < 256; i++)
						{
							// Wait until we can make one w/o exceeding our limit (or exit this loop if not w/i 1ms)
							if (!maxAtOnce.Wait(1)) break;

							// Create a handle and add it to the queue
							var idx = r.Next(arrays.Length);
							PinnedByRefHandle<int> handle = new(ref arrays[idx][0]);
							queue.Enqueue((handle, idx));
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
					Environment.Exit(40);
				}
			});
			Thread t2 = new(() =>
			{
				// Destroying thread
				try
				{
					Thread.Sleep(_i * 20); // Start these somewhat gradually
					Random r = new(_i * 3 + 2);
					var start = Stopwatch.GetTimestamp();
					var endAt = start + Stopwatch.Frequency * 4;
					while (Stopwatch.GetTimestamp() < endAt)
					{
						for (int i = 0; i < 256; i++)
						{
							if (queue.TryDequeue(out var inst))
							{
								// Check if the ref points to where it's meant to, then release it
								Assert.True(Unsafe.AreSame(ref inst.Handle.Target, ref arrays[inst.Index][0]));
								inst.Handle.Dispose();
								maxAtOnce.Release();
							}
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
					Environment.Exit(41);
				}
			});
			Thread t3 = new(() =>
			{
				// Updating thread
				try
				{
					Random r = new(_i * 3 + 3);
					var start = Stopwatch.GetTimestamp();
					var endAt = start + Stopwatch.Frequency * 4;
					while (Stopwatch.GetTimestamp() < endAt)
					{
						for (int i = 0; i < 256; i++)
						{
							if (queue.TryDequeue(out var inst))
							{
								// Check if the ref points to where it's meant to, then update which one it points into & re-add it
								Assert.True(Unsafe.AreSame(ref inst.Handle.Target, ref arrays[inst.Index][0]));
								var idx = r.Next(arrays.Length);
								inst.Handle.SetTarget(ref arrays[idx][0]);
								queue.Enqueue((inst.Handle, idx));
							}
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
					Environment.Exit(43);
				}
			});
			t1.Start();
			t2.Start();
			t3.Start();
			threads.Add(t1);
			threads.Add(t2);
			threads.Add(t3);
		}

		// Wait until all exit
		foreach (var thread in threads) thread.Join();
		return 42;
	}
}
