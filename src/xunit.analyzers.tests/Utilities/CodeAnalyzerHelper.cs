﻿using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Testing;

static class CodeAnalyzerHelper
{
	public static ReferenceAssemblies CurrentXunit { get; }

	static CodeAnalyzerHelper()
	{
		CurrentXunit = ReferenceAssemblies.Default.AddPackages(
			ImmutableArray.Create(
				new PackageIdentity("System.Collections.Immutable", "1.6.0"),
				new PackageIdentity("xunit.assert", "2.4.1"),
				new PackageIdentity("xunit.core", "2.4.1")
			)
		);
	}
}
