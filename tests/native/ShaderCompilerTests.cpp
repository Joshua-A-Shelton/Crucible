// The shader compiler (ADR 0047): what it accepts and how it refuses.

#include <crucible/importers/ShaderCompiler.h>
#include <crucible/importers/Texture.h>

#include <crucible/MaterialFile.h>

#include <doctest/doctest.h>

#include <algorithm>
#include <cstring>
#include <ostream>
#include <string>
#include <string_view>
#include <vector>

using namespace crucible;
using namespace crucible::importers;

namespace {

Result<CompiledShader*> compile(std::string_view source)
{
    const std::string_view searchPaths[] = {CRUCIBLE_SHADER_LIBRARY_DIR};
    const render::ShaderBinaryFormat formats[] = {render::ShaderBinaryFormat::Spirv};
    return compileShader("Test.slang", source, {searchPaths, formats});
}

std::string error()
{
    return std::string(lastImportError());
}

} // namespace

TEST_CASE("Shared structs come out with their layout")
{
    Result<CompiledShader*> shader = compile(R"(
        import crucible.shared;
        [GpuShared] struct Light { float3 color; float range; float4x4 view; uint flags; int index; float2 cone; };
        struct Private { float4 notShared; };
        [shader("compute")] [numthreads(8, 8, 1)] void main() {}
    )");
    INFO(error());
    REQUIRE(shader);
    REQUIRE(sharedStructCount(*shader.value()) == 1);
    CHECK(sharedStructName(*shader.value(), 0) == "Light");
    CHECK(sharedStructSize(*shader.value(), 0) == 96);
    const auto fields = sharedStructFields(*shader.value(), 0);
    REQUIRE(fields.size() == 6);
    CHECK(fields[0].name == "color");
    CHECK(fields[0].columns == 3);
    CHECK(fields[1].offset == 12);
    CHECK(fields[2].rows == 4);
    CHECK(fields[2].offset == 16);
    CHECK(fields[3].kind == SharedFieldKind::Uint);
    CHECK(fields[4].kind == SharedFieldKind::Int);
    CHECK(fields[5].offset == 88);
    REQUIRE(compiledEntryCount(*shader.value()) == 1);
    CHECK(compiledEntryName(*shader.value(), 0) == "main");
    destroyCompiledShader(shader.value());
}

TEST_CASE("A module without entry points still describes its structs")
{
    Result<CompiledShader*> shader = compile(R"(
        import crucible.shared;
        [GpuShared] struct Pair { float a; float b; };
    )");
    INFO(error());
    REQUIRE(shader);
    CHECK(compiledEntryCount(*shader.value()) == 0);
    CHECK(sharedStructCount(*shader.value()) == 1);
    destroyCompiledShader(shader.value());
}

TEST_CASE("Structs laid out differently by constant and structured buffers are refused")
{
    // A float3 straddling 16 bytes moves in a constant buffer.
    CHECK(compile(R"(
        import crucible.shared;
        [GpuShared] struct Straddle { float2 uv; float3 tint; };
    )")
              .status() == Status::InvalidData);
    CHECK(error().find("Straddle.tint is at offset 16 (12 bytes) in a Vulkan constant buffer, but at 8") !=
          std::string::npos);
    CHECK(error().find("add explicit padding") != std::string::npos);
    // Constant buffers round a struct's size up to 16 bytes in arrays only:
    // a trailing float3 after padding is the same everywhere.
    Result<CompiledShader*> fixed = compile(R"(
        import crucible.shared;
        [GpuShared] struct Fixed { float2 uv; float2 padding; float3 tint; };
    )");
    CHECK(fixed);
    destroyCompiledShader(fixed ? fixed.value() : nullptr);
}

TEST_CASE("Shared structs refuse types they can't mirror")
{
    CHECK_FALSE(compile(R"(
        import crucible.shared;
        [GpuShared] struct WithArray { float values[4]; };
    )"));
    CHECK(error().find("WithArray.values") != std::string::npos);
    CHECK_FALSE(compile(R"(
        import crucible.shared;
        struct Plain { float a; };
        [GpuShared] struct Holder { Plain inner; };
    )"));
    CHECK(error().find("can't hold structs yet") != std::string::npos);
    CHECK_FALSE(compile(R"(
        import crucible.shared;
        [GpuShared] struct Ints { int3 cell; };
    )"));
    CHECK(error().find("vectors must be float") != std::string::npos);
}

TEST_CASE("Compile errors carry Slang's diagnostics")
{
    CHECK(compile("[shader(\"compute\")] [numthreads(1, 1, 1)] void main() { undefinedThing(); }").status() ==
          Status::InvalidData);
    CHECK(error().find("doesn't compile") != std::string::npos);
    CHECK(error().find("undefinedThing") != std::string::npos);
}

namespace {

// The MTYP section (MaterialFile.h), from the compiled shader's sections.
std::vector<uint8_t> materialTypes(CompiledShader& shader)
{
    for (uint32_t i = 0; i < compiledSectionCount(shader); ++i)
    {
        const ShaderSectionInfo info = compiledSection(shader, i);
        if (info.tag == assets::shaderMaterialTypesTag)
        {
            std::vector<uint8_t> bytes(info.size);
            copyCompiledSection(shader, i, bytes);
            return bytes;
        }
    }
    return {};
}

template <class T>
T at(const std::vector<uint8_t>& bytes, size_t offset)
{
    T value;
    std::memcpy(&value, bytes.data() + offset, sizeof(T));
    return value;
}

std::string_view nameAt(const std::vector<uint8_t>& bytes, uint32_t offset, uint32_t length)
{
    return {reinterpret_cast<const char*>(bytes.data()) + offset, length};
}

} // namespace

TEST_CASE("Material shaders describe their materials and get entry points for them")
{
    Result<CompiledShader*> shader = compileMaterialShader("Bricks.slang", R"(
        import crucible.material;
        public struct Brick : IMaterial
        {
            public float4 tint = float4(1, 0.5, 0.25, 1);
            public TextureSlot albedo;
            public float roughness = 0.75;
            public float weights[3] = { 1, 2, 3 };
            public uint flags;
            public typealias Shading = ToonShading;
            public SurfaceData surface(SurfaceInput input)
            {
                SurfaceData s = SurfaceData.defaults(input);
                s.albedo = tint.rgb * albedo.sample(input.uv0, float4(1, 1, 1, 1)).rgb * weights[1];
                s.roughness = roughness;
                return s;
            }
            public ToonData shading(SurfaceInput input) { return ToonData.defaults(); }
        }
        public struct NotAMaterial { float x; }
    )",
                                                           CRUCIBLE_SHADER_LIBRARY_DIR, "spirv");
    INFO(error());
    REQUIRE(shader);

    std::vector<std::string_view> entries;
    for (uint32_t i = 0; i < compiledEntryCount(*shader.value()); ++i)
    {
        entries.push_back(compiledEntryName(*shader.value(), i));
    }
    CHECK(std::find(entries.begin(), entries.end(), "materialVertex") != entries.end());
    CHECK(std::find(entries.begin(), entries.end(), "Brick_forward") != entries.end());
    CHECK(std::find(entries.begin(), entries.end(), "Brick_mask") != entries.end());

    const std::vector<uint8_t> types = materialTypes(*shader.value());
    REQUIRE(types.size() > 8);
    REQUIRE(at<uint32_t>(types, 0) == 1); // NotAMaterial isn't one
    REQUIRE(at<uint32_t>(types, 4) == 5);
    const auto type = at<assets::MaterialTypeInfo>(types, 8);
    CHECK(nameAt(types, type.nameOffset, type.nameLength) == "Brick");
    CHECK(type.size == 40); // packed as a structured buffer element
    const size_t parametersAt = 8 + sizeof(assets::MaterialTypeInfo);
    auto parameter = [&](uint32_t i) {
        return at<assets::MaterialParameter>(types, parametersAt + i * sizeof(assets::MaterialParameter));
    };
    CHECK(nameAt(types, parameter(0).nameOffset, parameter(0).nameLength) == "tint");
    CHECK(parameter(0).columns == 4);
    CHECK(parameter(1).kind == assets::MaterialParameterKind::Texture);
    CHECK(parameter(1).offset == 16);
    CHECK(parameter(2).offset == 20);
    CHECK(parameter(3).arrayLength == 3);
    CHECK(parameter(3).arrayStride == 4);
    CHECK(parameter(3).offset == 24);
    CHECK(parameter(4).kind == assets::MaterialParameterKind::Uint);
    CHECK(parameter(4).offset == 36);

    // Defaults from the initializers; zero where there is none.
    const size_t defaults = type.defaultsOffset;
    CHECK(at<float>(types, defaults + 0) == 1.0f);
    CHECK(at<float>(types, defaults + 8) == 0.25f);
    CHECK(at<uint32_t>(types, defaults + 16) == 0); // the texture
    CHECK(at<float>(types, defaults + 20) == 0.75f);
    CHECK(at<float>(types, defaults + 24 + 2 * 4) == 3.0f);
    destroyCompiledShader(shader.value());
}

TEST_CASE("Material parameters refuse what they can't hold")
{
    Result<CompiledShader*> shader = compileMaterialShader("Bad.slang", R"(
        import crucible.material;
        public struct Bad : IMaterial
        {
            public bool on;
            public typealias Shading = PbrShading;
            public SurfaceData surface(SurfaceInput input) { return SurfaceData.defaults(input); }
            public PbrData shading(SurfaceInput input) { return PbrData.defaults(); }
        }
    )",
                                                           CRUCIBLE_SHADER_LIBRARY_DIR, "spirv");
    CHECK(shader.status() == Status::InvalidData);
    CHECK(error().find("Bad.on") != std::string::npos);
}
