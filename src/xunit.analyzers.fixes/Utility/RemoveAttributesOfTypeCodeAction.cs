using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Xunit.Analyzers.Fixes;

public class RemoveAttributesOfTypeCodeAction : CodeAction
{
	readonly SyntaxList<AttributeListSyntax> attributeLists;
	readonly string attributeType;
	readonly Document document;
	readonly bool exactMatch;

	public RemoveAttributesOfTypeCodeAction(
		string title,
		string equivalenceKey,
		Document document,
		SyntaxList<AttributeListSyntax> attributeLists,
		string attributeType,
		bool exactMatch = false)
	{
		Title = title;
		EquivalenceKey = equivalenceKey;

		this.attributeLists = attributeLists;
		this.attributeType = attributeType;
		this.document = document;
		this.exactMatch = exactMatch;
	}

	public override string EquivalenceKey { get; }

	public override string Title { get; }

	protected override async Task<Document> GetChangedDocumentAsync(CancellationToken cancellationToken)
	{
		var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
		var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

		if (semanticModel is not null)
		{
			var dataAttributeType = semanticModel.Compilation.GetTypeByMetadataName(attributeType);

			if (dataAttributeType is not null)
				foreach (var attributeList in attributeLists)
					foreach (var attribute in attributeList.Attributes)
						if (dataAttributeType.IsAssignableFrom(semanticModel.GetTypeInfo(attribute, cancellationToken).Type, exactMatch))
							editor.RemoveNode(attribute);
		}

		return editor.GetChangedDocument();
	}
}
