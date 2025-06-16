using System.ComponentModel;
using System.Runtime.InteropServices;

namespace hamarb123.ByRefHandles;

internal static class MacOSHelpers
{
	private const int ENOENT = 2;
	private const int EINTR = 4;
	private const int EFAULT = 14;

#if NET5_0_OR_GREATER
	public static bool IsWaitOnAddressSupported => OperatingSystem.IsMacOSVersionAtLeast(14, 4) || OperatingSystem.IsIOSVersionAtLeast(17, 4) || OperatingSystem.IsTvOSVersionAtLeast(17, 4) || OperatingSystem.IsWatchOSVersionAtLeast(10, 4)
#if NET6_0_OR_GREATER
		|| OperatingSystem.IsMacCatalystVersionAtLeast(14, 4)
#endif
		;
#else
	private static readonly bool _isWaitOnAddressSupported = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && Environment.OSVersion.Version >= new Version(14, 4);
	public static bool IsWaitOnAddressSupported => _isWaitOnAddressSupported;
#endif

	[DllImport("/usr/lib/libSystem.dylib", SetLastError = true)]
	private static unsafe extern int os_sync_wait_on_address(void* addr, ulong value, nuint size, uint flags);

	[DllImport("/usr/lib/libSystem.dylib", SetLastError = true)]
	private static unsafe extern int os_sync_wake_by_address_any(void* addr, nuint size, uint flags);

	private const int OS_SYNC_WAIT_ON_ADDRESS_NONE = 0;
	private const int OS_SYNC_WAKE_BY_ADDRESS_NONE = 0;

	public static unsafe void WaitOnAddress(uint* address, uint value)
	{
		while (true)
		{
			var actual = Volatile.Read(ref *address);
			if (actual != value) return;
			var result = os_sync_wait_on_address(address, value, 4, OS_SYNC_WAIT_ON_ADDRESS_NONE);
			if (result >= 0) continue;
			var error = Marshal.GetLastWin32Error();
			if (error is not (EINTR or EFAULT)) throw new Win32Exception(error);
		}
	}

	public static unsafe void WakeByAddress(uint* address)
	{
		if (os_sync_wake_by_address_any(address, 4, OS_SYNC_WAKE_BY_ADDRESS_NONE) < 0)
		{
			var error = Marshal.GetLastWin32Error();
			if (error != ENOENT) throw new Win32Exception(error);
		}
	}
}
