// The shader compiler (ADR 0047): what it accepts and how it refuses.

#include <crucible/importers/ShaderCompiler.h>
#include <crucible/importers/Texture.h>

#include <doctest/doctest.h>

#include <ostream>
#include <string>
#include <string_view>

using namespace crucible;
using namespace crucible::importers;

namespace {

Result<CompiledShader*> compile(std::string_view source)
{
    const std::string_view searchPaths[] = {CRUCIBLE_SHADER_LIBRARY_DIR};
    return compileShader("Test.slang", source, {searchPaths, false});
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
