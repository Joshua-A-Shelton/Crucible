#ifndef CRUCIBLE_SERIALIZER_H
#define CRUCIBLE_SERIALIZER_H

#include <string>
#include <vector>

namespace crucible
{
    namespace serialization
    {
        class Serializer
        {
        public:
            static std::string read(std::istream& stream, size_t length);
            static int8_t readInt8(std::istream& stream);
            static uint8_t readUInt8(std::istream& stream);
            static int16_t readInt16(std::istream& stream);
            static uint16_t readUInt16(std::istream& stream);
            static int32_t readInt32(std::istream& stream);
            static uint32_t readUInt32(std::istream& stream);
            static int64_t readInt64(std::istream& stream);
            static uint64_t readUInt64(std::istream& stream);
            static std::vector<char> readBytes(std::istream& stream, size_t length);
            static void skipWhiteSpace(std::istream& stream);
        };
    } // serialization
} // crucible

#endif //CRUCIBLE_SERIALIZER_H
