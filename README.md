# hamarb123.ByRefHandles
A .NET handle type similar to PinnedGCHandle for byrefs.

NuGet link:
[![NuGet version (hamarb123.ByRefHandles)](https://img.shields.io/nuget/v/hamarb123.ByRefHandles.svg?style=flat-square)](https://www.nuget.org/packages/hamarb123.ByRefHandles/)

## !!! DISCLAIMER !!!
This library is ***extremely `unsafe`***.

If you are not an expert in `unsafe` code (in particular lifetimes, pinning, `ref` rules, finalizers, multi-threading, and ECMA-335) and the [.NET Memory Model](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Memory-model.md) (in particular how it is UB to access managed references located on another thread's stack), you should NOT be using this library, as you will likely use it incorrectly on accident, which can lead to severe issues.

It is dangerous because it allows you to completely bypass any safety C# usually gives you, in particular lifetimes and cross-threaded access, as you are expected to validate your usage yourself. In particular, it conceptually allows you to store a byref on the heap, which means that your byref can escape any expressible lifetime, and allows you to break any cross-threading limitations that would otherwise be imposed. The API surface is also (intentionally) not thread-safe, so using the API incorrectly in multi-threading scenarios can produce nasty bugs. Using the API incorrectly in general, such as by double freeing, etc., can also lead to nasty bugs too, so you must be extremely careful to not use it incorrectly.

The library is tested in [GC Hole Stress](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/jit/investigate-stress.md) mode, but I would still not recommend using this in production code - you should just design your API better to not need to keep `ref`s alive like this, or use other (less sketchy) workarounds where possible.

If you don't understand the concerns I laid out above, then you probably shouldn't be using this library.

## Configuration

The following `AppContext` variables are available (primarily, to enable better trimming if desired):
- `hamarb123.ByRefHandles.PinnedByRefHandle.OnlyUseSmallHelper` (`bool`): when set to `true`, only uses the smallest helper (64 slots currently)
- `hamarb123.ByRefHandles.PinnedByRefHandle.OnlyUseMediumHelper` (`bool`): when set to `true`, only uses the medium-sized helper (4096 slots currently)

## Example Usage:
```csharp
using hamarb123.ByRefHandles;
using System.Runtime.InteropServices;

Main();

static void Main()
{
	Span<int> spanOnHeap = (Span<int>)(int[])[1, 2, 3, 4];
	UseSpanInAsync(spanOnHeap).GetAwaiter().GetResult();
	Console.WriteLine(string.Join(", ", spanOnHeap.ToArray()));
	// Output: 21, 2, 42, 42
}

static Task UseSpanInAsync(Span<int> x)
{
	PinnedByRefHandleHolder<int> holder = new(ref MemoryMarshal.GetReference(x));
	return Impl(holder, x.Length);
	static async Task Impl(PinnedByRefHandleHolder<int> holder, int length)
	{
		// Async boundary
		await Task.Yield();

		// Fill second half of span with 42
		ref var reference = ref holder.Handle.Target;
		GC.KeepAlive(holder);
		Span<int> sp = MemoryMarshal.CreateSpan(ref reference, length);
		sp[(sp.Length / 2)..].Fill(42);

		// Async boundary
		await Task.Yield();

		// Set first element to 21
		reference = ref holder.Handle.Target;
		GC.KeepAlive(holder);
		MemoryMarshal.CreateSpan(ref reference, length)[0] = 21;

		// Clean up
		holder.Dispose();
	}
}

// To ensure it gets cleaned up if we don't dispose it
sealed class PinnedByRefHandleHolder<T>(ref T reference)
{
	public PinnedByRefHandle<T> Handle = new(ref reference);

	public void Dispose()
	{
		Handle.Dispose();
		GC.SuppressFinalize(this);
	}

	~PinnedByRefHandleHolder()
	{
		Handle.Dispose();
	}
}
```
