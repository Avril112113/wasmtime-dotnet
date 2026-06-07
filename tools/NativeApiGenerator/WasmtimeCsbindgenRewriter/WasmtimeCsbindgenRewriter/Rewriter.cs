using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GenerateNativeWasmtimeDocs;

public class Rewriter : CSharpSyntaxRewriter
{
	private readonly string _xmlDirPath;
	private readonly Dictionary<string, XElement> _compoundLookup;
	private readonly Dictionary<string, XElement> _memberDefs = new();
	
	public Rewriter(string xmlDirPath)
	{
		_xmlDirPath = xmlDirPath;
		var indexDocument = XDocument.Load(Path.Combine(xmlDirPath, "index.xml")).Root!;
		if (indexDocument.Name != "doxygenindex")
			throw new InvalidDataException("Expected first root of index.xml to be doxygenindex");
		
		var fileCompounds = indexDocument
			.Elements("compound")
			// Only process files, which excludes any C++ stuff.
			.Where(compound => compound.Attribute("kind")?.Value is "file")
			.Elements("member")
			.Elements("name")
			// If we found duplicates, but refer to the same refid, just pick either.
			.GroupBy(x => x.Value)
				.Where(group => group.All(item => item.Attribute("refid")?.Value == group.First().Attribute("refid")?.Value))
				.Select(group => group.First())
			.ToDictionary(x => x.Value, x => x);
		
		var structCompounds = indexDocument
			.Elements("compound")
			// Only process files, which excludes any C++ stuff.
			.Where(compound => compound.Attribute("kind")?.Value is "struct")
			.Elements("member")
			.Elements("name")
			.GroupBy(x => $"{x.Parent!.Parent!.Element("name")!.Value}.{x.Value}")
				.Where(group => group.All(item => item.Attribute("refid")?.Value == group.First().Attribute("refid")?.Value))
				.Select(group => group.First())
			.ToDictionary(x => $"{x.Parent!.Parent!.Element("name")!.Value}.{x.Value}", x => x);

		_compoundLookup = fileCompounds.Concat(structCompounds).ToDictionary();
	}

	private XElement? GetInfoByName(string name)
	{
		if (!_compoundLookup.TryGetValue(name, out var element))
			return null;

		var memberElem = element.Parent;
		var memberRefId = memberElem?.Attribute("refid")?.Value ?? throw new InvalidDataException("Failed to get member refid, invalid xml structure.");

		if (_memberDefs.TryGetValue(memberRefId, out var info)) {
			return info;
		}
		
		var compoundElem = memberElem?.Parent;
		var compoundRefId = compoundElem?.Attribute("refid")?.Value ?? throw new InvalidDataException("Failed to get compound refid, invalid xml structure.");

		var compoundFilePath = Path.Combine(_xmlDirPath, $"{compoundRefId}.xml");
		if (!File.Exists(compoundFilePath)) {
			Console.Error.WriteLine($"Could not find compound file at {compoundFilePath}");
			return null;
		}

		var compoundDocument = XDocument.Load(compoundFilePath).Root!;
		if (compoundDocument.Name != "doxygen")
			throw new InvalidDataException($"Expected root node of {compoundFilePath} to be doxygen");

		foreach (var memberdef in compoundDocument.Elements("compounddef").Elements("sectiondef").Elements("memberdef")) {
			_memberDefs.Add(memberdef.Attribute("id")!.Value, memberdef);
		}
		
		if (_memberDefs.TryGetValue(memberRefId, out var newInfo)) {
			return newInfo;
		}

		// This is an error, as we expect the value at this point and if it doesn't exist, it would reach this point over and over again.
		throw new InvalidDataException($"Failed to get member {name}, it should have been found in {compoundFilePath}");
	}

	private void InfoToDocComment(XElement info, List<SyntaxTrivia> newLeadingTrivia, SyntaxTrivia indent)
	{
		var originalCount = newLeadingTrivia.Count;
		
		if (info.Element("briefdescription") is { Value.Length: > 0 } brief) {
			foreach (var lineSpan in brief.Value.Trim().EnumerateLines()) {
				var line = lineSpan.ToString();
				newLeadingTrivia.Add(indent);
				newLeadingTrivia.Add(SyntaxFactory.Comment($"/// {line}"));
				newLeadingTrivia.Add(SyntaxFactory.ElasticLineFeed);
			}
		}
			
		if (info.Element("detaileddescription") is { Value.Length: > 0 } detail) {
			if (newLeadingTrivia.Count > 0) {
				newLeadingTrivia.Add(indent);
				newLeadingTrivia.Add(SyntaxFactory.Comment("///"));
				newLeadingTrivia.Add(SyntaxFactory.ElasticLineFeed);
			}
				
			foreach (var lineSpan in detail.Value.Trim().EnumerateLines()) {
				var line = lineSpan.ToString();
				newLeadingTrivia.Add(indent);
				newLeadingTrivia.Add(SyntaxFactory.Comment($"/// {line}"));
				newLeadingTrivia.Add(SyntaxFactory.ElasticLineFeed);
			}
		}

		if (newLeadingTrivia.Count != originalCount) {
			newLeadingTrivia.Insert(originalCount, indent);
			newLeadingTrivia.Insert(originalCount+1, SyntaxFactory.Comment("/// <summary>"));
			newLeadingTrivia.Insert(originalCount+2, SyntaxFactory.ElasticLineFeed);
			newLeadingTrivia.Add(indent);
			newLeadingTrivia.Add(SyntaxFactory.Comment("/// </summary>"));
			newLeadingTrivia.Add(SyntaxFactory.ElasticLineFeed);
		}
	}

	public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
	{
		var info = GetInfoByName(node.Identifier.Text);
		if (info != null) {
			var existingLeadingTrivia = node.GetLeadingTrivia();
			var indent = existingLeadingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
			
			var newLeadingTrivia = new List<SyntaxTrivia>();
			InfoToDocComment(info, newLeadingTrivia, indent);
			node = node.WithLeadingTrivia(existingLeadingTrivia.InsertRange(existingLeadingTrivia.Count - 1, newLeadingTrivia));
		}
		
		return base.VisitStructDeclaration(node);
	}

	public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
	{
		if (node.Parent is TypeDeclarationSyntax parentContainer) {
			var fieldName = node.Declaration.Variables.First().Identifier.Text;
			var info = GetInfoByName($"{parentContainer.Identifier.Text}.{fieldName}");
			if (info != null) {
				var existingLeadingTrivia = node.GetLeadingTrivia();
				var indent = existingLeadingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
			
				var newLeadingTrivia = new List<SyntaxTrivia>();
				InfoToDocComment(info, newLeadingTrivia, indent);
				node = node.WithLeadingTrivia(existingLeadingTrivia.InsertRange(existingLeadingTrivia.Count - 1, newLeadingTrivia));
			}
		}
		
		return base.VisitFieldDeclaration(node);
	}

	public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
	{
		var info = GetInfoByName(node.Identifier.Text);
		if (info != null) {
			var existingLeadingTrivia = node.GetLeadingTrivia();
			var indent = existingLeadingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
			
			var newLeadingTrivia = new List<SyntaxTrivia>();
			InfoToDocComment(info, newLeadingTrivia, indent);
			node = node.WithLeadingTrivia(existingLeadingTrivia.InsertRange(existingLeadingTrivia.Count - 1, newLeadingTrivia));
		}
		
		return base.VisitPropertyDeclaration(node);
	}

	public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
	{
		var info = GetInfoByName(node.Identifier.Text);
		if (info != null) {
			var existingLeadingTrivia = node.GetLeadingTrivia();
			var indent = existingLeadingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
			
			var newLeadingTrivia = new List<SyntaxTrivia>();
			InfoToDocComment(info, newLeadingTrivia, indent);
			node = node.WithLeadingTrivia(existingLeadingTrivia.InsertRange(existingLeadingTrivia.Count - 1, newLeadingTrivia));
		}
		
		return base.VisitMethodDeclaration(node);
	}
}
