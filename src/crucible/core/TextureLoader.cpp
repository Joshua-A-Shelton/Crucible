#include "TextureLoader.h"

#include <lz4.h>
#include <boost/endian/conversion.hpp>

#include "serialization/Serializer.h"
#include "slag/GraphicsCard.h"
#include "slag/SlagLib.h"

namespace crucible
{
    namespace core
    {
        slag::Texture* TextureLoader::loadTexture(unsigned char* textureData, unsigned long& readLength, slag::Texture::Layout inLayout,slag::BarrierAccess withAccess, slag::PipelineStages syncWork)
        {
            auto commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GpuQueue::TRANSFER);
            commandBuffer->begin();
            auto texture = loadTexture(textureData, readLength, inLayout,withAccess,syncWork,commandBuffer);
            commandBuffer->end();
            slag::SlagLib::graphicsCard()->transferQueue()->submit(commandBuffer);
            commandBuffer->waitUntilFinished();
            delete commandBuffer;
            return texture;
        }

        slag::Texture* TextureLoader::loadTexture(unsigned char* textureData, unsigned long& readLength, slag::Texture::Layout inLayout,slag::BarrierAccess withAccess, slag::PipelineStages syncWork, slag::CommandBuffer* commandBuffer)
        {
            readLength=0;
            slag::Texture* texture = nullptr;
            unsigned char* currentByte = textureData;
            auto magicNumber = std::string(reinterpret_cast<char*>(currentByte),5);
            currentByte+=5;
            if (magicNumber!="ctxr\n")
            {
                throw std::runtime_error("Attempted to load texture data not in Crucible Texture Format");
            }
            uint8_t textureType = currentByte[0];
            currentByte+=sizeof(uint8_t);
            slag::Pixels::Format format = static_cast<slag::Pixels::Format>(textureType);
            if (format==slag::Pixels::UNDEFINED)
            {
                return nullptr;
            }
            uint8_t textureKind = currentByte[0];
            currentByte+=sizeof(uint8_t);
            slag::Texture::Type type = static_cast<slag::Texture::Type>(textureKind);
            uint32_t width = *(uint32_t*)currentByte;
            currentByte+=sizeof(uint32_t);
            uint32_t height = *(uint32_t*)currentByte;
            currentByte+=sizeof(uint32_t);
            uint32_t depth = *(uint32_t*)currentByte;
            currentByte+=sizeof(uint32_t);
            uint32_t mipLevels = *(uint32_t*)currentByte;
            currentByte+=sizeof(uint32_t);
            if constexpr (std::endian::native == std::endian::big)
            {
                boost::endian::big_to_native_inplace(width);
                boost::endian::big_to_native_inplace(height);
                boost::endian::big_to_native_inplace(depth);
                boost::endian::big_to_native_inplace(mipLevels);
            }
            texture = slag::Texture::newTexture(format,type,width,height,mipLevels,depth,1,slag::TextureUsageFlags::SAMPLED_IMAGE);
            commandBuffer->insertBarrier(
                slag::ImageBarrier
                {
                    .texture = texture,
                    .baseLayer = 0,
                    .layerCount = depth,
                    .baseMipLevel = 0,
                    .mipCount = mipLevels,
                    .oldLayout = slag::Texture::Layout::UNDEFINED,
                    .newLayout = slag::Texture::Layout::TRANSFER_DESTINATION,
                    .accessBefore = slag::BarrierAccessFlags::NONE,
                    .accessAfter = slag::BarrierAccessFlags::TRANSFER_WRITE,
                    .syncBefore = slag::PipelineStageFlags::NONE,
                    .syncAfter = slag::PipelineStageFlags::TRANSFER,
                });
            for (auto layer=0; layer<depth; layer++)
            {
                for (auto mipLevel=0; mipLevel<mipLevels; mipLevel++)
                {
                    int32_t compressedLength = *(int32_t*)currentByte;
                    currentByte+=sizeof(int32_t);
                    int32_t uncompressedLength = *(int32_t*)currentByte;
                    currentByte+=sizeof(int32_t);
                    if constexpr (std::endian::native == std::endian::big)
                    {
                        boost::endian::big_to_native_inplace(compressedLength);
                        boost::endian::big_to_native_inplace(uncompressedLength);
                    }
                    auto buffer = std::unique_ptr<slag::Buffer>(slag::Buffer::newBuffer(uncompressedLength,slag::Buffer::CPU_AND_GPU,slag::Buffer::DATA_BUFFER));
                    auto result = LZ4_decompress_safe((char*)currentByte, reinterpret_cast<char*>(buffer->cpuHandle()),compressedLength,uncompressedLength);
                    currentByte+=compressedLength;
                    if (result<0)
                    {
                        return texture;
                    }
                    commandBuffer->copyBufferToImage(buffer.get(),0,texture,slag::Texture::TRANSFER_DESTINATION,layer,mipLevel);
                }
            }
            commandBuffer->insertBarrier(
                slag::ImageBarrier
                {
                    .texture = texture,
                    .baseLayer = 0,
                    .layerCount = depth,
                    .baseMipLevel = 0,
                    .mipCount = mipLevels,
                    .oldLayout = slag::Texture::Layout::TRANSFER_DESTINATION,
                    .newLayout = inLayout,
                    .accessBefore = slag::BarrierAccessFlags::TRANSFER_WRITE,
                    .accessAfter = withAccess,
                    .syncBefore = slag::PipelineStageFlags::TRANSFER,
                    .syncAfter = syncWork,
                });
            readLength = ((uintptr_t)currentByte)-((uintptr_t)textureData);
            return texture;
        }

        std::vector<unsigned char> TextureLoader::toLZ4(slag::Texture* texture, slag::Texture::Layout startingLayout, slag::Texture::Layout endingLayout, slag::BarrierAccess accessAfter, slag::PipelineStages syncAfter)
        {
            auto commandBuffer = std::unique_ptr<slag::CommandBuffer>(slag::CommandBuffer::newCommandBuffer(slag::GpuQueue::TRANSFER));
            commandBuffer->begin();
            if (startingLayout!=slag::Texture::Layout::TRANSFER_SOURCE)
            {
                commandBuffer->insertBarrier(
                slag::ImageBarrier
                {
                    .texture = texture,
                    .baseLayer = 0,
                    .layerCount = texture->layers(),
                    .baseMipLevel = 0,
                    .mipCount = texture->mipLevels(),
                    .oldLayout = startingLayout,
                    .newLayout = slag::Texture::TRANSFER_SOURCE,
                    .accessBefore = slag::BarrierAccessFlags::NONE,
                    .accessAfter = slag::BarrierAccessFlags::TRANSFER_WRITE,
                    .syncBefore = slag::PipelineStageFlags::NONE,
                    .syncAfter = slag::PipelineStageFlags::NONE,
                });
            }

            std::vector<unsigned char> result;
            result.push_back('c');
            result.push_back('t');
            result.push_back('x');
            result.push_back('r');
            result.push_back('\n');
            uint8_t format = texture->format();
            uint8_t type = texture->type();
            uint32_t width = texture->width();
            uint32_t height = texture->height();
            uint32_t depth = texture->layers();
            uint32_t mipLevels = texture->mipLevels();
            if constexpr (std::endian::native == std::endian::big)
            {
                boost::endian::big_to_native_inplace(width);
                boost::endian::big_to_native_inplace(height);
                boost::endian::big_to_native_inplace(depth);
                boost::endian::big_to_native_inplace(mipLevels);
            }
            bufferInsert(result,format);
            bufferInsert(result,type);
            bufferInsert(result,width);
            bufferInsert(result,height);
            bufferInsert(result,depth);
            bufferInsert(result,mipLevels);
            auto pixelSize = slag::Pixels::pixelBytes(texture->format());
            std::vector<std::unique_ptr<slag::Buffer>> buffers;
            for (auto layer=0; layer<depth; layer++)
            {
                for (auto mipLevel=0; mipLevel<mipLevels; mipLevel++)
                {
                    auto w = texture->width()>>mipLevel;
                    auto h = texture->height()>>mipLevel;
                    auto bufferSize = w*h*pixelSize;
                    auto buffer = std::unique_ptr<slag::Buffer>(slag::Buffer::newBuffer(bufferSize,slag::Buffer::CPU_AND_GPU,slag::Buffer::DATA_BUFFER));
                    commandBuffer->copyImageToBuffer(texture,slag::Texture::TRANSFER_SOURCE,layer,1,mipLevel,buffer.get(),0);
                    buffers.push_back(std::move(buffer));
                }
            }

            commandBuffer->insertBarrier(
                slag::ImageBarrier
                {
                    .texture = texture,
                    .baseLayer = 0,
                    .layerCount = texture->layers(),
                    .baseMipLevel = 0,
                    .mipCount = texture->mipLevels(),
                    .oldLayout = slag::Texture::TRANSFER_SOURCE,
                    .newLayout = endingLayout,
                    .accessBefore = slag::BarrierAccessFlags::TRANSFER_WRITE,
                    .accessAfter = accessAfter,
                    .syncBefore = slag::PipelineStageFlags::TRANSFER,
                    .syncAfter = syncAfter,
                });
            commandBuffer->end();
            slag::SlagLib::graphicsCard()->transferQueue()->submit(commandBuffer.get());
            commandBuffer->waitUntilFinished();
            size_t bufferIndex = 0;
            for (auto layer=0; layer<depth; layer++)
            {
                for (auto mipLevel=0; mipLevel<mipLevels; mipLevel++)
                {
                    auto& buffer = buffers[bufferIndex];
                    auto compressedSize = LZ4_compressBound(static_cast<int32_t>(buffer->size()));
                    std::vector<char> compressedData(compressedSize);
                    compressedSize =LZ4_compress_fast(reinterpret_cast<const char*>(buffer->cpuHandle()),compressedData.data(),static_cast<int32_t>(buffer->size()),compressedSize,1);
                    auto compressedAuto = compressedSize;
                    auto decompressedSize = static_cast<int32_t>(buffer->size());
                    if constexpr (std::endian::native == std::endian::big)
                    {
                        boost::endian::native_to_little_inplace(compressedSize);
                        boost::endian::native_to_little_inplace(decompressedSize);
                    }
                    bufferInsert(result,compressedSize);
                    bufferInsert(result,decompressedSize);
                    result.insert(result.end(),compressedData.begin(),compressedData.begin()+compressedAuto);
                    bufferIndex++;
                }
            }
            return result;
        }
    } // core
} // crucible
