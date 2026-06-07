using Microsoft.CodeAnalysis.CSharp;

namespace GenerateNativeWasmtimeDocs;

internal static class Program
{
	private static void Main(string[] args)
	{
		if (args.Length != 2)
			throw new DirectoryNotFoundException("Expected 2 arguments, one for NativeWasmtime.g.cs and another for xml directory, but arg was not provided.");
		
		var generatedApiPath = args[0];
		if (!File.Exists(generatedApiPath))
			throw new FileNotFoundException(generatedApiPath);

		var xmlDirPath = args[1];
		if (!Directory.Exists(xmlDirPath))
			throw new DirectoryNotFoundException(xmlDirPath);
		
		var generatedApiOutPath = Path.Combine(generatedApiPath, "..", "NativeWasmtime.g.g.cs");

		var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(generatedApiPath));
		tree.WithFilePath(generatedApiPath);
		
		var rewriter = new Rewriter(xmlDirPath);
		var newSource = rewriter.Visit(tree.GetRoot());
		
		File.WriteAllText(generatedApiOutPath, newSource.ToFullString());
	}
}
