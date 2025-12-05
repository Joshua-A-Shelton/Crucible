#include "TextureSerializer.h"
#include <boost/endian/conversion.hpp>
#include <lz4.h>
#include "BufferUtils.h"
namespace crucible
{
    namespace serialization
    {
        uint64_t readTexture2D(const unsigned char* dataStream, slag::Texture** outTexture)
        {
            std::unique_ptr<slag::CommandBuffer> commandBuffer = std::unique_ptr<slag::CommandBuffer>(slag::CommandBuffer::newCommandBuffer(slag::GPUQueue::QueueType::TRANSFER));
            std::unique_ptr<slag::Semaphore> finishedSemaphore = std::unique_ptr<slag::Semaphore>(slag::Semaphore::newSemaphore(0));
            slag::Buffer* resourceBuffer = nullptr;
            commandBuffer->begin();
            auto readAmount = queueTexture2D(dataStream,commandBuffer.get(),outTexture,&resourceBuffer);
            if (readAmount == 0)
            {
                delete resourceBuffer;
                return 0;
            }
            commandBuffer->end();
            auto finishedPtr = finishedSemaphore.get();
            auto cmdBuffer = commandBuffer.get();
            slag::SemaphoreValue signal = {.semaphore = finishedPtr,.value = 1};
            slag::QueueSubmissionBatch submissionBatch
            {
                .waitSemaphores = nullptr,
                .waitSemaphoreCount = 0,
                .commandBuffers = &cmdBuffer,
                .commandBufferCount = 1,
                .signalSemaphores = &signal,
                .signalSemaphoreCount = 1,
            };
            slag::slagGraphicsCard()->transferQueue()->submit(&submissionBatch,1);
            finishedSemaphore->waitForValue(1);
            delete resourceBuffer;
            return readAmount;

        }

        uint64_t queueTexture2D(const unsigned char* dataStream, slag::CommandBuffer* commandBuffer, slag::Texture** outTexture,slag::Buffer** outResourceBuffer)
        {
            try
            {
                const unsigned char* currentByte = dataStream;
                auto magicNumber = std::string(reinterpret_cast<const char*>(currentByte),5);
                currentByte+=5;
                if (magicNumber != "ctxr\n")
                {
                    return 0;
                }
                auto iterations = 0;
                while ((char)currentByte[iterations]!='\n' && iterations <= 11)
                {
                    iterations++;
                }
                if (iterations == 11)
                {
                    return 0;
                }
                auto versionNumber = std::string(reinterpret_cast<const char*>(currentByte),iterations);
                currentByte+=iterations+1;
                if (versionNumber == "v1.0.0")
                {
                    uint32_t textureFormat = currentByte[0];
                    currentByte+=sizeof(uint32_t);
                    uint32_t width = *(uint32_t*)currentByte;
                    currentByte+=sizeof(uint32_t);
                    uint32_t height = *(uint32_t*)currentByte;
                    currentByte+=sizeof(uint32_t);
                    uint32_t mipLevels = *(uint32_t*)currentByte;
                    currentByte+=sizeof(uint32_t);
                    int32_t compressedLength = *(int32_t*)currentByte;
                    currentByte+=sizeof(int32_t);
                    int32_t uncompressedLength = *(int32_t*)currentByte;
                    currentByte+=sizeof(int32_t);
                    if constexpr (std::endian::native == std::endian::big)
                    {
                        boost::endian::big_to_native_inplace(textureFormat);
                        boost::endian::big_to_native_inplace(width);
                        boost::endian::big_to_native_inplace(height);
                        boost::endian::big_to_native_inplace(mipLevels);
                        boost::endian::big_to_native_inplace(compressedLength);
                        boost::endian::big_to_native_inplace(uncompressedLength);
                    }
                    slag::Pixels::Format format = static_cast<slag::Pixels::Format>(textureFormat);
                    *outResourceBuffer = slag::Buffer::newBuffer(uncompressedLength,slag::Buffer::Accessibility::CPU_AND_GPU);
                    LZ4_decompress_safe(reinterpret_cast<const char*>(currentByte),(char*)(*outResourceBuffer)->cpuHandle(),compressedLength,uncompressedLength);
                    *outTexture = slag::Texture::newTexture(format,slag::Texture::Type::TEXTURE_2D,slag::Texture::UsageFlags::SAMPLED_IMAGE,width,height,1,mipLevels,1);

                    std::vector<slag::TextureBufferMapping> mappings(mipLevels);
                    uint64_t bufferOffset = 0;
                    auto aspects = slag::Pixels::aspectFlags(format);
                    aspects = aspects & (slag::Pixels::AspectFlags::COLOR | slag::Pixels::AspectFlags::DEPTH);
                    for (uint32_t i = 0; i < mipLevels; i++)
                    {
                        auto& mapping = mappings[i];
                        mapping.bufferOffset = bufferOffset;
                        mapping.textureSubresource =
                        {
                            .aspectFlags = aspects,
                            .mipLevel = i,
                            .baseArrayLayer = 0,
                            .layerCount = 1
                        };
                        mapping.textureExtent = {.width = (*outTexture)->width(i),.height=(*outTexture)->height(i),.depth=1};
                        mapping.textureOffset = {0,0,0};

                        bufferOffset+=(*outTexture)->byteSize(i);
                    }

                    commandBuffer->copyBufferToTexture(*outResourceBuffer,*outTexture,mappings.data(),mappings.size());

                    currentByte+=compressedLength;
                    return currentByte - dataStream;

                }
                return 0;
            }
            catch (...)
            {
                if (*outTexture!=nullptr)
                {
                    delete (*outTexture);
                    *outTexture = nullptr;
                }
                if (*outResourceBuffer!=nullptr)
                {
                    delete (*outResourceBuffer);
                    *outResourceBuffer = nullptr;
                }
                throw;
            }

        }

        std::vector<unsigned char> encodeTexture2D(slag::Texture* texture)
        {

            auto headerSize = sizeof(uint32_t)*6+12;
            slag::Buffer* dataBuffer = slag::Buffer::newBuffer(texture->byteSize(),slag::Buffer::Accessibility::CPU_AND_GPU);
            slag::CommandBuffer* commandBuffer = slag::CommandBuffer::newCommandBuffer(slag::GPUQueue::QueueType::TRANSFER);
            slag::Semaphore* finished = slag::Semaphore::newSemaphore(0);
            std::vector<slag::TextureBufferMapping> mappings(texture->mipLevels());
            uint64_t bufferOffset = 0;
            auto aspects = slag::Pixels::aspectFlags(texture->format());
            aspects = aspects & (slag::Pixels::AspectFlags::COLOR | slag::Pixels::AspectFlags::DEPTH);
            for (uint32_t i = 0; i < texture->mipLevels(); i++)
            {
                auto& mapping = mappings[i];
                mapping.bufferOffset = bufferOffset;
                mapping.textureSubresource =
                {
                    .aspectFlags = aspects,
                    .mipLevel = i,
                    .baseArrayLayer = 0,
                    .layerCount = 1
                };
                mapping.textureExtent = {.width = texture->width(i),.height=texture->height(i),.depth=1};
                mapping.textureOffset = {0,0,0};

                bufferOffset+=texture->byteSize(i);
            }

            commandBuffer->begin();
            commandBuffer->copyTextureToBuffer(texture,dataBuffer,mappings.data(),mappings.size());
            commandBuffer->end();

            slag::SemaphoreValue signal{.semaphore = finished,.value = 1};
            slag::QueueSubmissionBatch submissionBatch
            {
                .waitSemaphores = nullptr,
                .waitSemaphoreCount = 0,
                .commandBuffers = &commandBuffer,
                .commandBufferCount = 1,
                .signalSemaphores = &signal,
                .signalSemaphoreCount = 1,
            };
            slag::slagGraphicsCard()->transferQueue()->submit(&submissionBatch,1);

            finished->waitForValue(1);

            auto compressedSize = LZ4_compressBound(dataBuffer->size());
            std::vector<unsigned char> compressedBuffer(compressedSize+headerSize);

            compressedSize = LZ4_compress_fast(dataBuffer->as<char>(),(char*)compressedBuffer.data()+headerSize,dataBuffer->size(),compressedSize,1);
            compressedBuffer.resize(compressedSize+headerSize);

            size_t bufferIndex = 0;
            bufferSet(compressedBuffer,bufferIndex,"ctxr\n");
            bufferIndex+=5;
            bufferSet(compressedBuffer,bufferIndex,"v1.0.0\n");
            bufferIndex+=7;

            auto textureFormat = texture->format();
            uint32_t width = texture->width();
            uint32_t height = texture->height();
            uint32_t mipLevels = texture->mipLevels();
            int32_t compressedLength = compressedSize;
            int32_t uncompressedLength = texture->byteSize();
            if constexpr (std::endian::native == std::endian::big)
            {
                boost::endian::native_to_little_inplace(textureFormat);
                boost::endian::native_to_little_inplace(width);
                boost::endian::native_to_little_inplace(height);
                boost::endian::native_to_little_inplace(mipLevels);
                boost::endian::native_to_little_inplace(compressedLength);
                boost::endian::native_to_little_inplace(uncompressedLength);
            }
            bufferSet(compressedBuffer,bufferIndex,textureFormat);
            bufferIndex+=sizeof(uint32_t);
            bufferSet(compressedBuffer,bufferIndex,width);
            bufferIndex+=sizeof(uint32_t);
            bufferSet(compressedBuffer,bufferIndex,height);
            bufferIndex+=sizeof(uint32_t);
            bufferSet(compressedBuffer,bufferIndex,mipLevels);
            bufferIndex+=sizeof(uint32_t);
            bufferSet(compressedBuffer,bufferIndex,compressedLength);
            bufferIndex+=sizeof(uint32_t);
            bufferSet(compressedBuffer,bufferIndex,uncompressedLength);

            delete finished;
            delete commandBuffer;
            delete dataBuffer;

            return compressedBuffer;
        }
    } // serialization
} // crucible
