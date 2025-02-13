#ifndef CRUCIBLE_VERTEXATTRIBUTESTREAM_H
#define CRUCIBLE_VERTEXATTRIBUTESTREAM_H
#include <slag/SlagLib.h>
namespace crucible
{
    namespace core
    {

        class VertexAttributeStream
        {
        public:
            VertexAttributeStream()=default;
            VertexAttributeStream(void* data, size_t dataLength, slag::Buffer::Accessibility accessibility);
            ~VertexAttributeStream();
            VertexAttributeStream(const VertexAttributeStream&)=delete;
            VertexAttributeStream& operator=(const VertexAttributeStream&)=delete;
            VertexAttributeStream(VertexAttributeStream&& from);
            VertexAttributeStream& operator=(VertexAttributeStream&& from);
            slag::Buffer* data();
        private:
            void move(VertexAttributeStream& from);
            slag::Buffer* _data= nullptr;
        };

    } // core
} // crucible

#endif //CRUCIBLE_VERTEXATTRIBUTESTREAM_H
