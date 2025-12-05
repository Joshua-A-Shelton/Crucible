#ifndef CRUCIBLE_TEXTURESERIALIZER_H
#define CRUCIBLE_TEXTURESERIALIZER_H
#include <slag/Slag.h>
namespace crucible
{
    namespace serialization
    {
        /**
         * Load a new 2D texture from a data stream
         * @param dataStream Texture data in ".ctxr" format
         * @param outTexture Newly loaded texture
         * @return Number of bytes read from the stream
         */
        uint64_t readTexture2D(const unsigned char* dataStream, slag::Texture** outTexture);
        /**
         * Queue a new 2D texture to be created from a data stream
         * @param dataStream Texture data Texture data in ".ctxr" format
         * @param commandBuffer Command buffer to queue the texture creation to. Texture will be finished after the command buffer is finished
         * @param outTexture Newly loaded texture. It's created, but not initialized until the command buffer is finished
         * @param outResourceBuffer New buffer that contains the data the texture is created with. It's returned to the user, and it becomes the user's responsibility to clean up after the command buffer is finished
         * @return
         */
        uint64_t queueTexture2D(const unsigned char* dataStream, slag::CommandBuffer* commandBuffer, slag::Texture** outTexture, slag::Buffer** outResourceBuffer);
        std::vector<unsigned char> encodeTexture2D(slag::Texture* texture);
    } // serialization
} // crucible

#endif //CRUCIBLE_TEXTURESERIALIZER_H
