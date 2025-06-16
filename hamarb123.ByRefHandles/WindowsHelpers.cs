using System.Runtime.InteropServices;

namespace hamarb123.ByRefHandles;

internal static class WindowsHelpers
{
	[DllImport("API-MS-Win-Core-Synch-l1-2-0.dll")]
	private static unsafe extern void WakeByAddressSingle(void* Address);

	[DllImport("API-MS-Win-Core-Synch-l1-2-0.dll")]
	private static unsafe extern int WaitOnAddress(void* Address, void* CompareAddress, nuint AddressSize, int dwMilliseconds);

#if NET5_0_OR_GREATER
	public static bool IsWaitOnAddressSupported => OperatingSystem.IsWindowsVersionAtLeast(6, 2);
#else
	private static readonly bool _isWaitOnAddressSupported = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Environment.OSVersion.Version >= new Version(6, 2);
	public static bool IsWaitOnAddressSupported => _isWaitOnAddressSupported;
#endif

	private const int INFINITE = -1;

	public static unsafe void WaitOnAddress(uint* address, uint value)
	{
		while (true)
		{
			var actual = Volatile.Read(ref *address);
			if (actual != value) return;
			var result = WaitOnAddress(address, &value, 4, INFINITE);
			if (result != 0) continue;
		}
	}

	public static unsafe void WakeByAddress(uint* address)
	{
		WakeByAddressSingle(address);
	}
}
