#include "gtest/gtest.h"
#include <crucible/core/VirtualBuffer.h>
using namespace crucible::core;
TEST(VirtualUniformBufferTest, Creation)
{
    VirtualBuffer virtualUniformBuffer(10);
    GTEST_ASSERT_EQ(virtualUniformBuffer.capacity(), 64);
    GTEST_ASSERT_EQ(virtualUniformBuffer.usage(), 0);
}

TEST(VirtualUniformBufferTest, Extend)
{
    VirtualBuffer virtualUniformBuffer(64);
    GTEST_ASSERT_EQ(virtualUniformBuffer.capacity(), 64);
    std::vector<unsigned char> data(64, 255);
    virtualUniformBuffer.write(data.data(), data.size());
    virtualUniformBuffer.write(data.data(), data.size());
    GTEST_ASSERT_TRUE(virtualUniformBuffer.capacity() >= 128);
    virtualUniformBuffer.write(data.data(), data.size());
    GTEST_ASSERT_TRUE(virtualUniformBuffer.capacity() >= 192);
    virtualUniformBuffer.write(data.data(), data.size());
    GTEST_ASSERT_TRUE(virtualUniformBuffer.capacity() >= 288);
}

TEST(VirtualUniformBufferTest, Reset)
{
    VirtualBuffer virtualUniformBuffer(64);
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

TEST(VirtualUniformBufferTest, Downsize)
{
    VirtualBuffer virtualUniformBuffer(1000);
    std::vector<unsigned char> data(200, 255);
    virtualUniformBuffer.write(data.data(), data.size());
    auto oldUsage = virtualUniformBuffer.usage();
    auto oldCapacity = virtualUniformBuffer.capacity();
    virtualUniformBuffer.reset();
    auto newCapacity = virtualUniformBuffer.capacity();
    GTEST_ASSERT_LT(newCapacity,oldCapacity);
    GTEST_ASSERT_LT(newCapacity,oldUsage*1.5+64);
    GTEST_ASSERT_GE(newCapacity,oldUsage);
}
