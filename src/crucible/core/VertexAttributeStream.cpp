#include "VertexAttributeStream.h"

namespace crucible
{
    namespace core
    {
        VertexAttributeStream::VertexAttributeStream(void* data, size_t dataLength, slag::Buffer::Accessibility accessibility)
        {
            _data = slag::Buffer::newBuffer(data,dataLength,accessibility,slag::Buffer::Usage::VERTEX_BUFFER);
        }

        VertexAttributeStream::~VertexAttributeStream()
        {
            delete _data;
        }

        VertexAttributeStream::VertexAttributeStream(VertexAttributeStream&& from)
        {
            move(from);
        }

        VertexAttributeStream& VertexAttributeStream::operator=(VertexAttributeStream&& from)
        {
            move(from);
            return *this;
        }

        slag::Buffer* VertexAttributeStream::data()const
        {
            return  _data;
        }

        void VertexAttributeStream::move(VertexAttributeStream& from)
        {
            std::swap(_data,from._data);
        }
    } // core
} // crucible