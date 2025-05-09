using Microsoft.CodeAnalysis;
using Xunit.Analyzers;

namespace Xunit.Suppressors;

/// <summary>
/// Base class for diagnostic suppressors which support xUnit.net v2 only.
/// </summary>
public abstract class XunitV2DiagnosticSuppressor(SuppressionDescriptor descriptor) :
	XunitDiagnosticSuppressor(descriptor)
{
	protected override bool ShouldAnalyze(XunitContext xunitContext) =>
		Guard.ArgumentNotNull(xunitContext).HasV2References;
}
