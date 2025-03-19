#ifndef CRUCIBLE_SERIALIZER_H
#define CRUCIBLE_SERIALIZER_H
#include <vector>

namespace crucible
{
    namespace core
    {
        template<class T> void bufferInsert(std::vector<unsigned char>& data, T& value)
        {
            unsigned char* valPtr = reinterpret_cast<unsigned char*>(&value);
            for (int i = 0; i < sizeof(T); i++)
            {
                data.push_back(valPtr[i]);
            }
        }
    }
}
#endif //CRUCIBLE_SERIALIZER_H
