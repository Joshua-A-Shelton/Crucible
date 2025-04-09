#include "gtest/gtest.h"
#include <crucible/core/VirtualBuffer.h>
using namespace crucible::core;
TEST(VirtualBufferTest, Creation)
{
    VirtualBuffer virtualUniformBuffer(10,slag::Buffer::UNIFORM_BUFFER);
    GTEST_ASSERT_EQ(virtualUniformBuffer.capacity(), 64);
    GTEST_ASSERT_EQ(virtualUniformBuffer.usage(), 0);

    VirtualBuffer virtualUniformBuffer2(100,slag::Buffer::UNIFORM_BUFFER);
    GTEST_ASSERT_EQ(virtualUniformBuffer2.capacity(), 128);
    GTEST_ASSERT_EQ(virtualUniformBuffer2.usage(), 0);

    VirtualBuffer virtualStorageBuffer(10,slag::Buffer::STORAGE_BUFFER);
    GTEST_ASSERT_EQ(virtualStorageBuffer.capacity(), 64);
    GTEST_ASSERT_EQ(virtualStorageBuffer.usage(), 0);

    VirtualBuffer virtualStorageBuffer2(100,slag::Buffer::STORAGE_BUFFER);
    GTEST_ASSERT_EQ(virtualStorageBuffer2.capacity(), 100);
    GTEST_ASSERT_EQ(virtualStorageBuffer2.usage(), 0);
}

TEST(VirtualBufferTest, Extend)
{
    VirtualBuffer virtualUniformBuffer(64,slag::Buffer::UNIFORM_BUFFER);
    GTEST_ASSERT_EQ(virtualUniformBuffer.capacity(), 64);
    std::vector<unsigned char> data(64, 255);
    virtualUniformBuffer.write(data.data(), data.size());
    virtualUniformBuffer.write(data.data(), data.size());
    GTEST_ASSERT_TRUE(virtualUniformBuffer.capacity() == 128);
    virtualUniformBuffer.write(data.data(), data.size());
    GTEST_ASSERT_TRUE(virtualUniformBuffer.capacity() == 192);
    virtualUniformBuffer.write(data.data(), data.size());
    GTEST_ASSERT_TRUE(virtualUniformBuffer.capacity() == 320);

    VirtualBuffer virtualStorageBuffer(100,slag::Buffer::STORAGE_BUFFER);
    GTEST_ASSERT_EQ(virtualStorageBuffer.capacity(), 100);
    virtualStorageBuffer.write(data.data(), 50);
    virtualStorageBuffer.write(data.data(), 50);
    virtualStorageBuffer.write(data.data(), 20);
    GTEST_ASSERT_TRUE(virtualStorageBuffer.capacity() == 150);
    virtualStorageBuffer.write(data.data(), data.size());
    GTEST_ASSERT_TRUE(virtualStorageBuffer.capacity() == 225);
}

TEST(VirtualBufferTest, Reset)
{
    VirtualBuffer virtualUniformBuffer(64,slag::Buffer::UNIFORM_BUFFER);
    std::vector<unsigned char> data(200, 255);
    virtualUniformBuffer.write(data.data(), data.size());
    virtualUniformBuffer.write(data.data(), data.size());
    auto capacity = virtualUniformBuffer.capacity();
    GTEST_ASSERT_TRUE(capacity >= 400);
    virtualUniformBuffer.reset();
    auto newCapacity = virtualUniformBuffer.capacity();
    GTEST_ASSERT_EQ(capacity, newCapacity);
    GTEST_ASSERT_EQ(virtualUniformBuffer.usage(), 0);
}

TEST(VirtualBufferTest, Downsize)
{
    VirtualBuffer virtualUniformBuffer(1000,slag::Buffer::UNIFORM_BUFFER);
    std::vector<unsigned char> data(200, 255);
    virtualUniformBuffer.write(data.data(), data.size());
    auto oldUsage = virtualUniformBuffer.usage();
    auto oldCapacity = virtualUniformBuffer.capacity();
    virtualUniformBuffer.reset();
    auto newCapacity = virtualUniformBuffer.capacity();
    GTEST_ASSERT_LT(newCapacity,oldCapacity);
    GTEST_ASSERT_LT(newCapacity,oldUsage*1.5+64);
    GTEST_ASSERT_GE(newCapacity,oldUsage);

    VirtualBuffer virtualStorageBuffer(1000,slag::Buffer::STORAGE_BUFFER);
    virtualStorageBuffer.write(data.data(), data.size());

    oldUsage = virtualStorageBuffer.usage();
    oldCapacity = virtualStorageBuffer.capacity();
    virtualStorageBuffer.reset();
     newCapacity = virtualStorageBuffer.capacity();
    GTEST_ASSERT_LT(newCapacity,oldCapacity);
    GTEST_ASSERT_EQ(newCapacity,oldUsage*1.5);
    GTEST_ASSERT_GE(newCapacity,oldUsage);
}
