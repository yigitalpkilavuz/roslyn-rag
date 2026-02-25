using System.Security.Cryptography;
using System.Text;
using RoslynRag.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynRag.Parsing;

public sealed class MethodChunkWalker : CSharpSyntaxWalker
{
    private readonly string _filePath;
    private readonly string _solutionRoot;
    private readonly string _solutionPath;
    private readonly List<CodeChunk> _chunks = new();

    private string _currentNamespace = string.Empty;
    private string _currentClassName = string.Empty;
    private string[] _currentClassBaseTypes = [];
    private string[] _currentClassDependencies = [];

    public IReadOnlyList<CodeChunk> Chunks => _chunks;

    public MethodChunkWalker(string filePath, string solutionRoot, string solutionPath)
    {
        _filePath = filePath;
        _solutionRoot = solutionRoot;
        _solutionPath = solutionPath;
    }

    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        var previousNamespace = _currentNamespace;
        _currentNamespace = node.Name.ToString();
        base.VisitNamespaceDeclaration(node);
        _currentNamespace = previousNamespace;
    }

    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        var previousNamespace = _currentNamespace;
        _currentNamespace = node.Name.ToString();
        base.VisitFileScopedNamespaceDeclaration(node);
        _currentNamespace = previousNamespace;
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var previousClassName = _currentClassName;
        var previousBaseTypes = _currentClassBaseTypes;
        var previousDeps = _currentClassDependencies;

        _currentClassName = node.Identifier.Text;
        _currentClassBaseTypes = ExtractBaseTypes(node);
        _currentClassDependencies = ExtractDependencies(node);

        EmitClassHeaderChunk(node);

        base.VisitClassDeclaration(node);

        _currentClassName = previousClassName;
        _currentClassBaseTypes = previousBaseTypes;
        _currentClassDependencies = previousDeps;
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        var previousClassName = _currentClassName;
        var previousBaseTypes = _currentClassBaseTypes;
        var previousDeps = _currentClassDependencies;

        _currentClassName = node.Identifier.Text;
        _currentClassBaseTypes = ExtractBaseTypes(node);
        _currentClassDependencies = ExtractDependencies(node);

        EmitClassHeaderChunk(node);

        base.VisitRecordDeclaration(node);

        _currentClassName = previousClassName;
        _currentClassBaseTypes = previousBaseTypes;
        _currentClassDependencies = previousDeps;
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        EmitMethodChunk(node, ChunkKind.Method);
        // Don't call base — no need to recurse into method bodies
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        EmitMethodChunk(node, ChunkKind.Constructor);
    }

    private void EmitClassHeaderChunk(TypeDeclarationSyntax node)
    {
        var relativePath = GetRelativePath();
        var lineSpan = node.SyntaxTree.GetLineSpan(node.Span);
        var startLine = lineSpan.StartLinePosition.Line + 1;

        var headerBuilder = new StringBuilder();
        var classDecl = BuildClassDeclarationText(node);
        headerBuilder.AppendLine(classDecl);
        headerBuilder.AppendLine("{");

        foreach (var field in node.Members.OfType<FieldDeclarationSyntax>())
        {
            headerBuilder.AppendLine($"    {field.ToFullString().Trim()}");
        }

        foreach (var prop in node.Members.OfType<PropertyDeclarationSyntax>())
        {
            var propText = $"    {prop.Type} {prop.Identifier}{prop.AccessorList}";
            headerBuilder.AppendLine(propText.Trim());
        }

        // Constructor signatures (without bodies) for DI visibility
        foreach (var ctor in node.Members.OfType<ConstructorDeclarationSyntax>())
        {
            headerBuilder.AppendLine($"    {ctor.Modifiers} {ctor.Identifier}{ctor.ParameterList};");
        }

        headerBuilder.AppendLine("}");

        var body = headerBuilder.ToString();
        var endLine = startLine + body.Split('\n').Length - 1;
        var attributes = ExtractAttributes(node.AttributeLists);
        var baseTypeDisplay = _currentClassBaseTypes.Length > 0
            ? $" : {string.Join(", ", _currentClassBaseTypes)}"
            : string.Empty;
        var fullSignature = $"{node.Keyword.Text} {_currentClassName}{baseTypeDisplay}";

        var embeddingText = ComposeEmbeddingText(
            relativePath, _currentNamespace, _currentClassName + baseTypeDisplay,
            _currentClassDependencies, attributes, body);

        var chunkId = GenerateChunkId(relativePath, startLine);

        _chunks.Add(new CodeChunk
        {
            Id = chunkId,
            SolutionId = _solutionPath,
            FilePath = relativePath,
            Namespace = _currentNamespace,
            ClassName = _currentClassName,
            MethodName = string.Empty,
            FullSignature = fullSignature,
            Kind = ChunkKind.ClassHeader,
            StartLine = startLine,
            EndLine = endLine,
            Body = body,
            Attributes = attributes,
            Dependencies = _currentClassDependencies,
            BaseTypes = _currentClassBaseTypes,
            EmbeddingText = embeddingText
        });
    }

    private void EmitMethodChunk(BaseMethodDeclarationSyntax node, ChunkKind kind)
    {
        var relativePath = GetRelativePath();
        var lineSpan = node.SyntaxTree.GetLineSpan(node.Span);
        var startLine = lineSpan.StartLinePosition.Line + 1;
        var endLine = lineSpan.EndLinePosition.Line + 1;

        var body = node.ToFullString().Trim();
        var attributes = ExtractAttributes(node.AttributeLists);
        var methodName = GetMethodName(node);
        var fullSignature = BuildMethodSignature(node);

        var embeddingText = ComposeEmbeddingText(
            relativePath, _currentNamespace,
            _currentClassName + (_currentClassBaseTypes.Length > 0
                ? $" : {string.Join(", ", _currentClassBaseTypes)}"
                : string.Empty),
            _currentClassDependencies, attributes, body);

        var chunkId = GenerateChunkId(relativePath, startLine);

        _chunks.Add(new CodeChunk
        {
            Id = chunkId,
            SolutionId = _solutionPath,
            FilePath = relativePath,
            Namespace = _currentNamespace,
            ClassName = _currentClassName,
            MethodName = methodName,
            FullSignature = fullSignature,
            Kind = kind,
            StartLine = startLine,
            EndLine = endLine,
            Body = body,
            Attributes = attributes,
            Dependencies = _currentClassDependencies,
            BaseTypes = _currentClassBaseTypes,
            EmbeddingText = embeddingText
        });
    }

    private static string ComposeEmbeddingText(
        string filePath, string ns, string classDisplay,
        string[] dependencies, string[] attributes, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// File: {filePath}");
        sb.AppendLine($"// Namespace: {ns}");
        sb.AppendLine($"// Class: {classDisplay}");

        if (dependencies.Length > 0)
        {
            sb.Append("// Dependencies: ");
            sb.AppendJoin(", ", dependencies);
            sb.AppendLine();
        }

        if (attributes.Length > 0)
        {
            sb.Append("// Attributes: ");
            sb.AppendJoin(", ", attributes);
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.Append(body);

        return sb.ToString();
    }

    private string[] ExtractBaseTypes(TypeDeclarationSyntax node)
    {
        if (node.BaseList is null)
            return [];

        return node.BaseList.Types
            .Select(t => t.Type.ToString())
            .ToArray();
    }

    private string[] ExtractDependencies(TypeDeclarationSyntax node)
    {
        // Check primary constructor parameters first (C# 12+)
        if (node.ParameterList is { Parameters.Count: > 0 })
        {
            return node.ParameterList.Parameters
                .Select(p => p.Type?.ToString() ?? "unknown")
                .ToArray();
        }

        var ctor = node.Members
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault();

        if (ctor is null)
            return [];

        return ctor.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? "unknown")
            .ToArray();
    }

    private static string[] ExtractAttributes(SyntaxList<AttributeListSyntax> attributeLists)
    {
        return attributeLists
            .SelectMany(al => al.Attributes)
            .Select(a => $"[{a}]")
            .ToArray();
    }

    private static string GetMethodName(BaseMethodDeclarationSyntax node) => node switch
    {
        MethodDeclarationSyntax method => method.Identifier.Text,
        ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
        _ => "Unknown"
    };

    private static string BuildMethodSignature(BaseMethodDeclarationSyntax node) => node switch
    {
        MethodDeclarationSyntax method =>
            $"{method.Modifiers} {method.ReturnType} {method.Identifier}{method.TypeParameterList}{method.ParameterList}",
        ConstructorDeclarationSyntax ctor =>
            $"{ctor.Modifiers} {ctor.Identifier}{ctor.ParameterList}",
        _ => node.ToString().Split('{')[0].Trim()
    };

    private static string BuildClassDeclarationText(TypeDeclarationSyntax node)
    {
        var sb = new StringBuilder();

        if (node.Modifiers.Any())
            sb.Append($"{node.Modifiers} ");

        sb.Append($"{node.Keyword.Text} {node.Identifier}");

        if (node.TypeParameterList is not null)
            sb.Append(node.TypeParameterList);

        if (node.ParameterList is not null)
            sb.Append(node.ParameterList);

        if (node.BaseList is not null)
            sb.Append($" {node.BaseList}");

        return sb.ToString();
    }

    private string GetRelativePath()
    {
        if (_filePath.StartsWith(_solutionRoot, StringComparison.OrdinalIgnoreCase))
        {
            var relative = _filePath[_solutionRoot.Length..].TrimStart(Path.DirectorySeparatorChar, '/');
            return relative.Replace('\\', '/');
        }
        return _filePath.Replace('\\', '/');
    }

    private string GenerateChunkId(string filePath, int startLine)
    {
        var input = $"{_solutionPath}:{filePath}:{startLine}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }
}
