using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Crucible.Ecs;

/// <summary>What a hot reload did to a component or one of its fields (ADR 0035). Names are placeholders.</summary>
public enum MigrationChange
{
    /// <summary>A field the new version added: zeroed, or its initial value in a managed component.</summary>
    FieldAdded,

    /// <summary>A field the new version removed: its values are dropped.</summary>
    FieldRemoved,

    /// <summary>A field whose type changed: reset like an added one.</summary>
    FieldTypeChanged,

    /// <summary>
    /// A managed component's field holding the game's own types (a game class,
    /// a <c>List</c> of them): reset, since the old version's objects would
    /// keep it loaded.
    /// </summary>
    FieldReset,

    /// <summary>
    /// A component the new version no longer has, or now holds references
    /// (so it must be registered as managed): removed from every entity.
    /// </summary>
    ComponentRemoved,
}

/// <summary>One change a hot reload made to existing values (ADR 0035), for the editor to show.</summary>
public readonly record struct MigrationNote(string Component, string? Field, MigrationChange Change)
{
    public override string ToString() => Field is null ? $"{Component}: {Change}" : $"{Component}.{Field}: {Change}";
}

/// <summary>
/// Moves every world's component values from one game version's types to
/// the next (ADR 0035): blittable components field by field in native
/// memory, managed ones into the new version's pools at the same slots.
/// </summary>
/// <remarks>
/// Fields are matched by name and type. Blittable components are flattened
/// to their leaf fields (Movement.Velocity.X), so an edit inside a nested
/// struct keeps the rest; managed components match their own fields only.
/// Runs between the two versions' loads, with the old one still loaded (its
/// values are read here) and the new one's code not yet run.
/// </remarks>
internal static class ComponentMigration
{
    private const string JitOnly =
        "Hot reload inspects the game's types by reflection, which only JIT builds can do (ADR 0001, 0035).";

    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [RequiresUnreferencedCode(JitOnly)]
    [RequiresDynamicCode(JitOnly)]
    public static List<MigrationNote> Migrate(AssemblyLoadContext oldGame, AssemblyLoadContext newGame)
    {
        List<MigrationNote> notes = [];
        foreach ((string name, Registration registration) in Components.Registered())
        {
            if (registration.Type is null || !registration.Type.TryGetTarget(out Type? oldType) ||
                !Involves(oldType, oldGame))
            {
                continue; // the engine's, or a test's: not reloaded
            }
            Type? newType = Counterpart(oldType, oldGame, newGame);
            if (newType is null)
            {
                Remove(name, registration, notes);
            }
            else if (registration.Kind == ComponentKind.Managed)
            {
                MigrateManaged(name, registration, oldType, newType, oldGame, notes);
            }
            else
            {
                MigrateBlittable(name, registration, oldType, newType, notes);
            }
        }
        return notes;
    }

    private static void Remove(string name, Registration registration, List<MigrationNote> notes)
    {
        Api.RetireComponent(registration.Id.Value);
        Api.RemoveComponentEverywhere(registration.Id.Value);
        Components.Replace(name, null);
        notes.Add(new MigrationNote(name, null, MigrationChange.ComponentRemoved));
    }

    // -------------------------------------------------------------------------
    // Blittable components: field by field, in native memory
    // -------------------------------------------------------------------------

    // A leaf field: its type, as matching sees it, and where it sits.
    private readonly record struct Leaf(string Type, uint Offset, uint Size);

    [RequiresUnreferencedCode(JitOnly)]
    [RequiresDynamicCode(JitOnly)]
    private static void MigrateBlittable(string name, Registration old, Type oldType, Type newType,
        List<MigrationNote> notes)
    {
        if (ContainsReferences(newType))
        {
            Remove(name, old, notes);
            return;
        }

        ComponentId id = old.Id;
        if (old.Kind != ComponentKind.Tag)
        {
            uint[] copies = Plan(name, Leaves(oldType), Leaves(newType), notes, out bool sameFields);
            uint size = SizeOf(newType);
            uint alignment = AlignmentOf(newType);
            if (!sameFields || size != SizeOf(oldType) || alignment != AlignmentOf(oldType))
            {
                // The same name, a new ID: native code refuses a name
                // registered again with another layout, so the old one goes.
                Api.RetireComponent(old.Id.Value);
                ComponentStorage storage = old.Kind == ComponentKind.Sparse
                    ? ComponentStorage.Sparse
                    : ComponentStorage.Archetype;
                id = new ComponentId(Api.RegisterComponent(Components.NameOf(newType), size, alignment, storage));
                Api.MigrateComponent(old.Id.Value, id.Value, copies);
            }
        }
        Adopt(name, newType, id, old.Kind);
    }

    // The copies, as (old offset, new offset, size) triples, for the leaves
    // both layouts have with the same type; the rest is noted. sameFields:
    // nothing to move at all.
    private static uint[] Plan(string component, Dictionary<string, Leaf> before, Dictionary<string, Leaf> after,
        List<MigrationNote> notes, out bool sameFields)
    {
        List<uint> copies = [];
        sameFields = before.Count == after.Count;
        foreach ((string path, Leaf leaf) in after)
        {
            if (!before.TryGetValue(path, out Leaf old))
            {
                notes.Add(new MigrationNote(component, path, MigrationChange.FieldAdded));
                sameFields = false;
            }
            else if (old.Type != leaf.Type || old.Size != leaf.Size)
            {
                notes.Add(new MigrationNote(component, path, MigrationChange.FieldTypeChanged));
                sameFields = false;
            }
            else
            {
                copies.AddRange([old.Offset, leaf.Offset, leaf.Size]);
                sameFields &= old.Offset == leaf.Offset;
            }
        }
        foreach (string path in before.Keys.Where(path => !after.ContainsKey(path)))
        {
            notes.Add(new MigrationNote(component, path, MigrationChange.FieldRemoved));
        }
        return [.. copies];
    }

    [RequiresUnreferencedCode(JitOnly)]
    [RequiresDynamicCode(JitOnly)]
    private static Dictionary<string, Leaf> Leaves(Type type)
    {
        Dictionary<string, Leaf> leaves = [];
        Flatten(type, "", [], leaves);
        return leaves;
    }

    [RequiresUnreferencedCode(JitOnly)]
    [RequiresDynamicCode(JitOnly)]
    private static void Flatten(Type type, string prefix, List<FieldInfo> chain, Dictionary<string, Leaf> leaves)
    {
        foreach (FieldInfo field in type.GetFields(InstanceFields))
        {
            chain.Add(field);
            string path = prefix + field.Name;
            if (LeafType(field) is { } leafType)
            {
                Type fieldType = field.FieldType;
                uint size = fieldType.IsPointer || fieldType.IsFunctionPointer ? (uint)IntPtr.Size : SizeOf(fieldType);
                leaves[path] = new Leaf(leafType, OffsetOf(chain), size);
            }
            else
            {
                Flatten(field.FieldType, path + ".", chain, leaves);
            }
            chain.RemoveAt(chain.Count - 1);
        }
    }

    // A field copied as a whole, named as matching compares it; null for a
    // struct to flatten further. Fixed buffers and inline arrays are copied
    // whole: their element type's name and the length must match.
    private static string? LeafType(FieldInfo field)
    {
        Type type = field.FieldType;
        if (type.IsPrimitive)
        {
            return type.FullName;
        }
        if (type.IsEnum)
        {
            return $"{type.FullName} : {Enum.GetUnderlyingType(type).FullName}";
        }
        if (type.IsPointer || type.IsFunctionPointer)
        {
            return "pointer";
        }
        if (field.GetCustomAttribute<FixedBufferAttribute>() is { } fixedBuffer)
        {
            return $"{fixedBuffer.ElementType.FullName}[{fixedBuffer.Length}]";
        }
        if (type.GetCustomAttribute<InlineArrayAttribute>() is { } inlineArray)
        {
            return $"{type.FullName}[{inlineArray.Length}]";
        }
        return null;
    }

    private delegate nint FieldAddress(ref byte start);

    // A field's offset in the runtime's own layout, which neither
    // Marshal.OffsetOf (the marshaled layout: bool is 4 bytes there) nor a
    // reflection API gives: a tiny method takes the field's address
    // (ldflda, through each nested struct) and subtracts the start's.
    [RequiresDynamicCode(JitOnly)]
    private static uint OffsetOf(List<FieldInfo> chain)
    {
        DynamicMethod method = new("FieldOffset", typeof(nint), [typeof(byte).MakeByRefType()],
            restrictedSkipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        foreach (FieldInfo field in chain)
        {
            il.Emit(OpCodes.Ldflda, field);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ret);
        // Only addresses are computed, nothing is read, but the start is a
        // real byte so the reference is a valid one.
        byte start = 0;
        return (uint)method.CreateDelegate<FieldAddress>()(ref start);
    }

    // -------------------------------------------------------------------------
    // Managed components: into the new version's pool, at the same slots
    // -------------------------------------------------------------------------

    // A field copied from each old value to the new one.
    private sealed class FieldMove(string name, FieldInfo from, FieldInfo to)
    {
        public string Name { get; } = name;
        public FieldInfo From { get; } = from;
        public FieldInfo To { get; } = to;
        public bool ResetNoted { get; set; }
    }

    [RequiresUnreferencedCode(JitOnly)]
    [RequiresDynamicCode(JitOnly)]
    private static void MigrateManaged(string name, Registration old, Type oldType, Type newType,
        AssemblyLoadContext oldGame, List<MigrationNote> notes)
    {
        List<FieldMove> moves = ManagedPlan(name, oldType, newType, oldGame, notes);
        IManagedPool newPool = (IManagedPool)typeof(ManagedPool<>).MakeGenericType(newType)
            .GetField(nameof(ManagedPool<int>.Instance))!.GetValue(null)!;
        if (ManagedPools.Get(old.Id) is { } oldPool)
        {
            object?[] values = oldPool.Export(out int[] free);
            for (int slot = 0; slot < values.Length; ++slot)
            {
                if (values[slot] is { } value)
                {
                    values[slot] = Convert(value, newType, moves, name, oldGame, notes);
                }
            }
            newPool.Import(values, free);
        }
        // The same ID: native code holds only slot numbers, and those stay.
        ManagedPools.Add(old.Id, newPool);
        Adopt(name, newType, old.Id, ComponentKind.Managed);
    }

    [RequiresUnreferencedCode(JitOnly)]
    private static List<FieldMove> ManagedPlan(string component, Type oldType, Type newType,
        AssemblyLoadContext oldGame, List<MigrationNote> notes)
    {
        Dictionary<string, FieldInfo> before = FieldsOf(oldType);
        Dictionary<string, FieldInfo> after = FieldsOf(newType);
        List<FieldMove> moves = [];
        foreach ((string fieldName, FieldInfo to) in after)
        {
            if (!before.TryGetValue(fieldName, out FieldInfo? from))
            {
                notes.Add(new MigrationNote(component, fieldName, MigrationChange.FieldAdded));
            }
            else if (Involves(from.FieldType, oldGame))
            {
                notes.Add(new MigrationNote(component, fieldName, MigrationChange.FieldReset));
            }
            else if (from.FieldType != to.FieldType)
            {
                notes.Add(new MigrationNote(component, fieldName, MigrationChange.FieldTypeChanged));
            }
            else
            {
                moves.Add(new FieldMove(fieldName, from, to));
            }
        }
        foreach (string fieldName in before.Keys.Where(fieldName => !after.ContainsKey(fieldName)))
        {
            notes.Add(new MigrationNote(component, fieldName, MigrationChange.FieldRemoved));
        }
        return moves;
    }

    // A class's fields include its base classes' private ones; the most
    // derived wins a name both have.
    [RequiresUnreferencedCode(JitOnly)]
    private static Dictionary<string, FieldInfo> FieldsOf(Type type)
    {
        Dictionary<string, FieldInfo> fields = [];
        for (Type? current = type; current is not null && current != typeof(object) && current != typeof(ValueType);
             current = current.BaseType)
        {
            foreach (FieldInfo field in current.GetFields(InstanceFields | BindingFlags.DeclaredOnly))
            {
                fields.TryAdd(field.Name, field);
            }
        }
        return fields;
    }

    // A new value, as the new version would make it (so added fields get
    // their initializers), with the matching fields copied over.
    [RequiresUnreferencedCode(JitOnly)]
    private static object Convert(object old, Type newType, List<FieldMove> moves, string component,
        AssemblyLoadContext oldGame, List<MigrationNote> notes)
    {
        object fresh = newType.IsValueType
            ? Activator.CreateInstance(newType)!
            : newType.GetConstructor(InstanceFields, Type.EmptyTypes)?.Invoke(null) ??
              RuntimeHelpers.GetUninitializedObject(newType);
        foreach (FieldMove move in moves)
        {
            object? value = move.From.GetValue(old);
            // A game object behind an engine or .NET type (object, an
            // interface) would keep the old version loaded too.
            if (value is not null && Involves(value.GetType(), oldGame))
            {
                if (!move.ResetNoted)
                {
                    notes.Add(new MigrationNote(component, move.Name, MigrationChange.FieldReset));
                    move.ResetNoted = true;
                }
                continue;
            }
            move.To.SetValue(fresh, value); // on a boxed struct, sets the box's copy
        }
        return fresh;
    }

    // -------------------------------------------------------------------------
    // Types
    // -------------------------------------------------------------------------

    // The new version's type caches the ID before its code runs, so its
    // Register call returns it; the table moves to the new type.
    [RequiresDynamicCode(JitOnly)]
    private static void Adopt(string oldName, Type newType, ComponentId id, ComponentKind kind)
    {
        Type cache = typeof(ComponentType<>).MakeGenericType(newType);
        cache.GetField(nameof(ComponentType<int>.Kind))!.SetValue(null, kind);
        cache.GetField(nameof(ComponentType<int>.Id))!.SetValue(null, id);
        Components.Replace(oldName, null);
        Components.Replace(Components.NameOf(newType),
            new Registration(id, kind, new WeakReference<Type>(newType)));
    }

    // Whether the type is the game's or built from the game's (List<GameType>,
    // GameType[]).
    private static bool Involves(Type type, AssemblyLoadContext game)
    {
        if (type.HasElementType)
        {
            return Involves(type.GetElementType()!, game);
        }
        if (type.IsGenericType && type.GetGenericArguments().Any(argument => Involves(argument, game)))
        {
            return true;
        }
        return AssemblyLoadContext.GetLoadContext(type.Assembly) == game;
    }

    // The new version's type for an old one: by assembly and full name, and
    // for an instantiation, its definition over its arguments' counterparts.
    [RequiresUnreferencedCode(JitOnly)]
    [RequiresDynamicCode(JitOnly)]
    private static Type? Counterpart(Type old, AssemblyLoadContext oldGame, AssemblyLoadContext newGame)
    {
        if (!Involves(old, oldGame))
        {
            return old;
        }
        if (old.IsSZArray)
        {
            return Counterpart(old.GetElementType()!, oldGame, newGame)?.MakeArrayType();
        }
        if (old.IsConstructedGenericType)
        {
            Type? definition = Counterpart(old.GetGenericTypeDefinition(), oldGame, newGame);
            Type?[] arguments = [.. old.GetGenericArguments().Select(argument => Counterpart(argument, oldGame, newGame))];
            if (definition is null || arguments.Any(argument => argument is null))
            {
                return null;
            }
            try
            {
                return definition.MakeGenericType(arguments!);
            }
            catch (ArgumentException)
            {
                return null; // an argument no longer meets a constraint
            }
        }
        string? assembly = old.Assembly.GetName().Name;
        Assembly? match = newGame.Assemblies.FirstOrDefault(candidate => candidate.GetName().Name == assembly);
        return old.FullName is null ? null : match?.GetType(old.FullName, throwOnError: false);
    }

    [RequiresDynamicCode(JitOnly)]
    private static uint SizeOf(Type type) =>
        (uint)(int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;

    [RequiresDynamicCode(JitOnly)]
    private static bool ContainsReferences(Type type) =>
        (bool)typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!
            .MakeGenericMethod(type).Invoke(null, null)!;

    [RequiresUnreferencedCode(JitOnly)]
    [RequiresDynamicCode(JitOnly)]
    private static uint AlignmentOf(Type type) =>
        (uint)typeof(Components).GetMethod(nameof(Components.AlignmentOf), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(type).Invoke(null, null)!;
}
