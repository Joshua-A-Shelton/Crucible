#include <crucible/Memory.h>
#include <crucible/Shader.h>
#include <crucible/Vector.h>

#include <slag/Pipeline.h>

#include <cstring>

namespace crucible::render {

static_assert(static_cast<uint8_t>(slag::ShaderFormat::Dxil) == static_cast<uint8_t>(ShaderBinaryFormat::Dxil));
static_assert(static_cast<uint8_t>(slag::ShaderFormat::Spirv) == static_cast<uint8_t>(ShaderBinaryFormat::Spirv));

struct ShaderFile
{
    Vector<ShaderEntryInfo> entries{MemoryTag::Render};
    Vector<char> names{MemoryTag::Render};            // each NUL-terminated too, for Slag's entry point name
    Vector<uint32_t> nameStarts{MemoryTag::Render};   // into names
    Vector<uint8_t> formats{MemoryTag::Render};       // in order of preference
    Vector<Vector<uint8_t>*> code{MemoryTag::Render}; // one section per format
    // Per entry point, its binaries (one per format it has code in), back to back.
    Vector<slag::ShaderBinary> binaries{MemoryTag::Render};
    Vector<uint32_t> binaryStarts{MemoryTag::Render}; // entry count + 1

    ~ShaderFile()
    {
        for (Vector<uint8_t>* section : code)
        {
            destroy(MemoryTag::Render, section);
        }
    }
};

namespace {

Status readSection(const binary::File& file, uint32_t index, Vector<uint8_t>& out)
{
    out.resize(binary::sectionInfo(file, index).size);
    return binary::readSection(file, index, out.span()).status();
}

// The format a CDxx tag names, or -1 for another tag.
int32_t formatOf(uint32_t tag)
{
    auto hex = [](uint32_t letter) -> int32_t {
        return letter >= '0' && letter <= '9'   ? static_cast<int32_t>(letter - '0')
               : letter >= 'A' && letter <= 'F' ? static_cast<int32_t>(letter - 'A' + 10)
                                                : -1;
    };
    if ((tag & 0xFFFF) != (binary::tag("CD00") & 0xFFFF))
    {
        return -1;
    }
    const int32_t high = hex((tag >> 16) & 0xFF), low = hex(tag >> 24);
    return high < 0 || low < 0 ? -1 : high * 16 + low;
}

} // namespace

Result<ShaderFile*> loadShaderFile(std::span<const uint8_t> bytes)
{
    Result<binary::File*> opened = binary::parseFile(bytes, shaderFileKind, shaderFileVersion);
    if (!opened)
    {
        return opened.status();
    }
    binary::File& file = *opened.value();
    ShaderFile* shader = create<ShaderFile>(MemoryTag::Render);
    Vector<uint8_t> entries(MemoryTag::Render), names(MemoryTag::Render);
    Status status = Status::Ok;
    for (uint32_t i = 0; status == Status::Ok && i < binary::sectionCount(file); ++i)
    {
        const uint32_t tag = binary::sectionInfo(file, i).tag;
        if (tag == binary::tag("ENTS"))
        {
            status = readSection(file, i, entries);
        }
        else if (tag == binary::tag("NAME"))
        {
            status = readSection(file, i, names);
        }
        else if (const int32_t format = formatOf(tag); format >= 0)
        {
            shader->formats.pushBack(static_cast<uint8_t>(format));
            shader->code.pushBack(create<Vector<uint8_t>>(MemoryTag::Render, MemoryTag::Render));
            status = readSection(file, i, *shader->code[shader->code.size() - 1]);
        }
    }
    binary::closeFile(opened.value());
    if (status != Status::Ok || entries.size() % sizeof(ShaderEntryInfo) != 0)
    {
        destroyShaderFile(shader);
        return status != Status::Ok ? status : Status::InvalidData;
    }

    const size_t count = entries.size() / sizeof(ShaderEntryInfo);
    shader->entries.resize(count);
    std::memcpy(shader->entries.data(), entries.data(), entries.size());
    for (const Vector<uint8_t>* section : shader->code)
    {
        if (section->size() < count * sizeof(ShaderCode))
        {
            destroyShaderFile(shader);
            return Status::InvalidData;
        }
    }
    for (uint32_t e = 0; e < count; ++e)
    {
        const ShaderEntryInfo& entry = shader->entries[e];
        if (uint64_t{entry.nameOffset} + entry.nameLength > names.size() || entry.stage > ShaderStageKind::Compute)
        {
            destroyShaderFile(shader);
            return Status::InvalidData;
        }
        shader->nameStarts.pushBack(static_cast<uint32_t>(shader->names.size()));
        for (uint32_t c = 0; c < entry.nameLength; ++c)
        {
            shader->names.pushBack(static_cast<char>(names[entry.nameOffset + c]));
        }
        shader->names.pushBack('\0');

        shader->binaryStarts.pushBack(static_cast<uint32_t>(shader->binaries.size()));
        for (uint32_t f = 0; f < shader->code.size(); ++f)
        {
            const Vector<uint8_t>& section = *shader->code[f];
            ShaderCode code;
            std::memcpy(&code, section.data() + e * sizeof(ShaderCode), sizeof(code));
            if (uint64_t{code.offset} + code.size > section.size())
            {
                destroyShaderFile(shader);
                return Status::InvalidData;
            }
            if (code.size > 0)
            {
                shader->binaries.pushBack(
                    {static_cast<slag::ShaderFormat>(shader->formats[f]), section.data() + code.offset, code.size});
            }
        }
    }
    shader->binaryStarts.pushBack(static_cast<uint32_t>(shader->binaries.size()));
    return shader;
}

void destroyShaderFile(ShaderFile* file)
{
    if (file != nullptr)
    {
        destroy(MemoryTag::Render, file);
    }
}

uint32_t shaderEntryCount(const ShaderFile& file)
{
    return static_cast<uint32_t>(file.entries.size());
}

const ShaderEntryInfo& shaderEntry(const ShaderFile& file, uint32_t index)
{
    return file.entries[index];
}

std::string_view shaderEntryName(const ShaderFile& file, uint32_t index)
{
    return {file.names.data() + file.nameStarts[index], file.entries[index].nameLength};
}

int32_t findShaderEntry(const ShaderFile& file, std::string_view name)
{
    for (uint32_t i = 0; i < file.entries.size(); ++i)
    {
        if (shaderEntryName(file, i) == name)
        {
            return static_cast<int32_t>(i);
        }
    }
    return -1;
}

std::span<const uint8_t> shaderFormats(const ShaderFile& file)
{
    return file.formats.span();
}

void shaderStage(const ShaderFile& file, uint32_t index, slag::ShaderStage& stage)
{
    const uint32_t first = file.binaryStarts[index];
    stage = {std::span<const slag::ShaderBinary>(file.binaries.data() + first, file.binaryStarts[index + 1] - first),
             file.names.data() + file.nameStarts[index]};
}

} // namespace crucible::render
