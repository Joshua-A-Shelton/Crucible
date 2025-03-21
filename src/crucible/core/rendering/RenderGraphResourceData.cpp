#include "RenderGraphResourceData.h"

namespace crucible
{
    namespace core
    {
        RenderGraphResourceData::RenderGraphResourceData(const std::string& name, slag::Texture::Layout layout)
        {
            _name = name;
            _layout = layout;
        }

        const std::string& RenderGraphResourceData::name() const
        {
            return _name;
        }

        slag::Texture::Layout RenderGraphResourceData::layout() const
        {
            return _layout;
        }
    } // core
} // crucible
