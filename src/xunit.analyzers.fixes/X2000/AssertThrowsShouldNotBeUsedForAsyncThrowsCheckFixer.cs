using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Xunit.Analyzers.Fixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public class AssertThrowsShouldNotBeUsedForAsyncThrowsCheckFixer : BatchedCodeFixProvider
{
	public const string Key_UseAlternateAssert = "xUnit2014_UseAlternateAssert";

	public AssertThrowsShouldNotBeUsedForAsyncThrowsCheckFixer() :
		base(Descriptors.X2014_AssertThrowsShouldNotBeUsedForAsyncThrowsCheck.Id)
	{ }

	public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root is null)
			return;

		var invocation = root.FindNode(context.Span).FirstAncestorOrSelf<InvocationExpressionSyntax>();
		if (invocation is null)
			return;

		var method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
		if (method is null)
			return;

		var diagnostic = context.Diagnostics.FirstOrDefault();
		if (diagnostic is null)
			return;
		if (!diagnostic.Properties.TryGetValue(Constants.Properties.MethodName, out var methodName))
			return;
		if (!diagnostic.Properties.TryGetValue(Constants.Properties.Replacement, out var replacement))
			return;
		if (replacement is null)
			return;

		context.RegisterCodeFix(
			CodeAction.Create(
				string.Format("Use Assert.{0}", replacement),
				ct => UseAsyncThrowsCheck(context.Document, invocation, method, replacement, ct),
				Key_UseAlternateAssert
			),
			context.Diagnostics
		);
	}

	static async Task<Document> UseAsyncThrowsCheck(
		Document document,
		InvocationExpressionSyntax invocation,
		MethodDeclarationSyntax method,
		string replacement,
		CancellationToken cancellationToken)
	{
		var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

		if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
		{
			var modifiers = GetModifiersWithAsyncKeywordAdded(method);
			var returnType = await GetReturnType(method, invocation, document, editor, cancellationToken);
			var asyncThrowsInvocation = GetAsyncThrowsInvocation(invocation, replacement, memberAccess);

			if (returnType is not null)
				editor.ReplaceNode(
					method,
					method
						.ReplaceNode(invocation, asyncThrowsInvocation)
						.WithModifiers(modifiers)
						.WithReturnType(returnType)
				);
		}

		return editor.GetChangedDocument();
	}

	static SyntaxTokenList GetModifiersWithAsyncKeywordAdded(MethodDeclarationSyntax method) =>
		method.Modifiers.Any(SyntaxKind.AsyncKeyword)
			? method.Modifiers
			: method.Modifiers.Add(Token(SyntaxKind.AsyncKeyword));

	static async Task<TypeSyntax?> GetReturnType(
		MethodDeclarationSyntax method,
		InvocationExpressionSyntax invocation,
		Document document,
		DocumentEditor editor,
		CancellationToken cancellationToken)
	{
		// Consider the case where a custom awaiter type is awaited
		if (invocation.Parent.IsKind(SyntaxKind.AwaitExpression))
			return method.ReturnType;

		var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
		if (semanticModel is null)
			return null;

		var methodSymbol = semanticModel.GetSymbolInfo(method.ReturnType, cancellationToken).Symbol as ITypeSymbol;
		var taskType = TypeSymbolFactory.Task(semanticModel.Compilation);
		if (taskType is null)
			return null;

		if (taskType.IsAssignableFrom(methodSymbol))
			return method.ReturnType;

		return editor.Generator.TypeExpression(taskType) as TypeSyntax;
	}

	static ExpressionSyntax GetAsyncThrowsInvocation(
		InvocationExpressionSyntax invocation,
		string memberName,
		MemberAccessExpressionSyntax memberAccess)
	{
		var asyncThrowsInvocation =
			invocation
				.WithExpression(memberAccess.WithName(GetName(memberName, memberAccess)))
				.WithArgumentList(invocation.ArgumentList);

		if (invocation.Parent.IsKind(SyntaxKind.AwaitExpression))
			return asyncThrowsInvocation;

		return
			AwaitExpression(asyncThrowsInvocation.WithoutLeadingTrivia())
				.WithLeadingTrivia(invocation.GetLeadingTrivia());
	}

	static SimpleNameSyntax GetName(
		string memberName,
		MemberAccessExpressionSyntax memberAccess)
	{
		if (memberAccess.Name is not GenericNameSyntax genericNameSyntax)
			return IdentifierName(memberName);

		return GenericName(IdentifierName(memberName).Identifier, genericNameSyntax.TypeArgumentList);
	}
}
