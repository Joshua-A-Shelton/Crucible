using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Crucible.Generators;

/// <summary>
/// Writes a JSON reader and writer into every <c>[Serialized]</c> type
/// (ADR 0021, 0038), as an implementation of <c>IJsonSerialized&lt;T&gt;</c>.
/// The code calls <c>Utf8JsonWriter</c> and <c>Utf8JsonReader</c> directly:
/// no reflection, so it runs the same under JIT, hot reload, and NativeAOT.
/// What it can't serialize is a compile error.
/// </summary>
/// <remarks>
/// Like the system registration generator, the per-type stage reduces
/// symbols to strings (<see cref="SerializedTypeModel"/>) so Roslyn can cache
/// it; each type gets its own file, so editing one type regenerates only it.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class SerializerGenerator : IIncrementalGenerator
{
    private const string SerializedName = "Crucible.Serialization.SerializedAttribute";
    private const string NotSerializedName = "Crucible.Serialization.NotSerializedAttribute";
    private const string FormerlyNamedName = "Crucible.Serialization.FormerlyNamedAttribute";
    private const string InterfaceName = "Crucible.Serialization.IJsonSerialized`1";
    private const string VersionProperty = "$version";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<SerializedTypeModel> types = context.SyntaxProvider.ForAttributeWithMetadataName(
            SerializedName,
            static (node, _) => node is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax,
            static (attributeContext, _) => Describe(attributeContext));
        context.RegisterSourceOutput(types, static (output, type) => Emit(output, type));

        // [Serialized] on a member only means something inside a [Serialized] type.
        IncrementalValuesProvider<DiagnosticInfo?> misplaced = context.SyntaxProvider.ForAttributeWithMetadataName(
            SerializedName,
            static (node, _) => node is VariableDeclaratorSyntax or PropertyDeclarationSyntax,
            static (attributeContext, _) => CheckMember(attributeContext));
        context.RegisterSourceOutput(misplaced, static (output, diagnostic) =>
        {
            if (diagnostic is not null)
            {
                output.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        });
    }

    private static DiagnosticInfo? CheckMember(GeneratorAttributeSyntaxContext context)
    {
        ISymbol member = context.TargetSymbol;
        if (member.ContainingType is { } type && HasAttribute(type, SerializedName))
        {
            return null;
        }
        return new DiagnosticInfo(Diagnostics.MisplacedAttribute, LocationInfo.From(member.Locations.FirstOrDefault()),
            new EquatableArray<string>([member.ToDisplayString(),
                "[Serialized] on a member only applies inside a [Serialized] type"]));
    }

    private static SerializedTypeModel Describe(GeneratorAttributeSyntaxContext context)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        string display = type.ToDisplayString();
        LocationInfo? typeLocation = LocationInfo.From(type.Locations.FirstOrDefault());
        List<DiagnosticInfo> diagnostics = [];

        void Report(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] arguments) =>
            diagnostics.Add(new DiagnosticInfo(descriptor, location, new EquatableArray<string>(arguments)));

        // The type itself.
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (!IsPartial(current))
            {
                Report(Diagnostics.NotPartial, typeLocation, display);
                break;
            }
        }
        string? problem = TypeProblem(type);
        if (problem is not null)
        {
            Report(Diagnostics.UnsupportedType, typeLocation, display, problem);
        }
        if (type.TypeKind == TypeKind.Class && !type.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 0))
        {
            Report(Diagnostics.NoParameterlessConstructor, typeLocation, display);
        }

        // Its version and migration.
        int version = 1;
        foreach (KeyValuePair<string, TypedConstant> argument in context.Attributes[0].NamedArguments)
        {
            if (argument.Key == "Version" && argument.Value.Value is int value)
            {
                version = value;
            }
        }
        if (version < 1)
        {
            Report(Diagnostics.BadVersion, typeLocation, display, $"the version must be at least 1, not {version}");
        }
        else if (version > 1 && !type.GetMembers("Migrate").OfType<IMethodSymbol>().Any(IsMigration))
        {
            Report(Diagnostics.BadVersion, typeLocation, display,
                $"version {version} needs 'static void Migrate(System.Text.Json.Nodes.JsonObject data, int fromVersion)' " +
                "to upgrade older data");
        }

        // Its members, in declaration order.
        List<MemberModel> members = [];
        HashSet<string> names = new(StringComparer.Ordinal) { VersionProperty };
        foreach (ISymbol member in type.GetMembers())
        {
            if (member.IsStatic || member.IsImplicitlyDeclared)
            {
                continue;
            }
            bool marked = HasAttribute(member, SerializedName);
            bool skipped = HasAttribute(member, NotSerializedName);
            LocationInfo? location = LocationInfo.From(member.Locations.FirstOrDefault());
            if (marked && skipped)
            {
                Report(Diagnostics.MisplacedAttribute, location, $"{display}.{member.Name}",
                    "[Serialized] and [NotSerialized] contradict each other");
                continue;
            }
            if (skipped)
            {
                continue;
            }

            ITypeSymbol memberType;
            string? readOnlyProblem = null;
            switch (member)
            {
                case IFieldSymbol field when !field.IsConst:
                    if (field.DeclaredAccessibility != Accessibility.Public && !marked)
                    {
                        continue;
                    }
                    memberType = field.Type;
                    if (field.IsReadOnly)
                    {
                        readOnlyProblem = "is readonly";
                    }
                    break;
                case IPropertySymbol property when !property.IsIndexer:
                    bool publicGetSet = property.DeclaredAccessibility == Accessibility.Public &&
                                        property.GetMethod?.DeclaredAccessibility == Accessibility.Public &&
                                        property.SetMethod?.DeclaredAccessibility == Accessibility.Public;
                    if (!publicGetSet && !marked)
                    {
                        continue; // computed, or not meant to be set from outside
                    }
                    memberType = property.Type;
                    readOnlyProblem = property.GetMethod is null ? "has no getter"
                        : property.SetMethod is null ? "has no setter"
                        : property.SetMethod.IsInitOnly ? "is init-only"
                        : null;
                    break;
                default:
                    continue;
            }
            if (readOnlyProblem is not null)
            {
                Report(Diagnostics.NotWritable, location, display, member.Name, readOnlyProblem);
                continue;
            }
            TypeShape? shape = ShapeOf(memberType);
            if (shape is null)
            {
                Report(Diagnostics.UnsupportedMember, location, display, member.Name, memberType.ToDisplayString());
                continue;
            }
            string[] formerNames =
            [
                .. member.GetAttributes()
                    .Where(attribute => attribute.AttributeClass?.ToDisplayString() == FormerlyNamedName)
                    .Select(attribute => attribute.ConstructorArguments.FirstOrDefault().Value as string)
                    .OfType<string>(),
            ];
            foreach (string name in formerNames.Prepend(member.Name))
            {
                if (!names.Add(name))
                {
                    Report(Diagnostics.DuplicateName, location, display, name);
                }
            }
            members.Add(new MemberModel(member.Name, new EquatableArray<string>(formerNames), shape));
        }

        List<string> containers = [];
        for (INamedTypeSymbol? current = type.ContainingType; current is not null; current = current.ContainingType)
        {
            containers.Insert(0, $"partial {Keyword(current)} {current.Name}");
        }
        return new SerializedTypeModel(
            type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
            new EquatableArray<string>([.. containers]),
            Keyword(type),
            type.Name,
            display,
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.IsReferenceType,
            version,
            new EquatableArray<MemberModel>([.. members]),
            new EquatableArray<DiagnosticInfo>([.. diagnostics]));
    }

    // Why the type can't carry generated serialization, or null if it can.
    private static string? TypeProblem(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.Arity > 0)
            {
                return "generic types aren't supported yet";
            }
        }
        if (type.IsStatic || type.IsAbstract)
        {
            return "it is static or abstract, so it can't be created";
        }
        if (type.IsRefLikeType)
        {
            return "a ref struct can't be stored";
        }
        if (type.TypeKind == TypeKind.Class && type.BaseType is { SpecialType: not SpecialType.System_Object })
        {
            return "classes with a base class aren't supported yet";
        }
        return null;
    }

    private static bool IsMigration(IMethodSymbol method) =>
        method.IsStatic && method.ReturnsVoid && method.Parameters.Length == 2 &&
        method.Parameters[0].Type.ToDisplayString() == "System.Text.Json.Nodes.JsonObject" &&
        method.Parameters[1].Type.SpecialType == SpecialType.System_Int32;

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.All(reference =>
            reference.GetSyntax() is TypeDeclarationSyntax declaration &&
            declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static string Keyword(INamedTypeSymbol type) =>
        type.IsRecord ? (type.TypeKind == TypeKind.Struct ? "record struct" : "record")
        : type.TypeKind == TypeKind.Struct ? "struct"
        : type.TypeKind == TypeKind.Interface ? "interface"
        : "class";

    private static bool HasAttribute(ISymbol symbol, string name) =>
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == name);

    // How a member type is written and read, or null if it can't be.
    private static TypeShape? ShapeOf(ITypeSymbol type)
    {
        string expression = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        ShapeKind? simple = type.SpecialType switch
        {
            SpecialType.System_Boolean => ShapeKind.Boolean,
            SpecialType.System_Byte => ShapeKind.Byte,
            SpecialType.System_SByte => ShapeKind.SByte,
            SpecialType.System_Int16 => ShapeKind.Int16,
            SpecialType.System_UInt16 => ShapeKind.UInt16,
            SpecialType.System_Int32 => ShapeKind.Int32,
            SpecialType.System_UInt32 => ShapeKind.UInt32,
            SpecialType.System_Int64 => ShapeKind.Int64,
            SpecialType.System_UInt64 => ShapeKind.UInt64,
            SpecialType.System_Single => ShapeKind.Single,
            SpecialType.System_Double => ShapeKind.Double,
            SpecialType.System_String => ShapeKind.String,
            _ => null,
        };
        if (simple is { } kind)
        {
            return new TypeShape(kind, expression, null);
        }
        if (type is IArrayTypeSymbol array)
        {
            return array.Rank == 1 && ShapeOf(array.ElementType) is { } element
                ? new TypeShape(ShapeKind.Array, expression, element)
                : null;
        }
        if (type is not INamedTypeSymbol named)
        {
            return null;
        }
        if (named.TypeKind == TypeKind.Enum)
        {
            return new TypeShape(ShapeKind.Enum, expression, null);
        }
        string definition = named.OriginalDefinition.ToDisplayString();
        switch (definition)
        {
            case "System.Guid":
                return new TypeShape(ShapeKind.Guid, expression, null);
            case "Crucible.Assets.AssetId":
                return new TypeShape(ShapeKind.AssetId, expression, null);
            case "Crucible.Ecs.Entity":
                return new TypeShape(ShapeKind.Entity, expression, null);
            case "System.Numerics.Vector2":
                return new TypeShape(ShapeKind.Vector2, expression, null);
            case "System.Numerics.Vector3":
                return new TypeShape(ShapeKind.Vector3, expression, null);
            case "System.Numerics.Vector4":
                return new TypeShape(ShapeKind.Vector4, expression, null);
            case "System.Numerics.Quaternion":
                return new TypeShape(ShapeKind.Quaternion, expression, null);
            case "System.Numerics.Matrix4x4":
                return new TypeShape(ShapeKind.Matrix4x4, expression, null);
            case "T?" or "System.Nullable<T>":
                return ShapeOf(named.TypeArguments[0]) is { } inner
                    ? new TypeShape(ShapeKind.Nullable, expression, inner)
                    : null;
            case "System.Collections.Generic.List<T>":
                return ShapeOf(named.TypeArguments[0]) is { } item
                    ? new TypeShape(ShapeKind.List, expression, item)
                    : null;
            case "System.Collections.Generic.Dictionary<TKey, TValue>":
                return named.TypeArguments[0].SpecialType == SpecialType.System_String &&
                       ShapeOf(named.TypeArguments[1]) is { } value
                    ? new TypeShape(ShapeKind.Dictionary, expression, value)
                    : null;
        }
        // A type with generated serialization: marked in this compilation, or
        // already implementing the interface (from another assembly).
        if (HasAttribute(named, SerializedName) ||
            named.AllInterfaces.Any(candidate => candidate.OriginalDefinition.ToDisplayString() == "Crucible.Serialization.IJsonSerialized<T>"))
        {
            return new TypeShape(ShapeKind.Serialized, expression, null);
        }
        return null;
    }

    private static void Emit(SourceProductionContext output, SerializedTypeModel type)
    {
        foreach (DiagnosticInfo diagnostic in type.Diagnostics)
        {
            output.ReportDiagnostic(diagnostic.ToDiagnostic());
        }
        if (type.Diagnostics.Length > 0)
        {
            return; // the type's own errors, and nothing generated to add more
        }

        CodeWriter code = new();
        code.Line("// <auto-generated/>");
        code.Line("// Crucible's JSON serialization for this type (ADR 0038).");
        code.Line("#nullable enable");
        code.Line("#pragma warning disable CS0618 // obsolete members are still saved");
        code.Line();
        if (type.Namespace.Length > 0)
        {
            code.Open($"namespace {type.Namespace}");
        }
        foreach (string container in type.Containers)
        {
            code.Open(container);
        }
        string self = type.TypeExpression;
        code.Open($"partial {type.Keyword} {type.Name} : global::Crucible.Serialization.IJsonSerialized<{self}>");
        EmitWrite(code, type);
        code.Line();
        EmitRead(code, type);
        int depth = type.Containers.Length + 1 + (type.Namespace.Length > 0 ? 1 : 0);
        for (int i = 0; i < depth; ++i)
        {
            code.Close();
        }
        string hint = (type.Namespace.Length > 0 ? type.Namespace + "." : "") +
                      string.Join(".", type.Containers.Select(container => container.Split(' ').Last())) +
                      (type.Containers.Length > 0 ? "." : "") + type.Name;
        output.AddSource($"{hint}.Json.g.cs", code.ToString());
    }

    private static void EmitWrite(CodeWriter code, SerializedTypeModel type)
    {
        code.Line("/// <summary>Writes the value as a JSON object (generated, ADR 0038).</summary>");
        code.Open($"public static void WriteJson(global::System.Text.Json.Utf8JsonWriter writer, in {type.TypeExpression}{(type.IsClass ? "?" : "")} value)");
        if (type.IsClass)
        {
            code.Open("if (value is null)");
            code.Line("writer.WriteNullValue();");
            code.Line("return;");
            code.Close();
        }
        code.Line("writer.WriteStartObject();");
        if (type.Version > 1)
        {
            code.Line($"writer.WriteNumber({Literal(VersionProperty)}u8, {type.Version});");
        }
        int counter = 0;
        foreach (MemberModel member in type.Members)
        {
            code.Line($"writer.WritePropertyName({Literal(member.Name)}u8);");
            WriteValue(code, member.Shape, $"value.{member.Name}", ref counter);
        }
        code.Line("writer.WriteEndObject();");
        code.Close();
    }

    private static void EmitRead(CodeWriter code, SerializedTypeModel type)
    {
        string self = type.TypeExpression;
        code.Line("/// <summary>Reads a value from a JSON object (generated, ADR 0038).</summary>");
        code.Open($"public static {self} ReadJson(ref global::System.Text.Json.Utf8JsonReader reader)");
        if (type.IsClass)
        {
            code.Open("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null)");
            code.Line("return null!;");
            code.Close();
        }
        if (type.Version > 1)
        {
            code.Line("int version = global::Crucible.Serialization.JsonValues.PeekVersion(reader);");
            code.Open($"if (version != {type.Version})");
            code.Line($"byte[] upgraded = global::Crucible.Serialization.JsonValues.Migrate(ref reader, version, {type.Version}, Migrate, {Literal(type.DisplayName)});");
            code.Line("global::System.Text.Json.Utf8JsonReader migrated = new(upgraded);");
            code.Line("migrated.Read();");
            code.Line("return ReadJsonObject(ref migrated);");
            code.Close();
        }
        code.Line("return ReadJsonObject(ref reader);");
        code.Close();
        code.Line();

        code.Open($"private static {self} ReadJsonObject(ref global::System.Text.Json.Utf8JsonReader reader)");
        code.Line($"global::Crucible.Serialization.JsonValues.Expect(reader, global::System.Text.Json.JsonTokenType.StartObject, {Literal($"a {type.Name} object")});");
        code.Line($"{self} result = new();");
        code.Open("while (true)");
        code.Line("reader.Read();");
        code.Open("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject)");
        code.Line("break;");
        code.Close();
        int counter = 0;
        foreach (MemberModel member in type.Members)
        {
            string match = string.Join(" || ",
                member.FormerNames.Prepend(member.Name).Select(name => $"reader.ValueTextEquals({Literal(name)}u8)"));
            code.Open($"if ({match})");
            code.Line("reader.Read();");
            code.Open("try");
            string local = $"v{counter++}";
            code.Line($"{member.Shape.Type} {local};");
            ReadValue(code, member.Shape, local, ref counter);
            code.Line($"result.{member.Name} = {local};");
            code.Close();
            code.Open("catch (global::System.Text.Json.JsonException exception)");
            code.Line($"throw new global::System.Text.Json.JsonException({Literal(type.Name + "." + member.Name + ": ")} + exception.Message, exception);");
            code.Close();
            code.Line("continue;");
            code.Close();
        }
        code.Line("// Unknown members (removed ones, and the version) are skipped.");
        code.Line("reader.Read();");
        code.Line("reader.Skip();");
        code.Close();
        code.Line("return result;");
        code.Close();
    }

    private const string Values = "global::Crucible.Serialization.JsonValues";

    private static void WriteValue(CodeWriter code, TypeShape shape, string value, ref int counter)
    {
        switch (shape.Kind)
        {
            case ShapeKind.Boolean:
                code.Line($"writer.WriteBooleanValue({value});");
                break;
            case ShapeKind.Byte or ShapeKind.SByte or ShapeKind.Int16 or ShapeKind.UInt16 or ShapeKind.Int32
                or ShapeKind.UInt32 or ShapeKind.Int64 or ShapeKind.UInt64:
                code.Line($"writer.WriteNumberValue({value});");
                break;
            case ShapeKind.Single or ShapeKind.Double or ShapeKind.Vector2 or ShapeKind.Vector3 or ShapeKind.Vector4
                or ShapeKind.Quaternion or ShapeKind.Matrix4x4 or ShapeKind.AssetId or ShapeKind.Entity:
                code.Line($"{Values}.Write{shape.Kind}(writer, {value});");
                break;
            case ShapeKind.Guid:
                code.Line($"writer.WriteStringValue({value});");
                break;
            case ShapeKind.Enum:
                code.Line($"{Values}.WriteEnum(writer, {value});");
                break;
            case ShapeKind.Serialized:
                code.Line($"{shape.Type}.WriteJson(writer, {value});");
                break;
            case ShapeKind.String:
                {
                    string local = $"s{counter++}";
                    code.Line($"string? {local} = {value};");
                    code.Open($"if ({local} is null)");
                    code.Line("writer.WriteNullValue();");
                    code.Close();
                    code.Open("else");
                    code.Line($"writer.WriteStringValue({local});");
                    code.Close();
                    break;
                }
            case ShapeKind.Nullable:
                {
                    string local = $"n{counter++}";
                    code.Line($"{shape.Type} {local} = {value};");
                    code.Open($"if ({local}.HasValue)");
                    WriteValue(code, shape.Element!, $"{local}.Value", ref counter);
                    code.Close();
                    code.Open("else");
                    code.Line("writer.WriteNullValue();");
                    code.Close();
                    break;
                }
            case ShapeKind.Array or ShapeKind.List:
                {
                    string local = $"a{counter++}";
                    string item = $"e{counter++}";
                    code.Line($"{shape.Type}? {local} = {value};");
                    code.Open($"if ({local} is null)");
                    code.Line("writer.WriteNullValue();");
                    code.Close();
                    code.Open("else");
                    code.Line("writer.WriteStartArray();");
                    code.Open($"foreach ({shape.Element!.Type} {item} in {local})");
                    WriteValue(code, shape.Element, item, ref counter);
                    code.Close();
                    code.Line("writer.WriteEndArray();");
                    code.Close();
                    break;
                }
            case ShapeKind.Dictionary:
                {
                    string local = $"d{counter++}";
                    string key = $"k{counter++}";
                    code.Line($"{shape.Type}? {local} = {value};");
                    code.Open($"if ({local} is null)");
                    code.Line("writer.WriteNullValue();");
                    code.Close();
                    code.Open("else");
                    code.Line("writer.WriteStartObject();");
                    code.Open($"foreach (string {key} in {Values}.SortedKeys({local}))");
                    code.Line($"writer.WritePropertyName({key});");
                    WriteValue(code, shape.Element!, $"{local}[{key}]", ref counter);
                    code.Close();
                    code.Line("writer.WriteEndObject();");
                    code.Close();
                    break;
                }
        }
    }

    // Reads the value at the reader's current token into `target`, leaving
    // the reader on the value's last token.
    private static void ReadValue(CodeWriter code, TypeShape shape, string target, ref int counter)
    {
        switch (shape.Kind)
        {
            case ShapeKind.String:
                code.Line($"{target} = {Values}.ReadString(ref reader)!;");
                break;
            case ShapeKind.Enum:
                code.Line($"{target} = {Values}.ReadEnum<{shape.Type}>(ref reader);");
                break;
            case ShapeKind.Serialized:
                code.Line($"{target} = {shape.Type}.ReadJson(ref reader);");
                break;
            case ShapeKind.Nullable:
                {
                    string inner = $"n{counter++}";
                    code.Open("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null)");
                    code.Line($"{target} = null;");
                    code.Close();
                    code.Open("else");
                    code.Line($"{shape.Element!.Type} {inner};");
                    ReadValue(code, shape.Element, inner, ref counter);
                    code.Line($"{target} = {inner};");
                    code.Close();
                    break;
                }
            case ShapeKind.Array or ShapeKind.List:
                {
                    string list = $"l{counter++}";
                    string item = $"e{counter++}";
                    code.Open("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null)");
                    code.Line($"{target} = null!;");
                    code.Close();
                    code.Open("else");
                    code.Line($"{Values}.Expect(reader, global::System.Text.Json.JsonTokenType.StartArray, \"an array\");");
                    code.Line($"global::System.Collections.Generic.List<{shape.Element!.Type}> {list} = new();");
                    code.Open("while (true)");
                    code.Line("reader.Read();");
                    code.Open("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray)");
                    code.Line("break;");
                    code.Close();
                    code.Line($"{shape.Element.Type} {item};");
                    ReadValue(code, shape.Element, item, ref counter);
                    code.Line($"{list}.Add({item});");
                    code.Close();
                    code.Line(shape.Kind == ShapeKind.Array ? $"{target} = {list}.ToArray();" : $"{target} = {list};");
                    code.Close();
                    break;
                }
            case ShapeKind.Dictionary:
                {
                    string dictionary = $"d{counter++}";
                    string key = $"k{counter++}";
                    string item = $"e{counter++}";
                    code.Open("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null)");
                    code.Line($"{target} = null!;");
                    code.Close();
                    code.Open("else");
                    code.Line($"{Values}.Expect(reader, global::System.Text.Json.JsonTokenType.StartObject, \"an object\");");
                    code.Line($"{shape.Type} {dictionary} = new();");
                    code.Open("while (true)");
                    code.Line("reader.Read();");
                    code.Open("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject)");
                    code.Line("break;");
                    code.Close();
                    code.Line($"string {key} = reader.GetString()!;");
                    code.Line("reader.Read();");
                    code.Line($"{shape.Element!.Type} {item};");
                    ReadValue(code, shape.Element, item, ref counter);
                    code.Line($"{dictionary}[{key}] = {item};");
                    code.Close();
                    code.Line($"{target} = {dictionary};");
                    code.Close();
                    break;
                }
            default:
                // Booleans, numbers, GUIDs, and vectors: one helper each.
                code.Line($"{target} = {Values}.Read{shape.Kind}(ref reader);");
                break;
        }
    }

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}

internal enum ShapeKind
{
    Boolean,
    Byte,
    SByte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    String,
    Guid,
    AssetId,
    Entity,
    Vector2,
    Vector3,
    Vector4,
    Quaternion,
    Matrix4x4,
    Enum,
    Nullable,
    Array,
    List,
    Dictionary,
    Serialized,
}

// How one member type is written and read; Element is the inner type of a
// nullable, array, list, or dictionary.
internal sealed record TypeShape(ShapeKind Kind, string Type, TypeShape? Element);

internal sealed record MemberModel(string Name, EquatableArray<string> FormerNames, TypeShape Shape);

// One [Serialized] type, reduced to strings so the incremental cache can compare it.
internal sealed record SerializedTypeModel(
    string Namespace,
    EquatableArray<string> Containers,
    string Keyword,
    string Name,
    string DisplayName,
    string TypeExpression,
    bool IsClass,
    int Version,
    EquatableArray<MemberModel> Members,
    EquatableArray<DiagnosticInfo> Diagnostics);

// Indented source text, four spaces per level.
internal sealed class CodeWriter
{
    private readonly StringBuilder _text = new();
    private int _depth;

    public void Line(string line = "")
    {
        if (line.Length > 0)
        {
            _text.Append(' ', _depth * 4).Append(line);
        }
        _text.Append('\n');
    }

    public void Open(string line)
    {
        Line(line);
        Line("{");
        ++_depth;
    }

    public void Close()
    {
        --_depth;
        Line("}");
    }

    public override string ToString() => _text.ToString();
}
