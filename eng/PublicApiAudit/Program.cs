using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PublicApiAudit <repository-root> <output.json>");
    return 1;
}

var root = Path.GetFullPath(args[0]);
var packages = new[] { "UiModel", "SelfHealing", "LlmHealing", "Discovery", "WebDiscovery", "IntentAutomation", "PlaywrightLiveExploration" };
var entries = new Dictionary<string, ApiType>(StringComparer.Ordinal);
foreach (var package in packages)
{
    var directory = Path.Combine(root, "TestAutomation", package);
    foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar).Any(part => part == "obj" || part == "bin"))
        .OrderBy(path => path, StringComparer.Ordinal))
    {
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
        foreach (var type in syntax.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Where(IsVisible))
        {
            var ns = string.Join(".", type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));
            var typeName = type.Identifier.Text + (type is TypeDeclarationSyntax declaration ? declaration.TypeParameterList?.ToString() : "");
            var name = ns + "." + typeName;
            if (!entries.TryGetValue(name, out var entry))
            {
                entry = new ApiType { Package = package, Type = name };
                entries.Add(name, entry);
            }
            entry.Files.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            entry.Declarations.Add(System.Text.RegularExpressions.Regex.Replace(
                type.ToString().Substring(0, type.OpenBraceToken.SpanStart - type.SpanStart), @"\s+", " ").Trim());
            foreach (var member in type.ChildNodes().OfType<MemberDeclarationSyntax>().Where(IsVisible))
            {
                entry.Members.Add(Signature(member));
                if (member is PropertyDeclarationSyntax property && property.AccessorList?.Accessors.Any(
                    accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration) && accessor.Modifiers.Count == 0) == true)
                {
                    entry.PublicMutableProperties++;
                }
            }
        }
    }
}

var output = entries.Values.OrderBy(entry => entry.Type, StringComparer.Ordinal).ToArray();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1], JsonSerializer.Serialize(output, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
}) + "\n");
Console.WriteLine($"{output.Length} public types; {output.Sum(entry => entry.PublicMutableProperties)} public settable properties.");
return 0;

static bool IsVisible(MemberDeclarationSyntax member)
{
    return (member.Modifiers.Any(SyntaxKind.PublicKeyword) || member.Modifiers.Any(SyntaxKind.ProtectedKeyword) ||
            member.Parent is InterfaceDeclarationSyntax || member is EnumMemberDeclarationSyntax) &&
        member.Ancestors().OfType<BaseTypeDeclarationSyntax>().All(type => type.Modifiers.Any(SyntaxKind.PublicKeyword));
}

static string Signature(MemberDeclarationSyntax member)
{
    var declaration = member switch
    {
        MethodDeclarationSyntax method => method.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default).ToString(),
        ConstructorDeclarationSyntax constructor => constructor.WithBody(null).WithInitializer(null).WithExpressionBody(null).WithSemicolonToken(default).ToString(),
        PropertyDeclarationSyntax property => string.Join(" ", property.Modifiers) + " " + property.Type + " " + property.Identifier +
            " { " + (property.AccessorList == null ? "get;" : string.Join(" ", property.AccessorList.Accessors.Select(accessor =>
                (accessor.Modifiers.Count == 0 ? "" : string.Join(" ", accessor.Modifiers) + " ") + accessor.Keyword.Text + ";"))) + " }",
        FieldDeclarationSyntax field => string.Join(" ", field.Modifiers) + " " + field.Declaration.Type + " " +
            string.Join(", ", field.Declaration.Variables.Select(variable => variable.Identifier.Text)) + ";",
        OperatorDeclarationSyntax op => op.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default).ToString(),
        ConversionOperatorDeclarationSyntax conversion => conversion.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default).ToString(),
        EnumMemberDeclarationSyntax value => value.ToString(),
        BaseTypeDeclarationSyntax type => type.Identifier.Text,
        _ => throw new NotSupportedException("Add an inventory signature for " + member.Kind())
    };
    return System.Text.RegularExpressions.Regex.Replace(declaration, @"\s+", " ").Trim();
}

internal sealed class ApiType
{
    public string Package { get; set; } = "";
    public string Type { get; set; } = "";
    public int PublicMutableProperties { get; set; }
    public SortedSet<string> Declarations { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> Files { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> Members { get; } = new(StringComparer.Ordinal);
}
