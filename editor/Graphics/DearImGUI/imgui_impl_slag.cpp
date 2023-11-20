#include "imgui_impl_slag.h"
#include <SDL_syswm.h>

bool ImGui_ImplSlag_Init(slag::Pixels::PixelFormat renderTargetFormat)
{
    ImGuiIO& io = ImGui::GetIO();
    io.BackendRendererUserData = new ImGui_SlagBackEndData();
    auto backEndData = imGui_getSlagBackendData();
    slag::FramebufferDescription description;
    description.addColorTarget(renderTargetFormat);
    backEndData->shader = slag::Shader::create("Graphics/DefaultShaders/dearimgui.vert.spv", "Graphics/DefaultShaders/dearimgui.frag.spv",description);
    backEndData->renderTargetFormat = renderTargetFormat;
    backEndData->sampler = slag::TextureSamplerBuilder().create();

    io.BackendFlags |= ImGuiBackendFlags_RendererHasViewports;
    io.BackendFlags |= ImGuiBackendFlags_RendererHasVtxOffset;

    if (io.ConfigFlags & ImGuiConfigFlags_ViewportsEnable)
    {
        ImGuiPlatformIO& platform_io = ImGui::GetPlatformIO();
        platform_io.Renderer_RenderWindow = ImGui_Slag_RenderWindow;
    }

    ImGuiViewport* main_viewport = ImGui::GetMainViewport();
    main_viewport->RendererUserData = IM_NEW(ImGui_SlagViewPort)();

    if (io.ConfigFlags & ImGuiConfigFlags_ViewportsEnable)
        ImGui_Slag_InitPlatformInterface();


    unsigned char* pixels;
    int width, height;
    io.Fonts->GetTexDataAsRGBA32(&pixels, &width, &height);
    size_t upload_size = width * height * 4 * sizeof(char);
    backEndData->fontAtlas = slag::Texture::create(width,height,slag::Pixels::R8G8B8A8_UNORM,pixels);

    // Store our identifier
    io.Fonts->SetTexID((ImTextureID)backEndData->fontAtlas);


    return true;
}

void ImGui_ImplSlag_Shutdown()
{
    auto backEndData = imGui_getSlagBackendData();
    delete backEndData->shader;
    delete backEndData->sampler;
    delete backEndData->fontAtlas;
    delete backEndData;
    ImGui::DestroyPlatformWindows();
}

void ImGui_ImplSlag_NewFrame()
{
    auto* bd = imGui_getSlagBackendData();
    IM_ASSERT(bd != nullptr && "Did you call ImGui_ImplSlag_Init()?");
    IM_UNUSED(bd);
}

void ImGui_ImplSlag_RenderDrawData(ImDrawData *draw_data, slag::Frame* frame, slag::Shader* shader)
{
// Avoid rendering when minimized, scale coordinates for retina displays (screen coordinates != framebuffer coordinates)
    int fb_width = (int)(draw_data->DisplaySize.x * draw_data->FramebufferScale.x);
    int fb_height = (int)(draw_data->DisplaySize.y * draw_data->FramebufferScale.y);
    if (fb_width <= 0 || fb_height <= 0 || draw_data->TotalVtxCount ==0)
        return;

    auto backEndData = imGui_getSlagBackendData();
    if(shader == nullptr)
    {
        shader = backEndData->shader;
    }

    auto commandBuffer = frame->getCommandBuffer();
    auto allocator = frame->getUniformSetDataAllocator();

    // Allocate array to store enough vertex/index buffers. Each unique viewport gets its own storage.
    ImGui_SlagViewPort* viewport_renderer_data = (ImGui_SlagViewPort*)draw_data->OwnerViewport->RendererUserData;
    IM_ASSERT(viewport_renderer_data != nullptr);

    ImGui_SlagWindowRenderBuffers* windowRenderBuffers = &viewport_renderer_data->RenderBuffers;
    /*if (windowRenderBuffers->FrameRenderBuffers == nullptr)
    {
        windowRenderBuffers->Index = 0;
        //windowRenderBuffers->Count = v->ImageCount;
        windowRenderBuffers->FrameRenderBuffers = (ImGui_SlagFrameRenderBuffers*)IM_ALLOC(sizeof(ImGui_SlagFrameRenderBuffers) * windowRenderBuffers->Count);
        memset(windowRenderBuffers->FrameRenderBuffers, 0, sizeof(ImGui_SlagFrameRenderBuffers) * windowRenderBuffers->Count);
    }*/
    //IM_ASSERT(wrb->Count == v->ImageCount);
    //windowRenderBuffers->Index = (windowRenderBuffers->Index + 1) % windowRenderBuffers->Count;
    //ImGui_SlagFrameRenderBuffers* rb = &windowRenderBuffers->FrameRenderBuffers[windowRenderBuffers->Index];

    std::vector<ImDrawVert> verts;
    std::vector<ImDrawIdx> idxs;


    slag::VertexBuffer* vertexBuffer = frame->getVertexBufferResource("ImGuiVerts");
    slag::IndexBuffer* indexBuffer = frame->getIndexBufferResource("ImGuiIndexes");
    if (draw_data->TotalVtxCount > 0)
    {
        // Create or resize the vertex/index buffers
        size_t vertex_size = draw_data->TotalVtxCount * sizeof(ImDrawVert);
        if(vertexBuffer->size() < vertex_size)
        {
            vertexBuffer->rebuild(vertex_size);
        }
        verts.resize(draw_data->TotalVtxCount);
        size_t index_size = draw_data->TotalIdxCount * sizeof(ImDrawIdx);
        if(indexBuffer->size() < index_size)
        {
            indexBuffer->rebuild(index_size);
        }
        idxs.resize(draw_data->TotalIdxCount);


        // Upload vertex/index data into a single contiguous GPU buffer
        ImDrawVert* vtx_dst = verts.data();
        ImDrawIdx* idx_dst = idxs.data();
        for (int n = 0; n < draw_data->CmdListsCount; n++)
        {
            const ImDrawList* cmd_list = draw_data->CmdLists[n];
            memcpy(vtx_dst, cmd_list->VtxBuffer.Data, cmd_list->VtxBuffer.Size * sizeof(ImDrawVert));
            memcpy(idx_dst, cmd_list->IdxBuffer.Data, cmd_list->IdxBuffer.Size * sizeof(ImDrawIdx));
            vtx_dst += cmd_list->VtxBuffer.Size;
            idx_dst += cmd_list->IdxBuffer.Size;
        }

        vertexBuffer->update(0,verts.data(),vertex_size);
        indexBuffer->update(0,idxs.data(),index_size);
    }

    // Setup desired Vulkan state
    ImGui_Slag_SetupRenderState(draw_data, shader, commandBuffer, vertexBuffer, indexBuffer, fb_width, fb_height);

    // Will project scissor/clipping rectangles into framebuffer space
    ImVec2 clip_off = draw_data->DisplayPos;         // (0,0) unless using multi-viewports
    ImVec2 clip_scale = draw_data->FramebufferScale; // (1,1) unless using retina display which are often (2,2)

    // Render command lists
    // (Because we merged all buffers into a single one, we maintain our own offset into them)
    int global_vtx_offset = 0;
    int global_idx_offset = 0;
    for (int n = 0; n < draw_data->CmdListsCount; n++)
    {
        const ImDrawList* cmd_list = draw_data->CmdLists[n];
        for (int cmd_i = 0; cmd_i < cmd_list->CmdBuffer.Size; cmd_i++)
        {
            const ImDrawCmd* pcmd = &cmd_list->CmdBuffer[cmd_i];
            if (pcmd->UserCallback != nullptr)
            {
                // User callback, registered via ImDrawList::AddCallback()
                // (ImDrawCallback_ResetRenderState is a special callback value used by the user to request the renderer to reset render state.)
                if (pcmd->UserCallback == ImDrawCallback_ResetRenderState)
                    ImGui_Slag_SetupRenderState(draw_data, shader, commandBuffer, vertexBuffer, indexBuffer, fb_width, fb_height);
                else
                    pcmd->UserCallback(cmd_list, pcmd);
            }
            else
            {
                // Project scissor/clipping rectangles into framebuffer space
                ImVec2 clip_min((pcmd->ClipRect.x - clip_off.x) * clip_scale.x, (pcmd->ClipRect.y - clip_off.y) * clip_scale.y);
                ImVec2 clip_max((pcmd->ClipRect.z - clip_off.x) * clip_scale.x, (pcmd->ClipRect.w - clip_off.y) * clip_scale.y);

                // Clamp to viewport as vkCmdSetScissor() won't accept values that are off bounds
                if (clip_min.x < 0.0f) { clip_min.x = 0.0f; }
                if (clip_min.y < 0.0f) { clip_min.y = 0.0f; }
                if (clip_max.x > fb_width) { clip_max.x = (float)fb_width; }
                if (clip_max.y > fb_height) { clip_max.y = (float)fb_height; }
                if (clip_max.x <= clip_min.x || clip_max.y <= clip_min.y)
                    continue;

                // Apply scissor/clipping rectangle
                slag::Rectangle scissor;
                scissor.offset.x = (int32_t)(clip_min.x);
                scissor.offset.y = (int32_t)(clip_min.y);
                scissor.extent.width = (uint32_t)(clip_max.x - clip_min.x);
                scissor.extent.height = (uint32_t)(clip_max.y - clip_min.y);
                commandBuffer->setScissors(scissor);

                // Bind DescriptorSet with font or user texture
                slag::UniformSetData slot0(shader->getUniformSet(0),allocator);
                slot0.setTexture(0, static_cast<slag::Texture*>(pcmd->TextureId), backEndData->sampler, slag::Texture::Layout::SHADER_RESOURCE);
                commandBuffer->bindUniformSetData(shader,slot0);

                // Draw
                commandBuffer->drawIndexed(pcmd->ElemCount, 1, pcmd->IdxOffset + global_idx_offset, pcmd->VtxOffset + global_vtx_offset, 0);
            }
        }
        global_idx_offset += cmd_list->IdxBuffer.Size;
        global_vtx_offset += cmd_list->VtxBuffer.Size;
    }

    // Note: at this point both vkCmdSetViewport() and vkCmdSetScissor() have been called.
    // Our last values will leak into user/application rendering IF:
    // - Your app uses a pipeline with VK_DYNAMIC_STATE_VIEWPORT or VK_DYNAMIC_STATE_SCISSOR dynamic state
    // - And you forgot to call vkCmdSetViewport() and vkCmdSetScissor() yourself to explicitly set that state.
    // If you use VK_DYNAMIC_STATE_VIEWPORT or VK_DYNAMIC_STATE_SCISSOR you are responsible for setting the values before rendering.
    // In theory we should aim to backup/restore those values but I am not sure this is possible.
    // We perform a call to vkCmdSetScissor() to set back a full viewport which is likely to fix things for 99% users but technically this is not perfect. (See github #4644)
    slag::Rectangle scissor = { { 0, 0 }, { (uint32_t)fb_width, (uint32_t)fb_height } };
    commandBuffer->setScissors(scissor);
}

ImGui_SlagBackEndData *imGui_getSlagBackendData()
{
    return (ImGui_SlagBackEndData*)ImGui::GetIO().BackendRendererUserData;
}

void ImGui_Slag_RenderWindow(ImGuiViewport *viewport, void *)
{
    ImGui_SlagViewPort* vd = (ImGui_SlagViewPort*)viewport->RendererUserData;
    auto swapchain = vd->window;
    if(auto frame = swapchain->next())
    {
        frame->begin();
        auto commandBuffer = frame->getCommandBuffer();
        auto backEndData = imGui_getSlagBackendData();
        slag::Rectangle view{{0,0},{swapchain->width(),swapchain->height()}};
        slag::Attachment colorAttachment{.texture = frame->getBackBuffer(), .clearOnLoad = true, .clear={0.5,0.5,0.5,0.5}};
        commandBuffer->setTargetFramebuffer(view,&colorAttachment,1);
        commandBuffer->bindShader(backEndData->shader);
        ImGui_ImplSlag_RenderDrawData(viewport->DrawData,frame, nullptr);
        commandBuffer->endTargetFramebuffer();
        frame->end();
    }
}

void ImGui_Slag_SetupRenderState(ImDrawData *draw_data, slag::Shader *pipeline, slag::CommandBuffer *command_buffer, slag::VertexBuffer* vertexBuffer, slag:: IndexBuffer* indexBuffer, int fb_width, int fb_height)
{
    auto backEndData = ImGui_SlagBackEndData();

    // Bind pipeline:
    command_buffer->bindShader(pipeline);

    // Bind Vertex And Index Buffer:
    if (draw_data->TotalVtxCount > 0)
    {
        command_buffer->bindVertexBuffer(vertexBuffer);
        command_buffer->bindIndexBuffer(indexBuffer,sizeof(ImDrawIdx) == 2 ? slag::GraphicsTypes::UINT16 : slag::GraphicsTypes::UINT32);
    }

    // Setup viewport:
    {
        slag::Rectangle viewport;
        viewport.offset.x = 0;
        viewport.offset.y = 0;
        viewport.extent.width = (float)fb_width;
        viewport.extent.height = (float)fb_height;
        //viewport.minDepth = 0.0f;
        //viewport.maxDepth = 1.0f;
        command_buffer->setViewport(viewport);
    }

    // Setup scale and translation:
    // Our visible imgui space lies from draw_data->DisplayPps (top left) to draw_data->DisplayPos+data_data->DisplaySize (bottom right). DisplayPos is (0,0) for single viewport apps.
    {
        struct pushConstants
        {
            float scale[2];
            float translate[2];
        };
        pushConstants pc;
        pc.scale[0] = 2.0f / draw_data->DisplaySize.x;
        pc.scale[1] = 2.0f / draw_data->DisplaySize.y;
        pc.translate[0] = -1.0f - draw_data->DisplayPos.x * pc.scale[0];
        pc.translate[1] = -1.0f - draw_data->DisplayPos.y * pc.scale[1];
        command_buffer->pushConstants(pipeline,pipeline->getPushConstantRange(0),&pc);
    }
}

void ImGui_Slag_InitPlatformInterface()
{
    ImGuiPlatformIO& platform_io = ImGui::GetPlatformIO();
    if (ImGui::GetIO().ConfigFlags & ImGuiConfigFlags_ViewportsEnable)
        IM_ASSERT(platform_io.Platform_CreateVkSurface != nullptr && "Platform needs to setup the CreateVkSurface handler.");
    platform_io.Renderer_CreateWindow = ImGui_Slag_CreateWindow;
    platform_io.Renderer_DestroyWindow = ImGui_Slag_DestroyWindow;
    platform_io.Renderer_SetWindowSize = ImGui_Slag_SetWindowSize;
    platform_io.Renderer_RenderWindow = ImGui_Slag_RenderWindow;
    platform_io.Platform_SetWindowPos = ImGui_Slag_SetWindowPos;
    platform_io.Renderer_SwapBuffers = nullptr;
}


void ImGui_Slag_CreateWindow(ImGuiViewport* viewport)
{
    ImGui_SlagViewPort* viewportData = new ImGui_SlagViewPort();
    viewport->RendererUserData = viewportData;
    auto window = SDL_CreateWindow("Crucible",viewport->Pos.x,viewport->Pos.y,viewport->Size.x,viewport->Size.y,SDL_WindowFlags::SDL_WINDOW_VULKAN | SDL_WindowFlags::SDL_WINDOW_BORDERLESS | SDL_WindowFlags::SDL_WINDOW_RESIZABLE);
    slag::PlatformData pd;
    SDL_SysWMinfo wmInfo;
    SDL_VERSION(&wmInfo.version);
    SDL_GetWindowWMInfo(window, &wmInfo);
#ifdef _WIN32
    pd.nativeWindowHandle = wmInfo.info.win.window;
#elif __linux
    pd.nativeWindowHandle = reinterpret_cast<void*>(wmInfo.info.x11.window);
    pd.nativeDisplayType = wmInfo.info.x11.display;
#endif

    viewportData->window = slag::SwapchainBuilder(pd).addVertexBufferResource("ImGuiVerts",{15000,slag::Buffer::Usage::GPU}).addIndexBufferResource("ImGuiIndexes",{15000,slag::Buffer::Usage::GPU}).create();
    viewportData->handle = window;
    viewportData->WindowOwned = true;
}

void ImGui_Slag_DestroyWindow(ImGuiViewport* viewport)
{
    // The main viewport (owned by the application) will always have RendererUserData == 0 since we didn't create the data for it.
    if (ImGui_SlagViewPort* viewportData = (ImGui_SlagViewPort*)viewport->RendererUserData)
    {
        if (viewportData->WindowOwned)
        {
            delete viewportData->window;
            SDL_DestroyWindow(viewportData->handle);
        }
        delete viewportData;
    }
    viewport->RendererUserData = nullptr;
}

void ImGui_Slag_SetWindowSize(ImGuiViewport* viewport, ImVec2 size)
{
    ImGui_SlagViewPort* viewportData = (ImGui_SlagViewPort*)viewport->RendererUserData;
    if (viewportData == nullptr) // This is nullptr for the main viewport (which is left to the user/app to handle)
        return;
    SDL_SetWindowSize(viewportData->handle,size.x,size.y);
    viewportData->window->resize(size.x,size.y);
}

void ImGui_Slag_SetWindowPos(ImGuiViewport* viewport, ImVec2 pos)
{
    ImGui_SlagViewPort* viewportData = (ImGui_SlagViewPort*)viewport->RendererUserData;
    if (viewportData == nullptr) // This is nullptr for the main viewport (which is left to the user/app to handle)
        return;
    SDL_SetWindowPosition(viewportData->handle,pos.x,pos.y);
}



