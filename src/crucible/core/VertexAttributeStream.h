#ifndef CRUCIBLE_VERTEXATTRIBUTESTREAM_H
#define CRUCIBLE_VERTEXATTRIBUTESTREAM_H
#include <slag/SlagLib.h>
namespace crucible
{
    namespace core
    {
        ///Holds the buffer of vertex data
        class VertexAttributeStream
        {
        public:
            VertexAttributeStream()=default;
            /**
             *
             * @param data vertex attribute data location
             * @param dataLength size in bytes of vertex attribute data
             * @param accessibility the accessibility of the buffer
             */
            VertexAttributeStream(void* data, size_t dataLength, slag::Buffer::Accessibility accessibility);
            ~VertexAttributeStream();
            VertexAttributeStream(const VertexAttributeStream&)=delete;
            VertexAttributeStream& operator=(const VertexAttributeStream&)=delete;
            VertexAttributeStream(VertexAttributeStream&& from);
            VertexAttributeStream& operator=(VertexAttributeStream&& from);
            ///The buffer containing vertex attributes
            slag::Buffer* data();
        private:
            void move(VertexAttributeStream& from);
            slag::Buffer* _data= nullptr;
        };

    } // core
} // crucible

#endif //CRUCIBLE_VERTEXATTRIBUTESTREAM_H
