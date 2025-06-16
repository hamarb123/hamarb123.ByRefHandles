#include "version.h"

.assembly hamarb123.ByRefHandles.ILHelpers
{
	.custom instance void [CORE_ASSEMBLY]System.Runtime.CompilerServices.InternalsVisibleToAttribute::.ctor(string) = { string('hamarb123.ByRefHandles') }
	.custom instance void [CORE_ASSEMBLY]System.Runtime.CompilerServices.CompilationRelaxationsAttribute::.ctor(int32) = { int32(8) }
	.custom instance void [CORE_ASSEMBLY]System.Runtime.CompilerServices.RuntimeCompatibilityAttribute::.ctor() = { property bool 'WrapNonExceptionThrows' = bool(true) }

#ifdef NET7_0_OR_GREATER
	.custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyMetadataAttribute::.ctor(string, string) = { string('IsTrimmable') string('True') }
#endif

	.custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyFileVersionAttribute::.ctor(string) = ASSEMBLY_FILE_VERSION
	.custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyInformationalVersionAttribute::.ctor(string) = ASSEMBLY_INFORMATIONAL_VERSION
	.custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyTitleAttribute::.ctor(string) = ASSEMBLY_TITLE
	.custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyDescriptionAttribute::.ctor(string) = ASSEMBLY_DESCRIPTION
	.custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyCopyrightAttribute::.ctor(string) = ASSEMBLY_COPYRIGHT
	.custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyCompanyAttribute::.ctor(string) = ASSEMBLY_COMPANY
	.custom instance void [CORE_ASSEMBLY]System.Reflection.AssemblyProductAttribute::.ctor(string) = ASSEMBLY_PRODUCT

	.hash algorithm 0x00008004
	.ver ASSEMBLY_VERSION
}

.module hamarb123.ByRefHandles.ILHelpers.dll
.imagebase 0x00400000
.file alignment 0x00000200
.stackreserve 0x00100000
.subsystem 0x0003 // WindowsCui
.corflags 0x00000001 // ILOnly

.class private auto ansi '<Module>'
{
}

#include "PinnedByRefHandleHelpers.generated.il"
