#include <crucible/importers/ShaderCompiler.h>

#include "Common.h"

#include <crucible/BinaryFile.h>
#include <crucible/Memory.h>
#include <crucible/Shader.h>
#include <crucible/Vector.h>

#include <slang-com-ptr.h>
#include <slang.h>

#include <cstdio>
#include <cstring>
#include <mutex>

namespace crucible::importers {

using Slang::ComPtr;

struct CompiledShader
{
    struct Entry
    {
        render::ShaderStageKind stage;
        uint32_t name; // into strings
        uint32_t threadGroupSize[3];
        Vector<uint8_t> dxil{MemoryTag::Assets};
        Vector<uint8_t> spirv{MemoryTag::Assets};
    };
    struct Struct
    {
        uint32_t name;
        uint32_t size;
        uint32_t firstField;
        uint32_t fieldCount;
    };
    struct Field
    {
        uint32_t name;
        uint32_t offset;
        uint32_t size;
        SharedFieldKind kind;
        uint32_t rows;
        uint32_t columns;
    };
    Vector<Entry*> entries{MemoryTag::Assets};
    Vector<Struct> structs{MemoryTag::Assets};
    Vector<Field> fields{MemoryTag::Assets};
    Vector<SharedField> views{MemoryTag::Assets}; // fields with their strings, filled at the end
    Vector<char> strings{MemoryTag::Assets};
    Vector<uint32_t> stringEnds{MemoryTag::Assets};
    Vector<char> warnings{MemoryTag::Assets};

    ~CompiledShader()
    {
        for (Entry* entry : entries)
        {
            destroy(MemoryTag::Assets, entry);
        }
    }

    uint32_t addString(std::string_view text)
    {
        for (char c : text)
        {
            strings.pushBack(c);
        }
        stringEnds.pushBack(static_cast<uint32_t>(strings.size()));
        return static_cast<uint32_t>(stringEnds.size() - 1);
    }

    std::string_view string(uint32_t index) const
    {
        const uint32_t begin = index == 0 ? 0 : stringEnds[index - 1];
        return {strings.data() + begin, stringEnds[index] - begin};
    }
};

namespace {

// Slang's global session loads its core module once (about a second), so it
// is kept; it isn't thread-safe, so compiles take turns.
std::mutex sessionLock;

slang::IGlobalSession* globalSession()
{
    static ComPtr<slang::IGlobalSession> session;
    if (!session)
    {
        slang::createGlobalSession(session.writeRef());
    }
    return session.get();
}

// The targets, in this order, with Slag's flags (Slag ADR 0034) and ADR
// 0047's: row-major matrices, and D3D12's buffer layouts on Vulkan. HLSL is
// there only for D3D12's layouts, which need no DXC.
enum Target : SlangInt
{
    Spirv,
    Hlsl,
    Dxil,
};

std::string_view text(slang::IBlob* blob)
{
    return blob != nullptr ? std::string_view(static_cast<const char*>(blob->getBufferPointer()), blob->getBufferSize())
                           : std::string_view();
}

Status failWith(const char* what, slang::IBlob* diagnostics)
{
    const std::string_view message = text(diagnostics);
    return fail("%s%s%.*s", what, message.empty() ? "" : ":\n", static_cast<int>(message.size()), message.data());
}

void appendWarnings(CompiledShader& shader, slang::IBlob* diagnostics)
{
    for (char c : text(diagnostics))
    {
        shader.warnings.pushBack(c);
    }
}

// ---------------------------------------------------------------------------
// Shared structs
// ---------------------------------------------------------------------------

// One field's layout under one set of rules.
struct Placed
{
    const char* path;
    uint32_t offset;
    uint32_t size;
};

void flatten(slang::TypeLayoutReflection* layout, uint32_t base, Vector<Placed>& out)
{
    for (unsigned int i = 0; i < layout->getFieldCount(); ++i)
    {
        slang::VariableLayoutReflection* field = layout->getFieldByIndex(i);
        slang::TypeLayoutReflection* type = field->getTypeLayout();
        const uint32_t offset = base + static_cast<uint32_t>(field->getOffset());
        out.pushBack({field->getName(), offset, static_cast<uint32_t>(type->getSize())});
    }
}

// The first field placed differently, or -1 when the layouts match (sizes
// included: then `a.size()` for a size difference).
int64_t firstDifference(const Vector<Placed>& a, uint32_t aSize, const Vector<Placed>& b, uint32_t bSize)
{
    for (size_t i = 0; i < a.size() && i < b.size(); ++i)
    {
        if (a[i].offset != b[i].offset || a[i].size != b[i].size)
        {
            return static_cast<int64_t>(i);
        }
    }
    return a.size() == b.size() && aSize == bSize ? -1 : static_cast<int64_t>(a.size());
}

Status describeField(CompiledShader& shader, std::string_view structName, slang::VariableLayoutReflection* field)
{
    slang::TypeLayoutReflection* layout = field->getTypeLayout();
    slang::TypeReflection* type = layout->getType();
    CompiledShader::Field out{};
    out.name = shader.addString(field->getName());
    out.offset = static_cast<uint32_t>(field->getOffset());
    out.size = static_cast<uint32_t>(layout->getSize());
    out.rows = 1;
    out.columns = 1;
    slang::TypeReflection* scalar = type;
    switch (type->getKind())
    {
    case slang::TypeReflection::Kind::Scalar:
        break;
    case slang::TypeReflection::Kind::Vector:
        out.columns = static_cast<uint32_t>(type->getElementCount());
        scalar = type->getElementType();
        break;
    case slang::TypeReflection::Kind::Matrix:
        out.rows = type->getRowCount();
        out.columns = type->getColumnCount();
        scalar = type->getElementType();
        break;
    case slang::TypeReflection::Kind::Struct:
        // NVIDIA's Vulkan driver (621.01, 2026-10) places nested members at
        // the outer struct's offset 0 in buffers reached through descriptor
        // heaps; D3D12 and RADV don't. Refused until it's fixed (ADR 0047).
        return fail("%.*s.%s: [GpuShared] structs can't hold structs yet (NVIDIA's Vulkan driver misplaces nested "
                    "members); flatten its fields into this struct",
                    static_cast<int>(structName.size()), structName.data(), field->getName());
    default:
        return fail("%.*s.%s: a [GpuShared] struct can hold scalars, float vectors, and float4x4; arrays and other "
                    "types aren't supported yet",
                    static_cast<int>(structName.size()), structName.data(), field->getName());
    }
    switch (scalar->getScalarType())
    {
    case slang::TypeReflection::ScalarType::Float32:
        out.kind = SharedFieldKind::Float;
        break;
    case slang::TypeReflection::ScalarType::Int32:
        out.kind = SharedFieldKind::Int;
        break;
    case slang::TypeReflection::ScalarType::UInt32:
    case slang::TypeReflection::ScalarType::Bool:
        out.kind = SharedFieldKind::Uint;
        break;
    default:
        return fail("%.*s.%s: only 32-bit scalars are supported in [GpuShared] structs",
                    static_cast<int>(structName.size()), structName.data(), field->getName());
    }
    const bool vector = out.rows == 1 && out.columns > 1;
    const bool matrix = out.rows > 1;
    if ((vector && out.kind != SharedFieldKind::Float) || (matrix && (out.rows != 4 || out.columns != 4)) ||
        (matrix && out.kind != SharedFieldKind::Float))
    {
        return fail("%.*s.%s: vectors must be float, and the only matrix is float4x4",
                    static_cast<int>(structName.size()), structName.data(), field->getName());
    }
    shader.fields.pushBack(out);
    return Status::Ok;
}

// Every [GpuShared] struct the module declares, if its layout is the same as
// a constant buffer member and a structured buffer element, on both targets.
// The layouts are read from a module that declares each struct both ways, as
// a shader would: Slang's per-rule layout queries ignore -fvk-use-dx-layout,
// but its program layouts follow it.
Status describeStructs(CompiledShader& shader, slang::ISession* session, slang::IModule* module)
{
    slang::DeclReflection* declarations = module->getModuleReflection();
    if (declarations == nullptr)
    {
        return fail("Slang has no reflection for the module");
    }
    Vector<slang::TypeReflection*> types(MemoryTag::Assets);
    Vector<char> probe(MemoryTag::Assets);
    auto append = [&](std::string_view text) {
        for (char c : text)
        {
            probe.pushBack(c);
        }
    };
    append("import ");
    append(module->getName());
    append(";\n");
    for (unsigned int i = 0; i < declarations->getChildrenCount(); ++i)
    {
        slang::DeclReflection* declaration = declarations->getChild(i);
        slang::TypeReflection* type =
            declaration->getKind() == slang::DeclReflection::Kind::Struct ? declaration->getType() : nullptr;
        if (type == nullptr || type->findUserAttributeByName("GpuShared") == nullptr)
        {
            continue;
        }
        // Each with a binding of its own, as Slag's flags would give them all one.
        char line[256];
        const int binding = static_cast<int>(types.size()) * 1000;
        std::snprintf(line, sizeof(line),
                      "[[vk::binding(%d, 0)]] ConstantBuffer<%s> crucibleConstant%d;\n"
                      "[[vk::binding(%d, 0)]] RWStructuredBuffer<%s> crucibleStructured%d;\n",
                      binding, type->getName(), binding, binding + 500, type->getName(), binding);
        append(line);
        types.pushBack(type);
    }
    if (types.empty())
    {
        return Status::Ok;
    }
    probe.pushBack('\0');
    ComPtr<slang::IBlob> diagnostics;
    slang::IModule* layoutModule = session->loadModuleFromSourceString(
        "crucibleLayoutProbe", "crucibleLayoutProbe.slang", probe.data(), diagnostics.writeRef());
    ComPtr<slang::IBlob> layoutDiagnostics[2];
    slang::ProgramLayout* layouts[2] = {
        layoutModule != nullptr ? layoutModule->getLayout(Spirv, layoutDiagnostics[0].writeRef()) : nullptr,
        layoutModule != nullptr ? layoutModule->getLayout(Hlsl, layoutDiagnostics[1].writeRef()) : nullptr};

    if (layouts[0] == nullptr || layouts[1] == nullptr)
    {
        return failWith("can't lay out the [GpuShared] structs",
                        layoutModule == nullptr ? diagnostics.get()
                                                : layoutDiagnostics[layouts[0] == nullptr ? 0 : 1].get());
    }

    const char* targetNames[2] = {"Vulkan", "D3D12"};
    const char* ruleNames[2] = {"a constant buffer", "a structured buffer"};
    for (uint32_t t = 0; t < types.size(); ++t)
    {
        const char* name = types[t]->getName();
        // Parameter 2t is the constant buffer, 2t + 1 the structured buffer.
        auto element = [&](int target, int rule) {
            return layouts[target]
                ->getParameterByIndex(t * 2 + static_cast<unsigned int>(rule))
                ->getTypeLayout()
                ->getElementTypeLayout();
        };
        slang::TypeLayoutReflection* reference = element(0, 0);
        Vector<Placed> expected(MemoryTag::Assets);
        flatten(reference, 0, expected);
        const uint32_t expectedSize = static_cast<uint32_t>(reference->getSize());
        for (int target = 0; target < 2; ++target)
        {
            for (int rule = 0; rule < 2; ++rule)
            {
                slang::TypeLayoutReflection* layout = element(target, rule);
                Vector<Placed> placed(MemoryTag::Assets);
                flatten(layout, 0, placed);
                const int64_t differs =
                    firstDifference(expected, expectedSize, placed, static_cast<uint32_t>(layout->getSize()));
                if (differs < 0)
                {
                    continue;
                }
                if (static_cast<size_t>(differs) < expected.size() && static_cast<size_t>(differs) < placed.size())
                {
                    const Placed& want = expected[static_cast<size_t>(differs)];
                    const Placed& got = placed[static_cast<size_t>(differs)];
                    return fail("[GpuShared] %s.%s is at offset %u (%u bytes) in a Vulkan constant buffer, but at "
                                "%u (%u bytes) in %s on %s: add explicit padding so every layout agrees",
                                name, want.path, want.offset, want.size, got.offset, got.size, ruleNames[rule],
                                targetNames[target]);
                }
                return fail("[GpuShared] %s is %u bytes in a Vulkan constant buffer, but %u in %s on %s: add explicit "
                            "padding at the end so every layout agrees",
                            name, expectedSize, static_cast<uint32_t>(layout->getSize()), ruleNames[rule],
                            targetNames[target]);
            }
        }
        CompiledShader::Struct out{shader.addString(name), expectedSize, static_cast<uint32_t>(shader.fields.size()),
                                   reference->getFieldCount()};
        for (unsigned int f = 0; f < reference->getFieldCount(); ++f)
        {
            if (const Status status = describeField(shader, name, reference->getFieldByIndex(f)); status != Status::Ok)
            {
                return status;
            }
        }
        shader.structs.pushBack(out);
    }
    return Status::Ok;
}

// ---------------------------------------------------------------------------
// Entry points
// ---------------------------------------------------------------------------

Status compileEntry(CompiledShader& shader, slang::ISession* session, slang::IModule* module, SlangInt32 index,
                    bool dxil)
{
    ComPtr<slang::IEntryPoint> entryPoint;
    ComPtr<slang::IComponentType> composite;
    ComPtr<slang::IComponentType> linked;
    ComPtr<slang::IBlob> diagnostics;
    module->getDefinedEntryPoint(index, entryPoint.writeRef());
    slang::IComponentType* parts[] = {module, entryPoint.get()};
    if (SLANG_FAILED(session->createCompositeComponentType(parts, 2, composite.writeRef(), diagnostics.writeRef())) ||
        SLANG_FAILED(composite->link(linked.writeRef(), diagnostics.writeRef())))
    {
        return failWith("can't link an entry point", diagnostics);
    }
    slang::EntryPointReflection* reflection = linked->getLayout(Spirv)->getEntryPointByIndex(0);

    auto* entry = create<CompiledShader::Entry>(MemoryTag::Assets);
    shader.entries.pushBack(entry);
    entry->name = shader.addString(reflection->getName());
    switch (reflection->getStage())
    {
    case SLANG_STAGE_VERTEX:
        entry->stage = render::ShaderStageKind::Vertex;
        break;
    case SLANG_STAGE_FRAGMENT:
        entry->stage = render::ShaderStageKind::Pixel;
        break;
    case SLANG_STAGE_COMPUTE:
    {
        entry->stage = render::ShaderStageKind::Compute;
        SlangUInt sizes[3] = {1, 1, 1};
        reflection->getComputeThreadGroupSize(3, sizes);
        for (int axis = 0; axis < 3; ++axis)
        {
            entry->threadGroupSize[axis] = static_cast<uint32_t>(sizes[axis]);
        }
        break;
    }
    default:
        return fail("entry point %s: only vertex, pixel, and compute shaders are supported", reflection->getName());
    }

    const SlangInt targets[2] = {Spirv, Dxil};
    Vector<uint8_t>* outputs[2] = {&entry->spirv, &entry->dxil};
    for (int t = 0; t < (dxil ? 2 : 1); ++t)
    {
        ComPtr<slang::IBlob> code;
        diagnostics = nullptr;
        if (SLANG_FAILED(linked->getEntryPointCode(0, targets[t], code.writeRef(), diagnostics.writeRef())))
        {
            return failWith(t == 0 ? "can't make SPIR-V" : "can't make DXIL", diagnostics);
        }
        appendWarnings(shader, diagnostics);
        outputs[t]->assign(
            std::span<const uint8_t>(static_cast<const uint8_t*>(code->getBufferPointer()), code->getBufferSize()));
    }
    return Status::Ok;
}

} // namespace

// ---------------------------------------------------------------------------
// The API
// ---------------------------------------------------------------------------

Result<CompiledShader*> compileShader(std::string_view path, std::string_view source,
                                      const ShaderCompileOptions& options)
{
    clearError();
    std::lock_guard lock(sessionLock);
    slang::IGlobalSession* global = globalSession();
    if (global == nullptr)
    {
        return fail("Slang didn't start");
    }

    Vector<const char*> arguments(MemoryTag::Assets);
    for (const char* argument : {"-target", "spirv", "-profile", "spirv_1_6", "-capability", "spvDescriptorHeapEXT",
                                 "-spirv-unified-descriptor-heap-stride", "-fvk-b-shift", "0", "0",
                                 "-fvk-use-entrypoint-name", "-fvk-use-dx-layout", "-matrix-layout-row-major",
                                 "-target", "hlsl", "-profile", "sm_6_8", "-matrix-layout-row-major"})
    {
        arguments.pushBack(argument);
    }
    if (options.dxil)
    {
        for (const char* argument : {"-target", "dxil", "-profile", "sm_6_8", "-matrix-layout-row-major"})
        {
            arguments.pushBack(argument);
        }
    }
    slang::SessionDesc description{};
    ComPtr<ISlangUnknown> argumentStorage;
    if (SLANG_FAILED(global->parseCommandLineArguments(static_cast<int>(arguments.size()), arguments.data(),
                                                       &description, argumentStorage.writeRef())))
    {
        return fail("Slang refused the compile options");
    }
    // Search paths, NUL-terminated.
    Vector<char> pathText(MemoryTag::Assets);
    Vector<uint32_t> pathStarts(MemoryTag::Assets);
    for (std::string_view searchPath : options.searchPaths)
    {
        pathStarts.pushBack(static_cast<uint32_t>(pathText.size()));
        for (char c : searchPath)
        {
            pathText.pushBack(c);
        }
        pathText.pushBack('\0');
    }
    Vector<const char*> searchPaths(MemoryTag::Assets);
    for (uint32_t start : pathStarts)
    {
        searchPaths.pushBack(pathText.data() + start);
    }
    description.searchPaths = searchPaths.data();
    description.searchPathCount = static_cast<SlangInt>(searchPaths.size());

    ComPtr<slang::ISession> session;
    if (SLANG_FAILED(global->createSession(description, session.writeRef())))
    {
        return fail("Slang couldn't make a session");
    }

    // The module, named after its file.
    Vector<char> pathCopy(MemoryTag::Assets);
    pathCopy.assign(std::span<const char>(path.data(), path.size()));
    pathCopy.pushBack('\0');
    Vector<char> sourceCopy(MemoryTag::Assets);
    sourceCopy.assign(std::span<const char>(source.data(), source.size()));
    sourceCopy.pushBack('\0');
    size_t nameStart = path.find_last_of("/\\");
    nameStart = nameStart == std::string_view::npos ? 0 : nameStart + 1;
    size_t nameEnd = path.find_last_of('.');
    nameEnd = nameEnd == std::string_view::npos || nameEnd < nameStart ? path.size() : nameEnd;
    Vector<char> moduleName(MemoryTag::Assets);
    moduleName.assign(std::span<const char>(path.data() + nameStart, nameEnd - nameStart));
    moduleName.pushBack('\0');

    ComPtr<slang::IBlob> diagnostics;
    slang::IModule* module = session->loadModuleFromSourceString(moduleName.data(), pathCopy.data(), sourceCopy.data(),
                                                                 diagnostics.writeRef());
    if (module == nullptr)
    {
        return failWith("the shader doesn't compile", diagnostics);
    }

    CompiledShader* shader = create<CompiledShader>(MemoryTag::Assets);
    appendWarnings(*shader, diagnostics);
    Status status = describeStructs(*shader, session.get(), module);
    for (SlangInt32 i = 0; status == Status::Ok && i < module->getDefinedEntryPointCount(); ++i)
    {
        status = compileEntry(*shader, session.get(), module, i, options.dxil);
    }
    if (status != Status::Ok)
    {
        destroyCompiledShader(shader);
        return status;
    }
    for (const CompiledShader::Field& field : shader->fields)
    {
        shader->views.pushBack(
            {shader->string(field.name), field.offset, field.size, field.kind, field.rows, field.columns});
    }
    return shader;
}

void destroyCompiledShader(CompiledShader* shader)
{
    if (shader != nullptr)
    {
        destroy(MemoryTag::Assets, shader);
    }
}

uint32_t compiledEntryCount(const CompiledShader& shader)
{
    return static_cast<uint32_t>(shader.entries.size());
}

std::string_view compiledEntryName(const CompiledShader& shader, uint32_t index)
{
    return index < shader.entries.size() ? shader.string(shader.entries[index]->name) : std::string_view();
}

Result<void> writeShaderFile(const CompiledShader& shader, std::string_view path)
{
    Vector<render::ShaderEntryInfo> entries(MemoryTag::Assets);
    Vector<uint8_t> names(MemoryTag::Assets), dxil(MemoryTag::Assets), spirv(MemoryTag::Assets);
    for (const CompiledShader::Entry* entry : shader.entries)
    {
        const std::string_view name = shader.string(entry->name);
        render::ShaderEntryInfo info{};
        info.stage = entry->stage;
        info.nameOffset = static_cast<uint32_t>(names.size());
        info.nameLength = static_cast<uint32_t>(name.size());
        for (char c : name)
        {
            names.pushBack(static_cast<uint8_t>(c));
        }
        // Each blob 4-byte aligned, as SPIR-V's words are.
        while (dxil.size() % 4 != 0)
        {
            dxil.pushBack(0);
        }
        while (spirv.size() % 4 != 0)
        {
            spirv.pushBack(0);
        }
        info.dxilOffset = static_cast<uint32_t>(dxil.size());
        info.dxilSize = static_cast<uint32_t>(entry->dxil.size());
        info.spirvOffset = static_cast<uint32_t>(spirv.size());
        info.spirvSize = static_cast<uint32_t>(entry->spirv.size());
        std::memcpy(info.threadGroupSize, entry->threadGroupSize, sizeof(info.threadGroupSize));
        for (uint8_t value : entry->dxil)
        {
            dxil.pushBack(value);
        }
        for (uint8_t value : entry->spirv)
        {
            spirv.pushBack(value);
        }
        entries.pushBack(info);
    }

    binary::Writer* writer = binary::createWriter(render::shaderFileKind, render::shaderFileVersion);
    const std::span<const uint8_t> entryBytes(reinterpret_cast<const uint8_t*>(entries.data()),
                                              entries.size() * sizeof(render::ShaderEntryInfo));
    Result<void> result = binary::addSection(*writer, binary::tag("ENTS"), 1, entryBytes, 16);
    if (result)
    {
        result = binary::addSection(*writer, binary::tag("NAME"), 1, names.span(), 16);
    }
    if (result && !dxil.empty())
    {
        result = binary::addSection(*writer, binary::tag("DXIL"), 1, dxil.span(), 16);
    }
    if (result)
    {
        result = binary::addSection(*writer, binary::tag("SPRV"), 1, spirv.span(), 16);
    }
    if (result)
    {
        result = binary::writeFile(*writer, path, binary::Compression::Small);
    }
    binary::destroyWriter(writer);
    return result;
}

uint32_t sharedStructCount(const CompiledShader& shader)
{
    return static_cast<uint32_t>(shader.structs.size());
}

std::string_view sharedStructName(const CompiledShader& shader, uint32_t index)
{
    return shader.string(shader.structs[index].name);
}

uint32_t sharedStructSize(const CompiledShader& shader, uint32_t index)
{
    return shader.structs[index].size;
}

std::span<const SharedField> sharedStructFields(const CompiledShader& shader, uint32_t index)
{
    const CompiledShader::Struct& entry = shader.structs[index];
    return {shader.views.data() + entry.firstField, entry.fieldCount};
}

std::string_view compileWarnings(const CompiledShader& shader)
{
    return {shader.warnings.data(), shader.warnings.size()};
}

} // namespace crucible::importers
