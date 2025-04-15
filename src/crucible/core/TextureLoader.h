#ifndef CRUCIBLE_TEXTURELOADER_H
#define CRUCIBLE_TEXTURELOADER_H
#include "slag/CommandBuffer.h"
#include "slag/Texture.h"

namespace crucible
{
    namespace core
    {
        class TextureLoader
        {
        public:
            static slag::Texture* loadTexture(unsigned char* textureData, unsigned long& readLength, slag::Texture::Layout inLayout,slag::BarrierAccess withAccess, slag::PipelineStages syncWork);
            static slag::Texture* loadTexture(unsigned char* textureData, unsigned long& readLength, slag::Texture::Layout inLayout,slag::BarrierAccess withAccess, slag::PipelineStages syncWork, slag::CommandBuffer* commandBuffer);
            static std::vector<unsigned char> toLZ4(slag::Texture* texture, slag::Texture::Layout startingLayout, slag::Texture::Layout endingLayout, slag::BarrierAccess accessAfter, slag::PipelineStages syncAfter);
        };
    } // core
} // crucible

#endif //CRUCIBLE_TEXTURELOADER_H
