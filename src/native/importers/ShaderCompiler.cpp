#include <crucible/importers/ShaderCompiler.h>

#include "Common.h"

#include <crucible/BinaryFile.h>
#include <crucible/MaterialFile.h>
#include <crucible/Memory.h>
#include <crucible/Shader.h>
#include <crucible/Vector.h>

#include <slang-com-ptr.h>
#include <slang.h>

#include <algorithm>
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
        Vector<Vector<uint8_t>*> code{MemoryTag::Assets}; // per format, as asked for

        ~Entry()
        {
            for (Vector<uint8_t>* blob : code)
            {
                destroy(MemoryTag::Assets, blob);
            }
        }
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
    // A material type (ADR 0052) and its parameters; defaults are `size`
    // bytes at defaultsStart in `defaults`.
    struct MaterialType
    {
        uint32_t name;
        uint32_t size;
        uint32_t firstParameter;
        uint32_t parameterCount;
        uint32_t defaultsStart;
    };
    struct Parameter
    {
        uint32_t name;
        uint32_t offset;
        assets::MaterialParameterKind kind;
        uint32_t rows;
        uint32_t columns;
        uint32_t arrayLength;
        uint32_t arrayStride;
    };
    struct Section
    {
        uint32_t tag;
        Vector<uint8_t> bytes{MemoryTag::Assets};
    };
    Vector<render::ShaderBinaryFormat> formats{MemoryTag::Assets};
    Vector<Entry*> entries{MemoryTag::Assets};
    Vector<MaterialType> materialTypes{MemoryTag::Assets};
    Vector<Parameter> parameters{MemoryTag::Assets};
    Vector<uint8_t> defaults{MemoryTag::Assets};
    Vector<uint32_t> dependencies{MemoryTag::Assets}; // strings: the files the module read
    Vector<Section*> sections{MemoryTag::Assets};     // the SHDR file's, made on request
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
        for (Section* section : sections)
        {
            destroy(MemoryTag::Assets, section);
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
// there only for D3D12's layouts, which need no DXC; DXIL only when asked.
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

Status compileEntry(CompiledShader& shader, slang::ISession* session, slang::IModule* module, SlangInt32 index)
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

    for (render::ShaderBinaryFormat format : shader.formats)
    {
        const bool dxil = format == render::ShaderBinaryFormat::Dxil;
        ComPtr<slang::IBlob> code;
        diagnostics = nullptr;
        if (SLANG_FAILED(linked->getEntryPointCode(0, dxil ? Dxil : Spirv, code.writeRef(), diagnostics.writeRef())))
        {
            return failWith(dxil ? "can't make DXIL (is DXC beside Slang?)" : "can't make SPIR-V", diagnostics);
        }
        appendWarnings(shader, diagnostics);
        auto* blob = create<Vector<uint8_t>>(MemoryTag::Assets, MemoryTag::Assets);
        entry->code.pushBack(blob);
        blob->assign(
            std::span<const uint8_t>(static_cast<const uint8_t*>(code->getBufferPointer()), code->getBufferSize()));
    }
    return Status::Ok;
}

// ---------------------------------------------------------------------------
// Material types (ADR 0052)
// ---------------------------------------------------------------------------

Status describeParameter(CompiledShader& shader, std::string_view typeName, slang::VariableLayoutReflection* field)
{
    slang::TypeLayoutReflection* layout = field->getTypeLayout();
    slang::TypeReflection* type = layout->getType();
    CompiledShader::Parameter out{};
    out.name = shader.addString(field->getName());
    out.offset = static_cast<uint32_t>(field->getOffset());
    out.rows = 1;
    out.columns = 1;
    auto refuse = [&](const char* why) {
        return fail("%.*s.%s: %s", static_cast<int>(typeName.size()), typeName.data(), field->getName(), why);
    };
    if (type->getKind() == slang::TypeReflection::Kind::Array)
    {
        out.arrayLength = static_cast<uint32_t>(type->getElementCount());
        out.arrayStride = static_cast<uint32_t>(layout->getElementStride(SLANG_PARAMETER_CATEGORY_UNIFORM));
        type = type->getElementType();
        if (out.arrayLength == 0)
        {
            return refuse("arrays need a fixed length");
        }
    }
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
        if (std::string_view(type->getName()) == "TextureSlot")
        {
            out.kind = assets::MaterialParameterKind::Texture;
            shader.parameters.pushBack(out);
            return Status::Ok;
        }
        return refuse("a material's parameters can't hold structs other than TextureSlot");
    default:
        return refuse("a material's parameters are 32-bit scalars, float vectors, float4x4, TextureSlot, and "
                      "fixed-length arrays of them");
    }
    switch (scalar->getScalarType())
    {
    case slang::TypeReflection::ScalarType::Float32:
        out.kind = assets::MaterialParameterKind::Float;
        break;
    case slang::TypeReflection::ScalarType::Int32:
        out.kind = assets::MaterialParameterKind::Int;
        break;
    case slang::TypeReflection::ScalarType::UInt32:
        out.kind = assets::MaterialParameterKind::Uint;
        break;
    default:
        // bool is 4 bytes in shaders but 1 in C#: uint says what it is.
        return refuse("only float, int, and uint scalars are supported (bool: use uint)");
    }
    const bool vector = out.rows == 1 && out.columns > 1;
    const bool matrix = out.rows > 1;
    if ((vector || matrix) && out.kind != assets::MaterialParameterKind::Float)
    {
        return refuse("vectors and matrices must be float");
    }
    if (matrix && (out.rows != 4 || out.columns != 4))
    {
        return refuse("the only matrix is float4x4");
    }
    shader.parameters.pushBack(out);
    return Status::Ok;
}

// A field's initializer, placed at its offset (each array element at its
// stride). Slang packs it with no padding: scalars, vectors, then matrices
// row by row, array elements one after another.
void placeDefault(uint8_t* parameters, const CompiledShader::Parameter& parameter, slang::IBlob* value)
{
    const uint32_t elementBytes =
        parameter.kind == assets::MaterialParameterKind::Texture ? 4 : parameter.rows * parameter.columns * 4;
    const uint32_t count = parameter.arrayLength == 0 ? 1 : parameter.arrayLength;
    if (value->getBufferSize() != uint64_t{elementBytes} * count)
    {
        return; // not a plain value (a struct initializer, say): left zero
    }
    const auto* bytes = static_cast<const uint8_t*>(value->getBufferPointer());
    for (uint32_t e = 0; e < count; ++e)
    {
        std::memcpy(parameters + parameter.offset + e * parameter.arrayStride, bytes + e * elementBytes, elementBytes);
    }
}

// Every struct the module declares that implements IMaterial: its
// parameters' layout (a structured buffer element's, the same on Vulkan and
// D3D12, as ByteAddressBuffer.Load reads it), defaults from its field
// initializers, and its entry points, compiled from a module that
// specializes the library's generic ones.
Status compileMaterials(CompiledShader& shader, slang::ISession* session, slang::IModule* module,
                        std::string_view moduleName)
{
    ComPtr<slang::IBlob> diagnostics;
    slang::ProgramLayout* moduleLayout = module->getLayout(Spirv, diagnostics.writeRef());
    slang::TypeReflection* materialInterface =
        moduleLayout != nullptr ? moduleLayout->findTypeByName("IMaterial") : nullptr;
    if (materialInterface == nullptr)
    {
        return Status::Ok; // it doesn't import crucible.material
    }
    slang::DeclReflection* declarations = module->getModuleReflection();
    Vector<slang::DeclReflection*> materials(MemoryTag::Assets);
    for (unsigned int i = 0; i < declarations->getChildrenCount(); ++i)
    {
        slang::DeclReflection* declaration = declarations->getChild(i);
        if (declaration->getKind() != slang::DeclReflection::Kind::Struct)
        {
            continue;
        }
        slang::TypeReflection* type = declaration->getType();
        if (type != nullptr && moduleLayout->isSubType(type, materialInterface))
        {
            materials.pushBack(declaration);
        }
    }
    if (materials.empty())
    {
        return Status::Ok;
    }

    Vector<char> probe(MemoryTag::Assets);
    Vector<char> entries(MemoryTag::Assets);
    auto append = [](Vector<char>& to, std::string_view text) {
        for (char c : text)
        {
            to.pushBack(c);
        }
    };
    append(probe, "import ");
    append(probe, moduleName);
    append(probe, ";\n");
    append(entries, "import crucible.shared;\nimport crucible.material;\nimport ");
    append(entries, moduleName);
    append(entries, ";\n"
                    "[shader(\"vertex\")]\n"
                    "MaterialVertex materialVertex(uint vertex : SV_VertexID, uint instance : SV_InstanceID,\n"
                    "                              uint firstInstance : SV_StartInstanceLocation)\n"
                    "{\n    return materialVertexMain(vertex, firstInstance + instance);\n}\n");
    for (uint32_t m = 0; m < materials.size(); ++m)
    {
        const char* name = materials[m]->getName();
        char line[512];
        std::snprintf(line, sizeof(line), "[[vk::binding(%u, 0)]] RWStructuredBuffer<%s> crucibleMaterial%u;\n", m,
                      name, m);
        append(probe, line);
        std::snprintf(line, sizeof(line),
                      "[shader(\"fragment\")]\nfloat4 %s_forward(MaterialVertex v, bool frontFace : SV_IsFrontFace) "
                      ": SV_Target\n{\n    return materialForward<%s>(v, frontFace);\n}\n"
                      "[shader(\"fragment\")]\nvoid %s_mask(MaterialVertex v, bool frontFace : SV_IsFrontFace)\n"
                      "{\n    materialMask<%s>(v, frontFace);\n}\n",
                      name, name, name, name);
        append(entries, line);
    }
    probe.pushBack('\0');
    entries.pushBack('\0');

    diagnostics = nullptr;
    slang::IModule* probeModule = session->loadModuleFromSourceString(
        "crucibleMaterialProbe", "crucibleMaterialProbe.slang", probe.data(), diagnostics.writeRef());
    ComPtr<slang::IBlob> layoutDiagnostics[2];
    slang::ProgramLayout* layouts[2] = {
        probeModule != nullptr ? probeModule->getLayout(Spirv, layoutDiagnostics[0].writeRef()) : nullptr,
        probeModule != nullptr ? probeModule->getLayout(Hlsl, layoutDiagnostics[1].writeRef()) : nullptr};
    if (layouts[0] == nullptr || layouts[1] == nullptr)
    {
        return failWith("can't lay out the materials", probeModule == nullptr
                                                           ? diagnostics.get()
                                                           : layoutDiagnostics[layouts[0] == nullptr ? 0 : 1].get());
    }

    for (uint32_t m = 0; m < materials.size(); ++m)
    {
        const char* name = materials[m]->getName();
        slang::TypeLayoutReflection* vulkan =
            layouts[0]->getParameterByIndex(m)->getTypeLayout()->getElementTypeLayout();
        slang::TypeLayoutReflection* d3d12 =
            layouts[1]->getParameterByIndex(m)->getTypeLayout()->getElementTypeLayout();
        Vector<Placed> a(MemoryTag::Assets), b(MemoryTag::Assets);
        flatten(vulkan, 0, a);
        flatten(d3d12, 0, b);
        if (firstDifference(a, static_cast<uint32_t>(vulkan->getSize()), b, static_cast<uint32_t>(d3d12->getSize())) >=
            0)
        {
            return fail("material %s is laid out differently on Vulkan and D3D12", name);
        }
        CompiledShader::MaterialType type{};
        type.name = shader.addString(name);
        type.size = static_cast<uint32_t>(vulkan->getSize());
        type.firstParameter = static_cast<uint32_t>(shader.parameters.size());
        type.parameterCount = vulkan->getFieldCount();
        type.defaultsStart = static_cast<uint32_t>(shader.defaults.size());
        shader.defaults.resize(shader.defaults.size() + type.size);
        for (unsigned int f = 0; f < vulkan->getFieldCount(); ++f)
        {
            if (const Status status = describeParameter(shader, name, vulkan->getFieldByIndex(f)); status != Status::Ok)
            {
                return status;
            }
        }
        // Defaults: the fields' initializers, by name.
        for (unsigned int c = 0; c < materials[m]->getChildrenCount(); ++c)
        {
            slang::DeclReflection* child = materials[m]->getChild(c);
            if (child->getKind() != slang::DeclReflection::Kind::Variable)
            {
                continue;
            }
            slang::VariableReflection* variable = child->asVariable();
            ComPtr<ISlangBlob> value;
            if (SLANG_FAILED(variable->getDefaultValueBlob(value.writeRef())) || !value)
            {
                continue;
            }
            for (uint32_t p = type.firstParameter; p < type.firstParameter + type.parameterCount; ++p)
            {
                if (shader.string(shader.parameters[p].name) == variable->getName())
                {
                    placeDefault(shader.defaults.data() + type.defaultsStart, shader.parameters[p], value.get());
                }
            }
        }
        shader.materialTypes.pushBack(type);
    }

    diagnostics = nullptr;
    slang::IModule* entryModule = session->loadModuleFromSourceString(
        "crucibleMaterialEntries", "crucibleMaterialEntries.slang", entries.data(), diagnostics.writeRef());
    if (entryModule == nullptr)
    {
        return failWith("the material entry points don't compile", diagnostics);
    }
    appendWarnings(shader, diagnostics);
    for (SlangInt32 i = 0; i < entryModule->getDefinedEntryPointCount(); ++i)
    {
        if (const Status status = compileEntry(shader, session, entryModule, i); status != Status::Ok)
        {
            return status;
        }
    }
    return Status::Ok;
}

// The SHDR file's sections, built once.
void buildSections(CompiledShader& shader)
{
    if (!shader.sections.empty())
    {
        return;
    }
    auto add = [&](uint32_t tag) {
        auto* section = create<CompiledShader::Section>(MemoryTag::Assets);
        section->tag = tag;
        shader.sections.pushBack(section);
        return &section->bytes;
    };
    const size_t count = shader.entries.size();
    Vector<uint8_t>& entries = *add(binary::tag("ENTS"));
    Vector<uint8_t>& names = *add(binary::tag("NAME"));
    entries.resize(count * sizeof(render::ShaderEntryInfo));
    for (size_t e = 0; e < count; ++e)
    {
        const std::string_view name = shader.string(shader.entries[e]->name);
        render::ShaderEntryInfo info{};
        info.stage = shader.entries[e]->stage;
        info.nameOffset = static_cast<uint32_t>(names.size());
        info.nameLength = static_cast<uint32_t>(name.size());
        std::memcpy(info.threadGroupSize, shader.entries[e]->threadGroupSize, sizeof(info.threadGroupSize));
        for (char c : name)
        {
            names.pushBack(static_cast<uint8_t>(c));
        }
        std::memcpy(entries.data() + e * sizeof(info), &info, sizeof(info));
    }
    // One section per format: a ShaderCode per entry point, then the code.
    for (size_t f = 0; f < shader.formats.size(); ++f)
    {
        Vector<uint8_t>& section = *add(render::shaderCodeTag(static_cast<uint8_t>(shader.formats[f])));
        section.resize(count * sizeof(render::ShaderCode));
        for (size_t e = 0; e < count; ++e)
        {
            while (section.size() % 4 != 0) // SPIR-V is made of 4-byte words
            {
                section.pushBack(0);
            }
            const Vector<uint8_t>& code = *shader.entries[e]->code[f];
            const render::ShaderCode where{static_cast<uint32_t>(section.size()), static_cast<uint32_t>(code.size())};
            std::memcpy(section.data() + e * sizeof(render::ShaderCode), &where, sizeof(where));
            for (uint8_t value : code)
            {
                section.pushBack(value);
            }
        }
    }
    if (shader.materialTypes.empty())
    {
        return;
    }
    // MTYP (MaterialFile.h).
    Vector<uint8_t>& types = *add(assets::shaderMaterialTypesTag);
    const auto typeCount = static_cast<uint32_t>(shader.materialTypes.size());
    const auto parameterCount = static_cast<uint32_t>(shader.parameters.size());
    const uint32_t headerSize =
        8 + typeCount * sizeof(assets::MaterialTypeInfo) + parameterCount * sizeof(assets::MaterialParameter);
    const uint32_t defaultsStart = headerSize;
    const uint32_t namesStart = defaultsStart + static_cast<uint32_t>(shader.defaults.size());
    types.resize(namesStart);
    std::memcpy(types.data(), &typeCount, 4);
    std::memcpy(types.data() + 4, &parameterCount, 4);
    auto addName = [&](uint32_t string, uint32_t& offset, uint32_t& length) {
        const std::string_view name = shader.string(string);
        offset = static_cast<uint32_t>(types.size());
        length = static_cast<uint32_t>(name.size());
        for (char c : name)
        {
            types.pushBack(static_cast<uint8_t>(c));
        }
    };
    Vector<assets::MaterialTypeInfo> infos(MemoryTag::Assets);
    for (const CompiledShader::MaterialType& type : shader.materialTypes)
    {
        assets::MaterialTypeInfo info{};
        addName(type.name, info.nameOffset, info.nameLength);
        info.size = type.size;
        info.firstParameter = type.firstParameter;
        info.parameterCount = type.parameterCount;
        info.defaultsOffset = defaultsStart + type.defaultsStart;
        infos.pushBack(info);
    }
    Vector<assets::MaterialParameter> parameters(MemoryTag::Assets);
    for (const CompiledShader::Parameter& parameter : shader.parameters)
    {
        assets::MaterialParameter out{};
        addName(parameter.name, out.nameOffset, out.nameLength);
        out.offset = parameter.offset;
        out.kind = parameter.kind;
        out.rows = parameter.rows;
        out.columns = parameter.columns;
        out.arrayLength = parameter.arrayLength;
        out.arrayStride = parameter.arrayStride;
        parameters.pushBack(out);
    }
    std::memcpy(types.data() + 8, infos.data(), infos.size() * sizeof(assets::MaterialTypeInfo));
    std::memcpy(types.data() + 8 + infos.size() * sizeof(assets::MaterialTypeInfo), parameters.data(),
                parameters.size() * sizeof(assets::MaterialParameter));
    if (!shader.defaults.empty())
    {
        std::memcpy(types.data() + defaultsStart, shader.defaults.data(), shader.defaults.size());
    }
}

} // namespace

// ---------------------------------------------------------------------------
// The API
// ---------------------------------------------------------------------------

namespace {

Result<CompiledShader*> compile(std::string_view path, std::string_view source, const ShaderCompileOptions& options,
                                bool materials)
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
    bool dxil = false;
    for (render::ShaderBinaryFormat format : options.formats)
    {
        if (format != render::ShaderBinaryFormat::Dxil && format != render::ShaderBinaryFormat::Spirv)
        {
            return fail("format %u: only DXIL and SPIR-V can be made here", static_cast<unsigned>(format));
        }
        dxil |= format == render::ShaderBinaryFormat::Dxil;
    }
    if (dxil)
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
    shader->formats.assign(options.formats);
    appendWarnings(*shader, diagnostics);
    Status status = describeStructs(*shader, session.get(), module);
    for (SlangInt32 i = 0; status == Status::Ok && i < module->getDefinedEntryPointCount(); ++i)
    {
        status = compileEntry(*shader, session.get(), module, i);
    }
    if (status == Status::Ok && materials)
    {
        status = compileMaterials(*shader, session.get(), module,
                                  std::string_view(moduleName.data(), moduleName.size() - 1));
    }
    if (status != Status::Ok)
    {
        destroyCompiledShader(shader);
        return status;
    }
    for (SlangInt32 i = 0; i < module->getDependencyFileCount(); ++i)
    {
        shader->dependencies.pushBack(shader->addString(module->getDependencyFilePath(i)));
    }
    for (const CompiledShader::Field& field : shader->fields)
    {
        shader->views.pushBack(
            {shader->string(field.name), field.offset, field.size, field.kind, field.rows, field.columns});
    }
    return shader;
}

} // namespace

Result<CompiledShader*> compileShader(std::string_view path, std::string_view source,
                                      const ShaderCompileOptions& options)
{
    return compile(path, source, options, false);
}

Result<CompiledShader*> compileMaterialShader(std::string_view path, std::string_view source,
                                              std::string_view searchPaths, std::string_view formats)
{
    Vector<std::string_view> paths(MemoryTag::Assets);
    while (!searchPaths.empty())
    {
        const size_t end = searchPaths.find('\n');
        const std::string_view one = searchPaths.substr(0, end);
        if (!one.empty())
        {
            paths.pushBack(one);
        }
        searchPaths = end == std::string_view::npos ? std::string_view() : searchPaths.substr(end + 1);
    }
    Vector<render::ShaderBinaryFormat> chosen(MemoryTag::Assets);
    while (!formats.empty())
    {
        const size_t end = formats.find(',');
        const std::string_view one = formats.substr(0, end);
        if (one == "dxil")
        {
            chosen.pushBack(render::ShaderBinaryFormat::Dxil);
        }
        else if (one == "spirv")
        {
            chosen.pushBack(render::ShaderBinaryFormat::Spirv);
        }
        else if (!one.empty())
        {
            clearError();
            return fail("unknown shader format %.*s", static_cast<int>(one.size()), one.data());
        }
        formats = end == std::string_view::npos ? std::string_view() : formats.substr(end + 1);
    }
    return compile(path, source, {.searchPaths = paths.span(), .formats = chosen.span()}, true);
}

uint32_t compiledSectionCount(CompiledShader& shader)
{
    buildSections(shader);
    return static_cast<uint32_t>(shader.sections.size());
}

ShaderSectionInfo compiledSection(CompiledShader& shader, uint32_t index)
{
    buildSections(shader);
    if (index >= shader.sections.size())
    {
        return {};
    }
    return {shader.sections[index]->tag, 1, shader.sections[index]->bytes.size()};
}

void copyCompiledSection(CompiledShader& shader, uint32_t index, std::span<uint8_t> destination)
{
    buildSections(shader);
    if (index < shader.sections.size())
    {
        const Vector<uint8_t>& bytes = shader.sections[index]->bytes;
        std::memcpy(destination.data(), bytes.data(), std::min(destination.size(), bytes.size()));
    }
}

uint32_t compiledDependencyCount(const CompiledShader& shader)
{
    return static_cast<uint32_t>(shader.dependencies.size());
}

std::string_view compiledDependency(const CompiledShader& shader, uint32_t index)
{
    return index < shader.dependencies.size() ? shader.string(shader.dependencies[index]) : std::string_view();
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
    auto& built = const_cast<CompiledShader&>(shader); // the sections are a cache
    buildSections(built);
    binary::Writer* writer = binary::createWriter(render::shaderFileKind, render::shaderFileVersion);
    Result<void> result;
    for (const CompiledShader::Section* section : built.sections)
    {
        if (result)
        {
            result = binary::addSection(*writer, section->tag, 1, section->bytes.span(), 16);
        }
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
