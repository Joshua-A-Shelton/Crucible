#ifndef CRUCIBLE_BUFFERUTILS_H
#define CRUCIBLE_BUFFERUTILS_H
#include <vector>
#include <slag/Slag.h>

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

        inline slag::Buffer* readLZ4ToGPUBuffer(unsigned char* dataStream, slag::Buffer::Accessibility bufferAccessibility, uint64_t* outReadLength)
        {
            unsigned char* currentByte = dataStream;
            slag::Buffer* returnBuffer = nullptr;
            int32_t compressedLength = *(int32_t*)currentByte;
            currentByte+=sizeof(int32_t);
            int32_t uncompressedLength = *(int32_t*)currentByte;
            currentByte+=sizeof(int32_t);
            if constexpr (std::endian::native == std::endian::big)
            {
                boost::endian::big_to_native_inplace(compressedLength);
                boost::endian::big_to_native_inplace(uncompressedLength);
            }
            returnBuffer = slag::Buffer::newBuffer(uncompressedLength,bufferAccessibility);
            if (bufferAccessibility == slag::Buffer::Accessibility::CPU_AND_GPU)
            {
                LZ4_decompress_safe((char*)currentByte,returnBuffer->as<char>(),compressedLength,uncompressedLength);
            }
            else
            {
                std::vector<char> temp(uncompressedLength);
                LZ4_decompress_safe((char*)currentByte,temp.data(),compressedLength,uncompressedLength);
                slag::Semaphore* finished = slag::Semaphore::newSemaphore(0);
                slag::SemaphoreValue signal{.semaphore = finished,.value = 1};
                returnBuffer->update(0,temp.data(),uncompressedLength,nullptr,0,&signal,1);
                finished->waitForValue(1);
                delete finished;
            }
            currentByte+=compressedLength;
            if (outReadLength != nullptr)
            {
                *outReadLength = currentByte-dataStream;
            }
            return returnBuffer;
        }

    }
}
#endif //CRUCIBLE_BUFFERUTILS_H