using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace hamarb123.ByRefHandles;

public struct PinnedByRefHandle<T> : IDisposable, IEquatable<PinnedByRefHandle<T>>
#if NET9_0_OR_GREATER
	where T : allows ref struct
#endif
{
	// Internal values
	internal Impl? _impl;
	internal ushort _slot;

	/// <summary>
	/// Allocates a handle pinned by-reference handle for the specified by-reference.
	/// </summary>
	/// <param name="value">The by-reference to store in the handle.</param>
	public PinnedByRefHandle(ref T value) => (_impl, _slot) = Impl.Create(ref Unsafe.As<T, byte>(ref value));

	/// <inheritdoc cref="GCHandle.IsAllocated" />
	public readonly bool IsAllocated => _impl != null;

	/// <summary>
	/// Gets the pointer we're currently pinning.
	/// </summary>
	/// <exception cref="NullReferenceException">If the handle is not initialized or already disposed.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe readonly T* GetTargetPointer() => (T*)_impl!.GetPointerBySlot(_slot);

	/// <summary>
	/// Gets the by-reference we're currently pinning.
	/// </summary>
	/// <exception cref="NullReferenceException">If the handle is not initialized or already disposed.</exception>
	public unsafe readonly ref T Target
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => ref *GetTargetPointer();
	}

	/// <summary>
	/// Sets the by-reference we're currently pinning.
	/// </summary>
	/// <exception cref="NullReferenceException">If the handle is not initialized or already disposed.</exception>
	/// <param name="value">The by-reference to store in the handle.</param>
	/// <remarks>This method is not thread safe.</remarks>
	public readonly void SetTarget(ref T value) => _impl!.Update(_slot, ref Unsafe.As<T, byte>(ref value),
#if NET9_0_OR_GREATER
		!typeof(T).IsByRefLike &&
#endif
		!Unsafe.IsNullRef(ref value));

	/// <summary>
	/// Similar to <see cref="SetTarget(ref T)" />, but doesn't keep memory pinned or alive while helper thread stores it.
	/// The way to detect whether the target has been pinned yet or not is <see cref="EndSetTarget" />, but APIs like <see cref="Target" /> should still work before that.
	/// This method is very hard to use correctly, as <see cref="SetTarget(ref T)" /> already optimizes cases like <see langword="null" /> byrefs and <see langword="ref" /> <see langword="struct" />s as known to be already pinned and kept alive (by address escaping).
	/// </summary>
	/// <exception cref="NullReferenceException">If the handle is not initialized or already disposed.</exception>
	/// <param name="value">The by-reference to store in the handle.</param>
	/// <remarks>This method is not thread safe.</remarks>
	public readonly void BeginSetTarget(ref T value) => _impl!.Update(_slot, ref Unsafe.As<T, byte>(ref value), false);

	/// <summary>
	/// Complimentary method to <see cref="BeginSetTarget(ref T)" />, ensures the operation that began there has completed.
	/// </summary>
	/// <exception cref="NullReferenceException">If the handle is not initialized or already disposed.</exception>
	/// <remarks>This method is not thread safe.</remarks>
	public readonly void EndSetTarget() => _impl!.EndUpdate(_slot);

	/// <summary>
	/// Reinterprets the current instance with a new type parameter. Doesn't free the existing instance, but does zero it - you can make a copy of the original if you want to be able to access via both simultaneously, but note that both would use the same slot (so should only be freed once).
	/// </summary>
	public PinnedByRefHandle<TNew> UnsafeReinterpret<TNew>()
#if NET9_0_OR_GREATER
		where TNew : allows ref struct
#endif
	{
		PinnedByRefHandle<TNew> result = new() { _impl = _impl, _slot = _slot };
		this = default;
		return result;
	}

	/// <summary>
	/// Releases this handle.
	/// </summary>
	/// <remarks>This method is not thread safe.</remarks>s
	public void Dispose()
	{
		if (_impl != null)
		{
			Impl.Destroy(_impl, _slot);
			this = default;
		}
	}

#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
	/// <inheritdoc />
	public readonly override bool Equals([NotNullWhen(true)] object? obj) => obj is PinnedByRefHandle<T> handle && Equals(handle);
#else
#nullable disable
	/// <inheritdoc />
	public readonly override bool Equals(object obj) => obj is PinnedByRefHandle<T> handle && Equals(handle);
#nullable restore
#endif

	/// <inheritdoc cref="IEquatable{T}.Equals(T)" />
	public readonly bool Equals(PinnedByRefHandle<T> other) => _impl == other._impl && _slot == other._slot;

	/// <summary>
	/// Returns the hash code for the current instance.
	/// </summary>
	/// <returns>A hash code for the current instance.</returns>
	public readonly override int GetHashCode() => HashCode.Combine(_impl, _slot);

	/// <summary>
	/// Converts <paramref name="value" /> to an implementation defined internal representation.
	/// Note: it results in undefined behaviour to do anything to the <see langword="object" /> returned.
	/// </summary>
	public static (object?, nuint, nuint) ToInternalRepresentation(PinnedByRefHandle<T> value) => (value._impl, value._slot, 0);

	/// <summary>
	/// Converts from the implementation defined internal representation specified by <paramref name="internalRepr" /> to the handle instance.
	/// Note: it results in undefined behaviour to provide an <paramref name="internalRepr" /> not returned by <see cref="ToInternalRepresentation(PinnedByRefHandle{T})" />, or to use (in any way) an instance created from this API if the handle has already been freed.
	/// </summary>
	public static PinnedByRefHandle<T> FromInternalRepresentation((object?, nuint, nuint) internalRepr) => new() { _impl = Unsafe.As<Impl?>(internalRepr.Item1), _slot = (ushort)internalRepr.Item2 };
}
