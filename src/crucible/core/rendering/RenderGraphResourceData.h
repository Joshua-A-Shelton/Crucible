#ifndef CRUCIBLE_RESOURCEINDATA_H
#define CRUCIBLE_RESOURCEINDATA_H
#include "slag/SlagLib.h"

namespace crucible
{
    namespace core
    {

        class RenderGraphResourceData
        {
        public:
            RenderGraphResourceData(const std::string& name, slag::Texture::Layout layout);
            ~RenderGraphResourceData()=default;
            RenderGraphResourceData(const RenderGraphResourceData&)=default;
            RenderGraphResourceData& operator=(const RenderGraphResourceData&)=default;
            RenderGraphResourceData(RenderGraphResourceData&&)=default;
            RenderGraphResourceData& operator=(RenderGraphResourceData&&)=default;

            const std::string& name()const;
            slag::Texture::Layout layout()const;
        private:
            std::string _name;
            slag::Texture::Layout _layout;
        };

    } // core
} // crucible

#endif //CRUCIBLE_RESOURCEINDATA_H
