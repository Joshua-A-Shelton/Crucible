#include "Serializer.h"

#include <istream>
#include <boost/endian/conversion.hpp>

namespace crucible
{
    namespace serialization
    {
        std::string Serializer::read(std::istream& stream, size_t length)
        {
            std::vector<char> buffer(length);
            buffer[length-1] = '\0';
            stream.read(buffer.data(), length);
            return std::string(buffer.data(), length);
        }

        int8_t Serializer::readInt8(std::istream& stream)
        {
            int8_t value=0;
            stream.read(reinterpret_cast<char*>(&value),sizeof(int8_t));
            boost::endian::big_to_native_inplace(value);
            return value;
        }

        uint8_t Serializer::readUInt8(std::istream& stream)
        {
            uint8_t value=0;
            stream.read(reinterpret_cast<char*>(&value),sizeof(uint8_t));
            boost::endian::big_to_native_inplace(value);
            return value;
        }

        int16_t Serializer::readInt16(std::istream& stream)
        {
            int16_t value=0;
            stream.read(reinterpret_cast<char*>(&value),sizeof(int16_t));
            boost::endian::big_to_native_inplace(value);
            return value;
        }

        uint16_t Serializer::readUInt16(std::istream& stream)
        {
            uint16_t value=0;
            stream.read(reinterpret_cast<char*>(&value),sizeof(uint16_t));
            boost::endian::big_to_native_inplace(value);
            return value;
        }

        int32_t Serializer::readInt32(std::istream& stream)
        {
            int32_t value=0;
            stream.read(reinterpret_cast<char*>(&value),sizeof(int32_t));
            boost::endian::big_to_native_inplace(value);
            return value;
        }

        uint32_t Serializer::readUInt32(std::istream& stream)
        {
            uint32_t value=0;
            stream.read(reinterpret_cast<char*>(&value),sizeof(uint32_t));
            boost::endian::big_to_native_inplace(value);
            return value;
        }

        int64_t Serializer::readInt64(std::istream& stream)
        {
            int64_t value=0;
            stream.read(reinterpret_cast<char*>(&value),sizeof(int64_t));
            boost::endian::big_to_native_inplace(value);
            return value;
        }

        uint64_t Serializer::readUInt64(std::istream& stream)
        {
            uint64_t value=0;
            stream.read(reinterpret_cast<char*>(&value),sizeof(uint64_t));
            boost::endian::big_to_native_inplace(value);
            return value;
        }

        std::vector<char> Serializer::readBytes(std::istream& stream, size_t length)
        {
            std::vector<char> buffer(length,0);
            stream.read(reinterpret_cast<char*>(&buffer[0]),length);
            return buffer;
        }

        void Serializer::skipWhiteSpace(std::istream& stream)
        {
            char ws;
            while (iswspace(static_cast<char>(stream.peek())))
            {
                stream.read(&ws,1);
            }
        }
    } // serialization
} // crucible