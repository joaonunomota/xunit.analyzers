using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Xunit.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MemberDataShouldReferenceValidMember : XunitDiagnosticAnalyzer
{
	public MemberDataShouldReferenceValidMember() :
		base(
			Descriptors.X1014_MemberDataShouldUseNameOfOperator,
			Descriptors.X1015_MemberDataMustReferenceExistingMember,
			Descriptors.X1016_MemberDataMustReferencePublicMember,
			Descriptors.X1017_MemberDataMustReferenceStaticMember,
			Descriptors.X1018_MemberDataMustReferenceValidMemberKind,
			Descriptors.X1019_MemberDataMustReferenceMemberOfValidType,
			Descriptors.X1020_MemberDataPropertyMustHaveGetter,
			Descriptors.X1021_MemberDataNonMethodShouldNotHaveParameters
		)
	{ }

	public override void AnalyzeCompilation(
		CompilationStartAnalysisContext context,
		XunitContext xunitContext)
	{
		var compilation = context.Compilation;

		var supportsNameofOperator =
			compilation is CSharpCompilation cSharpCompilation
			&& cSharpCompilation.LanguageVersion >= LanguageVersion.CSharp6;

		context.RegisterSyntaxNodeAction(context =>
		{
			if (xunitContext.Core.MemberDataAttributeType is null)
				return;

			if (context.Node is not AttributeSyntax attribute)
				return;

			var memberNameArgument = attribute.ArgumentList?.Arguments.FirstOrDefault();
			if (memberNameArgument is null)
				return;

			var semanticModel = context.SemanticModel;
			if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetTypeInfo(attribute, context.CancellationToken).Type, xunitContext.Core.MemberDataAttributeType))
				return;

			if (attribute.ArgumentList is null)
				return;

			var propertyAttributeParameters =
				attribute
					.ArgumentList
					.Arguments
					.Count(a => !string.IsNullOrEmpty(a.NameEquals?.Name.Identifier.ValueText));

			var paramsCount = attribute.ArgumentList.Arguments.Count - 1 - propertyAttributeParameters;

			var constantValue = semanticModel.GetConstantValue(memberNameArgument.Expression, context.CancellationToken);
			if (constantValue.Value is not string memberName)
				return;

			var memberTypeArgument = attribute.ArgumentList.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == Constants.AttributeProperties.MemberType);
			var memberTypeSymbol = default(ITypeSymbol);
			if (memberTypeArgument?.Expression is TypeOfExpressionSyntax typeofExpression)
			{
				var typeSyntax = typeofExpression.Type;
				memberTypeSymbol = semanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type;
			}

			var classSyntax = attribute.FirstAncestorOrSelf<ClassDeclarationSyntax>();
			if (classSyntax is null)
				return;

			var testClassTypeSymbol = semanticModel.GetDeclaredSymbol(classSyntax);
			if (testClassTypeSymbol is null)
				return;

			var declaredMemberTypeSymbol = memberTypeSymbol ?? testClassTypeSymbol;
			if (declaredMemberTypeSymbol is null)
				return;

			var memberSymbol = FindMemberSymbol(memberName, declaredMemberTypeSymbol, paramsCount);

			if (memberSymbol is null)
				ReportMissingMember(context, attribute, memberName, declaredMemberTypeSymbol);
			else if (memberSymbol.Kind != SymbolKind.Field && memberSymbol.Kind != SymbolKind.Property && memberSymbol.Kind != SymbolKind.Method)
				ReportIncorrectMemberType(context, attribute);
			else
			{
				if (supportsNameofOperator && memberNameArgument.Expression.IsKind(SyntaxKind.StringLiteralExpression))
					ReportUseNameof(context, memberNameArgument, memberName, testClassTypeSymbol, memberSymbol);

				var memberProperties = new Dictionary<string, string?>
				{
					{ Constants.AttributeProperties.DeclaringType, declaredMemberTypeSymbol.ToDisplayString() },
					{ Constants.AttributeProperties.MemberName, memberName }
				}.ToImmutableDictionary();

				if (memberSymbol.DeclaredAccessibility != Accessibility.Public)
					ReportNonPublicAccessibility(context, attribute, memberProperties);

				if (!memberSymbol.IsStatic)
					ReportNonStatic(context, attribute, memberProperties);

				var memberType = memberSymbol switch
				{
					IPropertySymbol prop => prop.Type,
					IFieldSymbol field => field.Type,
					IMethodSymbol method => method.ReturnType,
					_ => null,
				};

				if (memberType is not null)
				{
					var iEnumerableOfObjectArrayType = TypeSymbolFactory.IEnumerableOfObjectArray(compilation);
					var iEnumerableOfTheoryDataRowType = TypeSymbolFactory.IEnumerableOfITheoryDataRow(compilation);
					var valid = iEnumerableOfObjectArrayType.IsAssignableFrom(memberType);

					if (!valid && xunitContext.HasV3References)
					{
						if (iEnumerableOfTheoryDataRowType is not null)
							valid = iEnumerableOfTheoryDataRowType.IsAssignableFrom(memberType);
					}

					if (!valid)
						ReportIncorrectReturnType(context, iEnumerableOfObjectArrayType, iEnumerableOfTheoryDataRowType, attribute, memberProperties, memberType);
				}

				if (memberSymbol.Kind == SymbolKind.Property && memberSymbol.DeclaredAccessibility == Accessibility.Public)
					if (memberSymbol is IPropertySymbol propertySymbol)
					{
						var getMethod = propertySymbol.GetMethod;
						if (getMethod is null || getMethod.DeclaredAccessibility != Accessibility.Public)
							ReportNonPublicPropertyGetter(context, attribute);
					}

				var extraArguments = attribute.ArgumentList.Arguments.Skip(1).TakeWhile(a => a.NameEquals is null).ToList();
				if (memberSymbol.Kind == SymbolKind.Property || memberSymbol.Kind == SymbolKind.Field)
					if (extraArguments.Any())
						ReportIllegalNonMethodArguments(context, attribute, extraArguments);

				// TODO: handle method parameter type matching, model after InlineDataMustMatchTheoryParameter
				//if (memberSymbol.Kind == SymbolKind.Method)
				//{
				//}
			}
		}, SyntaxKind.Attribute);
	}

	static ISymbol? FindMemberSymbol(
		string memberName,
		ITypeSymbol? type,
		int paramsCount)
	{
		if (paramsCount > 0 && FindMethodSymbol(memberName, type, paramsCount) is ISymbol methodSymbol)
			return methodSymbol;

		while (type is not null)
		{
			var memberSymbol = type.GetMembers(memberName).FirstOrDefault();
			if (memberSymbol is not null)
				return memberSymbol;

			type = type.BaseType;
		}

		return null;
	}

	static ISymbol? FindMethodSymbol(
		string memberName,
		ITypeSymbol? type,
		int paramsCount)
	{
		while (type is not null)
		{
			var methodSymbol =
				type
					.GetMembers(memberName)
					.OfType<IMethodSymbol>()
					.FirstOrDefault(x => x.Parameters.Length == paramsCount);

			if (methodSymbol is not null)
				return methodSymbol;

			type = type.BaseType;
		}

		return null;
	}

	static void ReportIllegalNonMethodArguments(
		SyntaxNodeAnalysisContext context,
		AttributeSyntax attribute,
		List<AttributeArgumentSyntax> extraArguments)
	{
		var span = TextSpan.FromBounds(extraArguments.First().Span.Start, extraArguments.Last().Span.End);

		context.ReportDiagnostic(
			Diagnostic.Create(
				Descriptors.X1021_MemberDataNonMethodShouldNotHaveParameters,
				Location.Create(attribute.SyntaxTree, span)
			)
		);
	}

	static void ReportIncorrectMemberType(
		SyntaxNodeAnalysisContext context,
		AttributeSyntax attribute) =>
			context.ReportDiagnostic(
				Diagnostic.Create(
					Descriptors.X1018_MemberDataMustReferenceValidMemberKind,
					attribute.GetLocation()
				)
			);

	static void ReportIncorrectReturnType(
		SyntaxNodeAnalysisContext context,
		INamedTypeSymbol iEnumerableOfObjectArrayType,
		INamedTypeSymbol? iEnumerableOfTheoryDataRowType,
		AttributeSyntax attribute,
		ImmutableDictionary<string, string?> memberProperties,
		ITypeSymbol memberType)
	{
		var validSymbols = "'" + SymbolDisplay.ToDisplayString(iEnumerableOfObjectArrayType) + "'";

		if (iEnumerableOfTheoryDataRowType is not null)
			validSymbols += " or '" + SymbolDisplay.ToDisplayString(iEnumerableOfTheoryDataRowType) + "'";

		context.ReportDiagnostic(
			Diagnostic.Create(
				Descriptors.X1019_MemberDataMustReferenceMemberOfValidType,
				attribute.GetLocation(),
				memberProperties,
				validSymbols,
				SymbolDisplay.ToDisplayString(memberType)
			)
		);
	}

	static void ReportMissingMember(
		SyntaxNodeAnalysisContext context,
		AttributeSyntax attribute,
		string memberName,
		ITypeSymbol declaredMemberTypeSymbol) =>
			context.ReportDiagnostic(
				Diagnostic.Create(
					Descriptors.X1015_MemberDataMustReferenceExistingMember,
					attribute.GetLocation(),
					memberName,
					SymbolDisplay.ToDisplayString(declaredMemberTypeSymbol)
				)
			);

	static void ReportNonPublicAccessibility(
		SyntaxNodeAnalysisContext context,
		AttributeSyntax attribute,
		ImmutableDictionary<string, string?> memberProperties) =>
			context.ReportDiagnostic(
				Diagnostic.Create(
					Descriptors.X1016_MemberDataMustReferencePublicMember,
					attribute.GetLocation(),
					memberProperties
				)
			);

	static void ReportNonPublicPropertyGetter(
		SyntaxNodeAnalysisContext context,
		AttributeSyntax attribute) =>
			context.ReportDiagnostic(
				Diagnostic.Create(
					Descriptors.X1020_MemberDataPropertyMustHaveGetter,
					attribute.GetLocation()
				)
			);

	static void ReportNonStatic(
		SyntaxNodeAnalysisContext context,
		AttributeSyntax attribute,
		ImmutableDictionary<string, string?> memberProperties) =>
			context.ReportDiagnostic(
				Diagnostic.Create(
					Descriptors.X1017_MemberDataMustReferenceStaticMember,
					attribute.GetLocation(),
					memberProperties
				)
			);

	static void ReportUseNameof(
		SyntaxNodeAnalysisContext context,
		AttributeArgumentSyntax memberNameArgument,
		string memberName,
		INamedTypeSymbol testClassTypeSymbol,
		ISymbol memberSymbol)
	{
		var builder = ImmutableDictionary.CreateBuilder<string, string?>();
		if (!SymbolEqualityComparer.Default.Equals(memberSymbol.ContainingType, testClassTypeSymbol))
			builder.Add("DeclaringType", memberSymbol.ContainingType.ToDisplayString());

		context.ReportDiagnostic(
			Diagnostic.Create(
				Descriptors.X1014_MemberDataShouldUseNameOfOperator,
				memberNameArgument.Expression.GetLocation(),
				builder.ToImmutable(),
				memberName,
				memberSymbol.ContainingType.ToDisplayString()
			)
		);
	}
}
