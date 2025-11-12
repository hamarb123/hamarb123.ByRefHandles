using Microsoft.DotNet.RemoteExecutor;
using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace hamarb123.ByRefHandles.Test;

public class ByRefHandleTest
{
	private static RemoteInvokeOptions CreateRemoteInvokeOptions(bool large, bool smallHelperOnly)
	{
		// Run all tests under GCStress mode 0xF (unless it would take too long, in which case we use 0xE for those large cases).
		// See https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/jit/investigate-stress.md about gc stress mode.
		return new RemoteInvokeOptions()
		{
			TimeOut = RemoteExecutor.FailWaitTimeoutMilliseconds * (large ? 4 : 1),
			StartInfo = new ProcessStartInfo()
			{
				EnvironmentVariables =
				{
					{ "DOTNET_GCStress", large ? "0xE" : "0xF" },
				},
			},
			RuntimeConfigurationOptions =
			{
				{ "hamarb123.ByRefHandles.PinnedByRefHandle.OnlyUseSmallHelper", smallHelperOnly },
			},
		};
	}

	private static void Run(Func<int> func, bool large = false, bool smallHelperOnly = false)
	{
		RemoteExecutor.Invoke(func, CreateRemoteInvokeOptions(large, smallHelperOnly)).Dispose();
	}

	private static void Run(Func<Task<int>> func, bool large = false, bool smallHelperOnly = false)
	{
		RemoteExecutor.Invoke(func, CreateRemoteInvokeOptions(large, smallHelperOnly)).Dispose();
	}

	private static void Run(Func<string, int> func, string arg, bool large = false, bool smallHelperOnly = false)
	{
		RemoteExecutor.Invoke(func, arg, CreateRemoteInvokeOptions(large, smallHelperOnly)).Dispose();
	}

	public class AllocateFreeNull
	{
		[Fact]
		public void Test() => Run(Tests.AllocateFreeNull);
	}

#if NET9_0_OR_GREATER
	public class AllocateFreeRefStruct
	{
		[Fact]
		public void Test() => Run(Tests.AllocateFreeRefStruct);
	}
#endif

	public class AllocateFreeRefStructAsByte
	{
		[Fact]
		public void Test() => Run(Tests.AllocateFreeRefStructAsByte);
	}

	public class AllocateFreeLocal
	{
		[Fact]
		public void Test() => Run(Tests.AllocateFreeLocal);
	}

	public class AllocateFreeFAVT
	{
		[Fact]
		public void Test() => Run(Tests.AllocateFreeFAVT);
	}

	public class AllocateFreeBoxed
	{
		[Fact]
		public void Test() => Run(Tests.AllocateFreeBoxed);
	}

	public class AllocateFreeArray
	{
		[Fact]
		public void Test() => Run(Tests.AllocateFreeArray);
	}

	public class AllocateUpdateFreeArray
	{
		[Fact]
		public void Test() => Run(Tests.AllocateUpdateFreeArray);
	}

	public class AllocateFreeWeak
	{
		[Fact]
		public void Test() => Run(Tests.AllocateFreeWeak);
	}

	public class UseInAsync
	{
		[Fact]
		public void Test() => Run(Tests.UseInAsync);
	}

	public class UseInAsyncWithUpdate
	{
		[Fact]
		public void Test() => Run(Tests.UseInAsyncWithUpdate);
	}

	public abstract class AllocateUpdateFreeVariableBase(int arg, bool useTwoStageUpdate)
	{
		[Fact]
		public void Test() => Run((s) =>
		{
			var split = s.Split(' ');
			return Tests.AllocateUpdateFreeVariable(int.Parse(split[0], CultureInfo.InvariantCulture), bool.Parse(split[1]));
		}, arg.ToString(CultureInfo.InvariantCulture) + " " + useTwoStageUpdate.ToString(CultureInfo.InvariantCulture), arg >= 1024);
	}

	public sealed class AllocateUpdateFreeVariable_0() : AllocateUpdateFreeVariableBase(1 << 0, false);
	public sealed class AllocateUpdateFreeVariable_1() : AllocateUpdateFreeVariableBase(1 << 1, false);
	public sealed class AllocateUpdateFreeVariable_2() : AllocateUpdateFreeVariableBase(1 << 2, false);
	public sealed class AllocateUpdateFreeVariable_3() : AllocateUpdateFreeVariableBase(1 << 3, false);
	public sealed class AllocateUpdateFreeVariable_4() : AllocateUpdateFreeVariableBase(1 << 4, false);
	public sealed class AllocateUpdateFreeVariable_5() : AllocateUpdateFreeVariableBase(1 << 5, false);
	public sealed class AllocateUpdateFreeVariable_6() : AllocateUpdateFreeVariableBase(1 << 6, false);
	public sealed class AllocateUpdateFreeVariable_7() : AllocateUpdateFreeVariableBase(1 << 7, false);
	public sealed class AllocateUpdateFreeVariable_8() : AllocateUpdateFreeVariableBase(1 << 8, false);
	public sealed class AllocateUpdateFreeVariable_9() : AllocateUpdateFreeVariableBase(1 << 9, false);
	public sealed class AllocateUpdateFreeVariable_10() : AllocateUpdateFreeVariableBase(1 << 10, false);
	public sealed class AllocateUpdateFreeVariable_11() : AllocateUpdateFreeVariableBase(1 << 11, false);
	public sealed class AllocateUpdateFreeVariable_12() : AllocateUpdateFreeVariableBase(1 << 12, false);
	public sealed class AllocateUpdateFreeVariable_13() : AllocateUpdateFreeVariableBase(1 << 13, false);
	public sealed class AllocateUpdateFreeVariable_14() : AllocateUpdateFreeVariableBase(1 << 14, false);
	public sealed class AllocateUpdateFreeVariable_15() : AllocateUpdateFreeVariableBase(1 << 15, false);
	public sealed class AllocateUpdateFreeVariable_16() : AllocateUpdateFreeVariableBase(1 << 16, false);
	public sealed class AllocateUpdateFreeVariable_17() : AllocateUpdateFreeVariableBase(1 << 17, false);
	public sealed class AllocateUpdateFreeVariable_18() : AllocateUpdateFreeVariableBase(1 << 18, false);

	public sealed class AllocateTwoStageUpdateFreeVariable_0() : AllocateUpdateFreeVariableBase(1 << 0, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_1() : AllocateUpdateFreeVariableBase(1 << 1, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_2() : AllocateUpdateFreeVariableBase(1 << 2, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_3() : AllocateUpdateFreeVariableBase(1 << 3, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_4() : AllocateUpdateFreeVariableBase(1 << 4, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_5() : AllocateUpdateFreeVariableBase(1 << 5, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_6() : AllocateUpdateFreeVariableBase(1 << 6, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_7() : AllocateUpdateFreeVariableBase(1 << 7, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_8() : AllocateUpdateFreeVariableBase(1 << 8, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_9() : AllocateUpdateFreeVariableBase(1 << 9, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_10() : AllocateUpdateFreeVariableBase(1 << 10, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_11() : AllocateUpdateFreeVariableBase(1 << 11, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_12() : AllocateUpdateFreeVariableBase(1 << 12, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_13() : AllocateUpdateFreeVariableBase(1 << 13, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_14() : AllocateUpdateFreeVariableBase(1 << 14, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_15() : AllocateUpdateFreeVariableBase(1 << 15, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_16() : AllocateUpdateFreeVariableBase(1 << 16, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_17() : AllocateUpdateFreeVariableBase(1 << 17, true);
	public sealed class AllocateTwoStageUpdateFreeVariable_18() : AllocateUpdateFreeVariableBase(1 << 18, true);

	public class AllocateReinterpretFreeArray
	{
		[Fact]
		public void Test() => Run(Tests.AllocateReinterpretFreeArray);
	}

	public class AllocateFreeActuallyReleases
	{
		[Fact]
		public void Test() => Run(Tests.AllocateFreeActuallyReleases);
	}

	public class AllocateConvertFreeActuallyReleases
	{
		[Fact]
		public void Test() => Run(Tests.AllocateConvertFreeActuallyReleases);
	}

	public class ThreadedTests
	{
		[Fact]
		public void MultiThreadCreateDestroy() => Run(Tests.MultiThreadCreateDestroy);

		[Fact]
		public void MultiThreadCreateUpdateDestroy() => Run(Tests.MultiThreadCreateUpdateDestroy);

		[Fact]
		public void MultiThreadCreateUpdateDestroySmallHelperOnly() => Run(Tests.MultiThreadCreateUpdateDestroySmallHelperOnly, false, true);

		[Fact]
		public void MultiThreadCreateDestroy_LargeMode() => Run(Tests.MultiThreadCreateDestroy, true);

		[Fact]
		public void MultiThreadCreateUpdateDestroy_LargeMode() => Run(Tests.MultiThreadCreateUpdateDestroy, true);

		[Fact]
		public void MultiThreadCreateUpdateDestroySmallHelperOnly_LargeMode() => Run(Tests.MultiThreadCreateUpdateDestroySmallHelperOnly, true, true);
	}
}
