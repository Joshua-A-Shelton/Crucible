#ifndef CRUCIBLE_TEXTURESERIALIZER_H
#define CRUCIBLE_TEXTURESERIALIZER_H
#include <slag/Slag.h>
namespace crucible
{
    namespace serialization
    {
        uint64_t readTexture2D(const unsigned char* dataStream, slag::Texture** outTexture);
        uint64_t queueTexture2D(const unsigned char* dataStream, slag::CommandBuffer* commandBuffer, slag::Texture** outTexture, slag::Buffer** outResourceBuffer);
        std::vector<unsigned char> encodeTexture2D(slag::Texture* texture);
    } // serialization
} // crucible

#endif //CRUCIBLE_TEXTURESERIALIZER_H
