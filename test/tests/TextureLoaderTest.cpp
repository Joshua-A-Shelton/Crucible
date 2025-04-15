#include "gtest/gtest.h"
#include <crucible/core/TextureLoader.h>

#include "slag/SlagLib.h"

void testTexture(std::unique_ptr<slag::Texture>& texture, slag::Texture::Layout layout)
{
    auto data = crucible::core::TextureLoader::toLZ4(texture.get(),layout,slag::Texture::TRANSFER_SOURCE,slag::BarrierAccessFlags::TRANSFER_READ,slag::PipelineStageFlags::ALL_COMMANDS);
    unsigned long readSize = 0;
    auto copy = std::unique_ptr<slag::Texture>(crucible::core::TextureLoader::loadTexture(data.data(),readSize,slag::Texture::TRANSFER_SOURCE,slag::BarrierAccessFlags::TRANSFER_READ,slag::PipelineStageFlags::ALL_COMMANDS));
    auto data2 = crucible::core::TextureLoader::toLZ4(copy.get(),slag::Texture::TRANSFER_SOURCE,slag::Texture::GENERAL,slag::BarrierAccessFlags::TRANSFER_READ,slag::PipelineStageFlags::ALL_COMMANDS);
    GTEST_ASSERT_EQ(texture->format(),copy->format());
    GTEST_ASSERT_EQ(texture->width(),copy->width());
    GTEST_ASSERT_EQ(texture->height(),copy->height());
    GTEST_ASSERT_EQ(texture->layers(),copy->layers());
    GTEST_ASSERT_EQ(texture->mipLevels(),copy->mipLevels());
    GTEST_ASSERT_EQ(texture->type(),copy->type());
    GTEST_ASSERT_EQ(data.size(),data2.size());
    for (auto i=0; i< data.size(); i++)
    {
        GTEST_ASSERT_EQ(data[i],data2[i]);
    }
}
TEST(TextureLoader, Color2d)
{
    auto texture = std::unique_ptr<slag::Texture>(slag::Texture::newTexture("resources/cube.png",slag::Pixels::R8G8B8A8_UINT,1,slag::TextureUsageFlags::SAMPLED_IMAGE,slag::Texture::TRANSFER_SOURCE));
    testTexture(texture,slag::Texture::TRANSFER_SOURCE);
}

TEST(TextureLoader, Color2dMultiMip)
{
    auto copyTexture = std::unique_ptr<slag::Texture>(slag::Texture::newTexture("resources/cube.png",slag::Pixels::R8G8B8A8_UNORM,1,slag::TextureUsageFlags::SAMPLED_IMAGE,slag::Texture::TRANSFER_SOURCE));
    auto texture = std::unique_ptr<slag::Texture>(slag::Texture::newTexture(slag::Pixels::R32G32B32A32_FLOAT,slag::Texture::TEXTURE_2D,100,100,4,1,1,slag::TextureUsageFlags::SAMPLED_IMAGE));
    auto commandBuffer = std::unique_ptr<slag::CommandBuffer>(slag::CommandBuffer::newCommandBuffer(slag::GpuQueue::GRAPHICS));

    commandBuffer->begin();
    commandBuffer->insertBarrier(slag::ImageBarrier
        {
            .texture = texture.get(),
            .baseLayer = 0,
            .layerCount = texture->layers(),
            .baseMipLevel = 0,
            .mipCount = texture->mipLevels(),
            .oldLayout = slag::Texture::UNDEFINED,
            .newLayout = slag::Texture::TRANSFER_DESTINATION,
            .accessBefore = slag::BarrierAccessFlags::NONE,
            .accessAfter = slag::BarrierAccessFlags::TRANSFER_WRITE,
            .syncBefore = slag::PipelineStageFlags::NONE,
            .syncAfter = slag::PipelineStageFlags::TRANSFER,
        });
    commandBuffer->blit(copyTexture.get(),slag::Texture::TRANSFER_SOURCE,0,0,slag::Rectangle{{0,0},{copyTexture->width(),copyTexture->height()}},texture.get(),slag::Texture::TRANSFER_DESTINATION,0,0,slag::Rectangle{{0,0},{texture->width(),texture->height()}},slag::Sampler::NEAREST);
    commandBuffer->updateMipChain(texture.get(),0,slag::Texture::TRANSFER_DESTINATION,slag::Texture::TRANSFER_SOURCE,slag::Texture::TRANSFER_DESTINATION,slag::Texture::TRANSFER_SOURCE,slag::PipelineStageFlags::TRANSFER,slag::PipelineStageFlags::ALL_GRAPHICS);
    commandBuffer->end();

    slag::SlagLib::graphicsCard()->graphicsQueue()->submit(commandBuffer.get());
    commandBuffer->waitUntilFinished();
    testTexture(texture,slag::Texture::TRANSFER_SOURCE);
}

TEST(TextureLoader, Color2dMultiLayerMultiMip)
{
    auto copyTexture1 = std::unique_ptr<slag::Texture>(slag::Texture::newTexture("resources/cube.png",slag::Pixels::R8G8B8A8_UNORM,1,slag::TextureUsageFlags::SAMPLED_IMAGE,slag::Texture::TRANSFER_SOURCE));
    auto copyTexture2 = std::unique_ptr<slag::Texture>(slag::Texture::newTexture("resources/solid-color.png",slag::Pixels::R8G8B8A8_UNORM,1,slag::TextureUsageFlags::SAMPLED_IMAGE,slag::Texture::TRANSFER_SOURCE));

    auto texture = std::unique_ptr<slag::Texture>(slag::Texture::newTexture(slag::Pixels::R32G32B32A32_FLOAT,slag::Texture::TEXTURE_2D,100,100,4,2,1,slag::TextureUsageFlags::SAMPLED_IMAGE));
    auto commandBuffer = std::unique_ptr<slag::CommandBuffer>(slag::CommandBuffer::newCommandBuffer(slag::GpuQueue::GRAPHICS));

    commandBuffer->begin();
    commandBuffer->insertBarrier(slag::ImageBarrier
        {
            .texture = texture.get(),
            .baseLayer = 0,
            .layerCount = texture->layers(),
            .baseMipLevel = 0,
            .mipCount = texture->mipLevels(),
            .oldLayout = slag::Texture::UNDEFINED,
            .newLayout = slag::Texture::TRANSFER_DESTINATION,
            .accessBefore = slag::BarrierAccessFlags::NONE,
            .accessAfter = slag::BarrierAccessFlags::TRANSFER_WRITE,
            .syncBefore = slag::PipelineStageFlags::NONE,
            .syncAfter = slag::PipelineStageFlags::TRANSFER,
        });
    commandBuffer->blit(copyTexture1.get(),slag::Texture::TRANSFER_SOURCE,0,0,slag::Rectangle{{0,0},{copyTexture1->width(),copyTexture1->height()}},texture.get(),slag::Texture::TRANSFER_DESTINATION,0,0,slag::Rectangle{{0,0},{texture->width(),texture->height()}},slag::Sampler::NEAREST);
    commandBuffer->blit(copyTexture2.get(),slag::Texture::TRANSFER_SOURCE,0,0,slag::Rectangle{{0,0},{copyTexture2->width(),copyTexture2->height()}},texture.get(),slag::Texture::TRANSFER_DESTINATION,1,0,slag::Rectangle{{0,0},{texture->width(),texture->height()}},slag::Sampler::NEAREST);

    commandBuffer->updateMipChain(texture.get(),0,slag::Texture::TRANSFER_DESTINATION,slag::Texture::TRANSFER_SOURCE,slag::Texture::TRANSFER_DESTINATION,slag::Texture::TRANSFER_SOURCE,slag::PipelineStageFlags::TRANSFER,slag::PipelineStageFlags::ALL_GRAPHICS);
    commandBuffer->end();

    slag::SlagLib::graphicsCard()->graphicsQueue()->submit(commandBuffer.get());
    commandBuffer->waitUntilFinished();
    testTexture(texture,slag::Texture::TRANSFER_SOURCE);
}

TEST(TextureLoader, Color1D)
{
    struct TestColor
    {
        unsigned char r;
        unsigned char g;
        unsigned char b;
        unsigned char a;
    };
    std::vector<TestColor> colors(5,{255,0,120,255});
    auto buffer = std::unique_ptr<slag::Buffer>(slag::Buffer::newBuffer(colors.data(),colors.size()*sizeof(TestColor),slag::Buffer::CPU_AND_GPU,slag::Buffer::DATA_BUFFER));

    auto texture = std::unique_ptr<slag::Texture>(slag::Texture::newTexture(slag::Pixels::R8G8B8A8_UNORM,slag::Texture::TEXTURE_1D,5,1,1,1,1,slag::TextureUsageFlags::SAMPLED_IMAGE));
    auto commandBuffer = std::unique_ptr<slag::CommandBuffer>(slag::CommandBuffer::newCommandBuffer(slag::GpuQueue::GRAPHICS));
    commandBuffer->begin();
    commandBuffer->insertBarrier(slag::ImageBarrier
        {
            .texture = texture.get(),
            .oldLayout = slag::Texture::UNDEFINED,
            .newLayout = slag::Texture::TRANSFER_DESTINATION,
            .accessBefore = slag::BarrierAccessFlags::NONE,
            .accessAfter = slag::BarrierAccessFlags::TRANSFER_WRITE,
            .syncBefore = slag::PipelineStageFlags::NONE,
            .syncAfter = slag::PipelineStageFlags::TRANSFER,
        });
    commandBuffer->copyBufferToImage(buffer.get(),0,texture.get(),slag::Texture::TRANSFER_DESTINATION,0,0);
    commandBuffer->insertBarrier(slag::ImageBarrier
        {
            .texture = texture.get(),
            .oldLayout = slag::Texture::TRANSFER_DESTINATION,
            .newLayout = slag::Texture::TRANSFER_SOURCE,
            .accessBefore = slag::BarrierAccessFlags::TRANSFER_WRITE,
            .accessAfter = slag::BarrierAccessFlags::TRANSFER_READ,
            .syncBefore = slag::PipelineStageFlags::TRANSFER,
            .syncAfter = slag::PipelineStageFlags::TRANSFER,
        });
    commandBuffer->end();
    slag::SlagLib::graphicsCard()->graphicsQueue()->submit(commandBuffer.get());
    commandBuffer->waitUntilFinished();
    testTexture(texture,slag::Texture::TRANSFER_SOURCE);
}

TEST(TextureLoader, Depth)
{
    std::vector<float> depth(25*25*sizeof(float),1.0f);
    auto texture = std::unique_ptr<slag::Texture>(slag::Texture::newTexture(depth.data(),slag::Pixels::D32_FLOAT,25,25,1,slag::TextureUsageFlags::DEPTH_STENCIL_ATTACHMENT,slag::Texture::TRANSFER_SOURCE));
    testTexture(texture,slag::Texture::TRANSFER_SOURCE);
}

TEST(TextureLoader, CubeMap)
{
    auto copyTexture1 = std::unique_ptr<slag::Texture>(slag::Texture::newTexture("resources/cube.png",slag::Pixels::R8G8B8A8_UNORM,1,slag::TextureUsageFlags::SAMPLED_IMAGE,slag::Texture::TRANSFER_SOURCE));
    auto copyTexture2 = std::unique_ptr<slag::Texture>(slag::Texture::newTexture("resources/solid-color.png",slag::Pixels::R8G8B8A8_UNORM,1,slag::TextureUsageFlags::SAMPLED_IMAGE,slag::Texture::TRANSFER_SOURCE));

    auto texture = std::unique_ptr<slag::Texture>(slag::Texture::newTexture(slag::Pixels::R32G32B32A32_FLOAT,slag::Texture::CUBE_MAP,100,100,4,6,1,slag::TextureUsageFlags::SAMPLED_IMAGE));
    auto commandBuffer = std::unique_ptr<slag::CommandBuffer>(slag::CommandBuffer::newCommandBuffer(slag::GpuQueue::GRAPHICS));

    commandBuffer->begin();
    commandBuffer->insertBarrier(slag::ImageBarrier
        {
            .texture = texture.get(),
            .baseLayer = 0,
            .layerCount = texture->layers(),
            .baseMipLevel = 0,
            .mipCount = texture->mipLevels(),
            .oldLayout = slag::Texture::UNDEFINED,
            .newLayout = slag::Texture::TRANSFER_DESTINATION,
            .accessBefore = slag::BarrierAccessFlags::NONE,
            .accessAfter = slag::BarrierAccessFlags::TRANSFER_WRITE,
            .syncBefore = slag::PipelineStageFlags::NONE,
            .syncAfter = slag::PipelineStageFlags::TRANSFER,
        });
    commandBuffer->blit(copyTexture1.get(),slag::Texture::TRANSFER_SOURCE,0,0,slag::Rectangle{{0,0},{copyTexture1->width(),copyTexture1->height()}},texture.get(),slag::Texture::TRANSFER_DESTINATION,0,0,slag::Rectangle{{0,0},{texture->width(),texture->height()}},slag::Sampler::NEAREST);
    commandBuffer->blit(copyTexture2.get(),slag::Texture::TRANSFER_SOURCE,0,0,slag::Rectangle{{0,0},{copyTexture2->width(),copyTexture2->height()}},texture.get(),slag::Texture::TRANSFER_DESTINATION,1,0,slag::Rectangle{{0,0},{texture->width(),texture->height()}},slag::Sampler::NEAREST);
    commandBuffer->blit(copyTexture1.get(),slag::Texture::TRANSFER_SOURCE,0,0,slag::Rectangle{{0,0},{copyTexture1->width(),copyTexture1->height()}},texture.get(),slag::Texture::TRANSFER_DESTINATION,2,0,slag::Rectangle{{0,0},{texture->width(),texture->height()}},slag::Sampler::NEAREST);
    commandBuffer->blit(copyTexture2.get(),slag::Texture::TRANSFER_SOURCE,0,0,slag::Rectangle{{0,0},{copyTexture2->width(),copyTexture2->height()}},texture.get(),slag::Texture::TRANSFER_DESTINATION,3,0,slag::Rectangle{{0,0},{texture->width(),texture->height()}},slag::Sampler::NEAREST);
    commandBuffer->blit(copyTexture1.get(),slag::Texture::TRANSFER_SOURCE,0,0,slag::Rectangle{{0,0},{copyTexture1->width(),copyTexture1->height()}},texture.get(),slag::Texture::TRANSFER_DESTINATION,4,0,slag::Rectangle{{0,0},{texture->width(),texture->height()}},slag::Sampler::NEAREST);
    commandBuffer->blit(copyTexture2.get(),slag::Texture::TRANSFER_SOURCE,0,0,slag::Rectangle{{0,0},{copyTexture2->width(),copyTexture2->height()}},texture.get(),slag::Texture::TRANSFER_DESTINATION,5,0,slag::Rectangle{{0,0},{texture->width(),texture->height()}},slag::Sampler::NEAREST);

    commandBuffer->updateMipChain(texture.get(),0,slag::Texture::TRANSFER_DESTINATION,slag::Texture::TRANSFER_SOURCE,slag::Texture::TRANSFER_DESTINATION,slag::Texture::TRANSFER_SOURCE,slag::PipelineStageFlags::TRANSFER,slag::PipelineStageFlags::ALL_GRAPHICS);
    commandBuffer->end();

    slag::SlagLib::graphicsCard()->graphicsQueue()->submit(commandBuffer.get());
    commandBuffer->waitUntilFinished();
    testTexture(texture,slag::Texture::TRANSFER_SOURCE);
}