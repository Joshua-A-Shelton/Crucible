#ifndef CRUCIBLE_BUFFERUTILS_H
#define CRUCIBLE_BUFFERUTILS_H
#include <vector>

namespace crucible
{
    namespace serialization
    {
        inline void bufferSet(std::vector<unsigned char>& dataBuffer, size_t index, const char* string)
        {
            auto curIndex = index;
            auto stringIndex = 0;
            while (string[stringIndex]!='\0')
            {
                dataBuffer[curIndex] = string[stringIndex];
                curIndex++;
                stringIndex++;
            }
        }

        template<class T> void bufferSet(std::vector<unsigned char>& dataBuffer, size_t index, T value)
        {
            unsigned char* valPtr = reinterpret_cast<unsigned char*>(&value);
            for (int i = 0; i < sizeof(T); i++)
            {
                dataBuffer[index + i] = valPtr[i];
            }
        }

        template<class T> void bufferPushBack(std::vector<unsigned char>& data, T& value)
        {
            unsigned char* valPtr = reinterpret_cast<unsigned char*>(&value);
            for (int i = 0; i < sizeof(T); i++)
            {
                data.push_back(valPtr[i]);
            }
        }
    }
}
#endif //CRUCIBLE_BUFFERUTILS_H