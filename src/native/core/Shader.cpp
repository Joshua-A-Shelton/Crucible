#include <crucible/Memory.h>
#include <crucible/Shader.h>
#include <crucible/Vector.h>

#include <slag/Pipeline.h>

#include <cstring>

namespace crucible::render {

struct ShaderFile
{
    Vector<ShaderEntryInfo> entries{MemoryTag::Render};
    Vector<char> names{MemoryTag::Render};          // each NUL-terminated too, for Slag's entry point name
    Vector<uint32_t> nameStarts{MemoryTag::Render}; // into names
    Vector<uint8_t> dxil{MemoryTag::Render};
    Vector<uint8_t> spirv{MemoryTag::Render};
    Vector<slag::ShaderBinary> binaries{MemoryTag::Render}; // two slots per entry
};

namespace {

Status readInto(const binary::File& file, uint32_t tag, Vector<uint8_t>& out)
{
    const int32_t index = binary::findSection(file, tag);
    if (index < 0)
    {
        return Status::Ok;
    }
    out.resize(binary::sectionInfo(file, static_cast<uint32_t>(index)).size);
    const Result<void> read = binary::readSection(file, static_cast<uint32_t>(index), out.span());
    return read.status();
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
    Status status = readInto(file, binary::tag("ENTS"), entries);
    if (status == Status::Ok)
    {
        status = readInto(file, binary::tag("NAME"), names);
    }
    if (status == Status::Ok)
    {
        status = readInto(file, binary::tag("DXIL"), shader->dxil);
    }
    if (status == Status::Ok)
    {
        status = readInto(file, binary::tag("SPRV"), shader->spirv);
    }
    binary::closeFile(opened.value());
    if (status != Status::Ok || entries.size() % sizeof(ShaderEntryInfo) != 0)
    {
        destroyShaderFile(shader);
        return status != Status::Ok ? status : Status::InvalidData;
    }

    shader->entries.resize(entries.size() / sizeof(ShaderEntryInfo));
    std::memcpy(shader->entries.data(), entries.data(), entries.size());
    for (const ShaderEntryInfo& entry : shader->entries)
    {
        const bool fits = uint64_t{entry.nameOffset} + entry.nameLength <= names.size() &&
                          uint64_t{entry.dxilOffset} + entry.dxilSize <= shader->dxil.size() &&
                          uint64_t{entry.spirvOffset} + entry.spirvSize <= shader->spirv.size() &&
                          entry.stage <= ShaderStageKind::Compute;
        if (!fits)
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
    }
    for (const ShaderEntryInfo& entry : shader->entries)
    {
        shader->binaries.pushBack({slag::ShaderFormat::Dxil, shader->dxil.data() + entry.dxilOffset, entry.dxilSize});
        shader->binaries.pushBack(
            {slag::ShaderFormat::Spirv, shader->spirv.data() + entry.spirvOffset, entry.spirvSize});
    }
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

void shaderStage(const ShaderFile& file, uint32_t index, slag::ShaderStage& stage)
{
    const ShaderEntryInfo& entry = file.entries[index];
    // A missing format is left out, so Slag reports what was offered.
    const slag::ShaderBinary* pair = file.binaries.data() + index * 2;
    std::span<const slag::ShaderBinary> binaries(pair, 2);
    if (entry.dxilSize == 0)
    {
        binaries = binaries.subspan(1);
    }
    stage = {binaries, file.names.data() + file.nameStarts[index]};
}

} // namespace crucible::render
